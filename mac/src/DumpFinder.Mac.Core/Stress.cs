using System.Diagnostics;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Windows StressPlanKind, with the plans Apple Silicon allows: no per-core pinning, and video memory is the unified
/// memory (the RAM stage covers it). Int order = picker index.
/// </summary>
public enum StressPlanKind { Quick = 0, Thorough = 1, Cpu = 2, Ram = 3, Gpu = 4, Disk = 5 }

public enum StressOutcome { Passed, Warning, Failed, Skipped, Stopped, Interrupted }

public sealed record PlannedStage(StressStage Stage, TimeSpan Duration);

public sealed class StressStageResult
{
    public StressStage Stage { get; set; }
    public StressOutcome Outcome { get; set; }
    public string Summary { get; set; } = "";
    public string Advice { get; set; } = "";
    public int DurationSec { get; set; }
}

public sealed class StressRunRecord
{
    public DateTime Time { get; set; }
    public StressPlanKind Plan { get; set; }
    public bool Completed { get; set; }
    public List<StressStageResult> Stages { get; set; } = new();
    public bool AnyFailure => Stages.Any(s => s.Outcome is StressOutcome.Failed or StressOutcome.Interrupted);
}

/// <summary>Written while a stage runs: still there at the next launch means the Mac crashed or the app was quit.</summary>
public sealed class StressMarker
{
    public DateTime Started { get; set; }
    public StressPlanKind Plan { get; set; }
    public StressStage Stage { get; set; }
    public DateTime StageStarted { get; set; }
    public List<StressStageResult> Done { get; set; } = new();
}

/// <summary>Plans, sequencing, verdicts, history (Windows StressPlanner / StressRunner / StressAdvice / History.Stress*).</summary>
public static class StressTest
{
    const string RunsFile = "history/stress.json", MarkerFile = "history/stress_running.json";

    public static List<PlannedStage> Plan(StressPlanKind kind, int minutes)
    {
        var m = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 120));
        return kind switch
        {
            StressPlanKind.Quick => new() { new(StressStage.Cpu, TimeSpan.FromMinutes(3)), new(StressStage.Ram, TimeSpan.FromMinutes(3)),
                                            new(StressStage.Gpu, TimeSpan.FromMinutes(2)), new(StressStage.Disk, TimeSpan.FromMinutes(2)) },
            StressPlanKind.Thorough => new() { new(StressStage.Cpu, TimeSpan.FromMinutes(30)), new(StressStage.Ram, TimeSpan.FromMinutes(45)),
                                               new(StressStage.Gpu, TimeSpan.FromMinutes(30)), new(StressStage.Disk, TimeSpan.FromMinutes(15)) },
            StressPlanKind.Cpu => new() { new(StressStage.Cpu, m) },
            StressPlanKind.Ram => new() { new(StressStage.Ram, m) },
            StressPlanKind.Gpu => new() { new(StressStage.Gpu, m) },
            _ => new() { new(StressStage.Disk, m) },
        };
    }

    public static bool HasDuration(StressPlanKind k) => k is not (StressPlanKind.Quick or StressPlanKind.Thorough);

    public static string PlanName(StressPlanKind k) => k switch
    {
        StressPlanKind.Quick => Loc.S("stress_text.quick_test"),
        StressPlanKind.Thorough => Loc.S("stress_text.thorough_test"),
        StressPlanKind.Cpu => StageName(StressStage.Cpu),
        StressPlanKind.Ram => StageName(StressStage.Ram),
        StressPlanKind.Gpu => StageName(StressStage.Gpu),
        _ => StageName(StressStage.Disk),
    };

    public static string StageName(StressStage s) => Loc.S(s switch
    {
        StressStage.Cpu => "stress_text.cpu_all_cores",
        StressStage.Ram => "mac.stress.unified_memory",
        StressStage.Gpu => "stress_text.gpu_verified_compute",
        _ => "stress_text.ssd_write_read_back",
    });

    public static string PlanHint(StressPlanKind k, int minutes, string diskSize) => k switch
    {
        StressPlanKind.Quick => Loc.F("mac.stress.hint_quick", Duration(Total(Plan(k, minutes))), diskSize),
        StressPlanKind.Thorough => Loc.F("mac.stress.hint_thorough", Duration(Total(Plan(k, minutes))), diskSize),
        StressPlanKind.Cpu => Loc.S("mac.stress.hint_cpu"),
        StressPlanKind.Ram => Loc.S("mac.stress.hint_ram"),
        StressPlanKind.Gpu => Loc.S("mac.stress.hint_gpu"),
        _ => Loc.F("mac.stress.hint_disk", diskSize),
    };

    public static TimeSpan Total(List<PlannedStage> plan) => TimeSpan.FromSeconds(plan.Sum(s => s.Duration.TotalSeconds));

    public static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00}"
        : t.TotalSeconds < 60 ? $"{(int)Math.Round(t.TotalSeconds)} s" : $"{(int)Math.Round(t.TotalMinutes)} min";

    // ---------- Run ----------
    /// <summary>Test hooks: the self-test swaps the engines for instant fakes.</summary>
    public static Func<PlannedStage, CancellationToken, Action<StressTick>, StageOutcome>? Engine { get; set; }
    public static Func<bool>? ThermalProbe { get; set; }

    /// <summary>
    /// Runs the stages in order: keeps the Mac awake (caffeinate), writes the crash marker before each stage, watches
    /// macOS thermal state, turns each outcome into a verdict with advice, saves the run (Windows StressRunner).
    /// </summary>
    public static StressRunRecord Run(StressPlanKind kind, List<PlannedStage> plan, HardwareInfo hw, CancellationToken ct, Action<int, StressTick> tick)
    {
        var record = new StressRunRecord { Time = DateTime.Now, Plan = kind };
        using var awake = Engine is null ? KeepAwake() : null;
        try
        {
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                if (ct.IsCancellationRequested)
                {
                    record.Stages.Add(new StressStageResult { Stage = p.Stage, Outcome = StressOutcome.Skipped, Summary = Loc.S("stress_advice.not_run_test_stopped") });
                    continue;
                }
                AppStore.Write(MarkerFile, new StressMarker { Started = record.Time, Plan = kind, Stage = p.Stage, StageStarted = DateTime.Now, Done = record.Stages.ToList() });
                bool throttled = false;
                using var thermal = new Timer(_ => throttled |= (ThermalProbe ?? DefaultThermal)(), null, 2000, 5000);
                int index = i;
                StageOutcome o;
                try
                {
                    o = (Engine ?? RunEngine(hw))(p, ct, t => tick(index, t));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or OutOfMemoryException or InvalidOperationException)
                {
                    o = new StageOutcome(p.Stage, false, 0, 0, "", TimeSpan.Zero, "error: " + e.Message);
                }
                thermal.Change(Timeout.Infinite, Timeout.Infinite);
                record.Stages.Add(Verdict(o, throttled, ct.IsCancellationRequested));
            }
            record.Completed = !ct.IsCancellationRequested;
            AddRun(record);
            return record;
        }
        finally { ClearMarker(); }
    }

    static Func<PlannedStage, CancellationToken, Action<StressTick>, StageOutcome> RunEngine(HardwareInfo hw) => (p, ct, tick) => p.Stage switch
    {
        StressStage.Cpu => StressEngines.Cpu(p.Duration, ct, t => tick(t with { Detail = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) })),
        StressStage.Ram => StressEngines.Ram(StressEngines.RamBudget(hw.MemoryBytes > 0 ? hw.MemoryBytes : 8L << 30, LiveFreePct()), p.Duration, ct, tick),
        StressStage.Gpu => StressEngines.Gpu(p.Duration, ct, tick),
        _ => StressEngines.Disk(TestFolder, StressEngines.DiskBudget(FreeBytes()), p.Duration, ct, tick),
    };

    public static string TestFolder => Path.Combine(MacPaths.Home, "Library", "Caches", "DumpFinder", "stress");
    public static long FreeBytes() { try { return new System.IO.DriveInfo(MacPaths.Home).AvailableFreeSpace; } catch (Exception e) when (e is IOException or ArgumentException) { return 0; } }
    static int? LiveFreePct() => SystemProbe.ParseMemoryFree(Proc.Run("/usr/bin/memory_pressure", new[] { "-Q" }, 10000).Stdout);
    static bool DefaultThermal() => SystemProbe.ParseTherm(Proc.Run("/usr/bin/pmset", new[] { "-g", "therm" }, 5000).Stdout).Warning;

    /// <summary>caffeinate -i for the life of this process: no idle sleep during the test (Windows SetThreadExecutionState).</summary>
    static IDisposable? KeepAwake()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/caffeinate") { UseShellExecute = false };
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var p = Process.Start(psi);
            return p is null ? null : new KillOnDispose(p);
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>Ends caffeinate with the test (it would otherwise last as long as the app).</summary>
    sealed class KillOnDispose(Process p) : IDisposable
    {
        public void Dispose()
        {
            try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
            p.Dispose();
        }
    }

    /// <summary>Errors → Failed with the stage's advice; macOS throttling → Warning with cooling advice; a stop → Stopped.</summary>
    public static StressStageResult Verdict(StageOutcome o, bool throttled, bool stopped)
    {
        var r = new StressStageResult { Stage = o.Stage, DurationSec = (int)Math.Round(o.Elapsed.TotalSeconds) };
        string key = o.Stage.ToString().ToLowerInvariant();
        if (!o.Ran)
        {
            r.Outcome = StressOutcome.Skipped;
            r.Summary = o.Stopped == "helper" ? Loc.S("mac.stress.gpu_helper_missing") : o.Stopped.StartsWith("error", StringComparison.Ordinal) ? Loc.S("stress_advice.stage_interrupted_by_an_internal_error") + o.Stopped[6..] : Loc.S("stress_advice.not_run_test_stopped");
            return r;
        }
        if (o.Errors > 0)
        {
            r.Outcome = StressOutcome.Failed;
            r.Summary = Loc.F($"mac.stress.{key}_failed", o.Errors);
            r.Advice = Loc.S($"mac.stress.advice_{key}");
            return r;
        }
        r.Outcome = stopped ? StressOutcome.Stopped : throttled ? StressOutcome.Warning : StressOutcome.Passed;
        r.Summary = Loc.F($"mac.stress.{key}_passed", RateText(o.Rate, o.RateUnit), o.Detail);
        if (throttled)
        {
            r.Summary += " " + Loc.S("mac.stress.throttled");
            r.Advice = Loc.S("mac.stress.advice_cooling");
        }
        return r;
    }

    public static string RateText(double rate, string unit) => unit switch
    {
        "MB/s" when rate >= 1024 => $"{rate / 1024:0.0} {Loc.S("gpu_stress.gb_s")}",
        "MB/s" => $"{rate:0} {Loc.S("stress_text.mb_s")}",
        "GFLOPS" => $"{rate / 1000:0.0} TFLOPS",
        _ => $"{rate:N0}/s",
    };

    public static string RunVerdict(StressRunRecord r) => Loc.S(r.AnyFailure ? "stress.instability_found_see_the_stages_in"
        : !r.Completed ? "stress.test_stopped_early_no_errors_up"
        : r.Stages.Any(s => s.Outcome == StressOutcome.Warning) ? "stress.finished_with_points_to_watch" : "stress.no_errors_found");

    public static string RunTone(StressRunRecord r) => r.AnyFailure ? "danger" : !r.Completed || r.Stages.Any(s => s.Outcome == StressOutcome.Warning) ? "warn" : "success";

    // ---------- History and crash recovery ----------
    public static List<StressRunRecord> Runs() => AppStore.Read<List<StressRunRecord>>(RunsFile).OrderByDescending(r => r.Time).ToList();

    static void AddRun(StressRunRecord r)
    {
        var list = AppStore.Read<List<StressRunRecord>>(RunsFile);
        list.Add(r);
        if (list.Count > 50) list = list.Skip(list.Count - 50).ToList();
        AppStore.Write(RunsFile, list);
    }

    static void ClearMarker()
    {
        try { File.Delete(Path.Combine(AppStore.Root, MarkerFile)); } catch (IOException) { }
    }

    /// <summary>
    /// At launch (Windows RecoverInterrupted): a marker left behind means the run never finished. If the Mac booted
    /// after the stage started, it crashed or restarted during that stage (Interrupted); otherwise the app was quit.
    /// Returns the rebuilt run (saved to history) or null.
    /// </summary>
    public static StressRunRecord? RecoverInterrupted(DateTimeOffset? bootTime)
    {
        var path = Path.Combine(AppStore.Root, MarkerFile);
        if (!File.Exists(path)) return null;
        var m = AppStore.Read<StressMarker>(MarkerFile);
        ClearMarker();
        if (m.Started == default) return null;
        bool crashed = bootTime is { } bt && bt.LocalDateTime > m.StageStarted;
        var r = new StressRunRecord { Time = m.Started, Plan = m.Plan, Completed = false, Stages = m.Done };
        r.Stages.Add(new StressStageResult
        {
            Stage = m.Stage,
            Outcome = crashed ? StressOutcome.Interrupted : StressOutcome.Stopped,
            Summary = Loc.S(crashed ? "mac.stress.crashed_during" : "mac.stress.abandoned"),
            Advice = crashed ? Loc.S($"mac.stress.advice_{m.Stage.ToString().ToLowerInvariant()}") + " " + Loc.S("mac.stress.see_crashes") : "",
        });
        AddRun(r);
        return r;
    }
}
