using System.Collections.ObjectModel;
using System.Diagnostics;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>A live tile (Windows StressPage: title, value, sub-line; tone 0 neutral, 1 success, 2 danger).</summary>
public sealed record StressTile(string Title, string Value, string Sub, int Tone)
{
    public bool IsGood => Tone == 1;
    public bool IsBad => Tone == 2;
}

/// <summary>A step chip "1. CPU · all cores": current = accent, done = secondary, after the run = outcome colour.</summary>
public sealed record StepChip(string Text, string Tone) : Toned(Tone);

/// <summary>A stage row of the results card.</summary>
public sealed record StageResultRow(string Icon, string Tone, string Head, string Summary, string Advice) : Toned(Tone)
{
    public bool HasAdvice => Advice.Length > 0;
}

/// <summary>Hardware › Stability test (Windows MainWindow.StressPage.cs).</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<StressTile> StressTiles { get; } = new();
    public ObservableCollection<StepChip> StepChips { get; } = new();
    public ObservableCollection<StageResultRow> StageResults { get; } = new();
    CancellationTokenSource? _stressCts;

    public RelayCommand StartStressCommand => _startStress ??= new(StartStressAsync, () => !IsStressRunning);
    public RelayCommand StopStressCommand => _stopStress ??= new(() => { _stressCts?.Cancel(); StressStatus = Loc.S("main_window.stopping"); return Task.CompletedTask; }, () => IsStressRunning);
    RelayCommand? _startStress, _stopStress;

    // ---------- Plan ----------
    public string[] StressPlans => Enum.GetValues<StressPlanKind>().Select(k =>
        StressTest.PlanName(k) + (k is StressPlanKind.Quick or StressPlanKind.Thorough
            ? $" (≈ {StressTest.Duration(StressTest.Total(StressTest.Plan(k, 10)))}{(k == StressPlanKind.Thorough ? Loc.S("stress.overnight") : "")})" : "")).ToArray();
    int _planIndex, _stressMinutes = 10;
    public int StressPlanIndex { get => _planIndex; set { if (value < 0) return; Set(ref _planIndex, value); RaisePlan(); } }
    public int StressMinutes { get => _stressMinutes; set { Set(ref _stressMinutes, Math.Clamp(value, 1, 120)); RaisePlan(); } }
    StressPlanKind PlanKind => (StressPlanKind)_planIndex;
    public bool StressHasDuration => StressTest.HasDuration(PlanKind);
    public string StressMinutesText => $"{StressMinutes} min";
    public string StressHint => StressTest.PlanHint(PlanKind, StressMinutes, DiskSizeText);
    string DiskSizeText => $"{StressEngines.DiskBudget(Offline ? 100L << 30 : StressTest.FreeBytes()) / 1073741824.0:0.#} GB";

    void RaisePlan()
    {
        foreach (var n in new[] { nameof(StressHasDuration), nameof(StressMinutesText), nameof(StressHint), nameof(StressPlans) }) Raise(n);
        if (!IsStressRunning) ResetLive();
    }

    /// <summary>"This Mac" lines (Windows capabilities: CPU, graphics, free memory, test drive).</summary>
    public string StressMachine
    {
        get
        {
            var hw = _result?.Hardware;
            if (hw is null) return Loc.S("stress.detecting_hardware");
            var free = StressTest.FreeBytes();
            return Loc.F("stress.cpu_cores_threads", hw.PerformanceCores + hw.EfficiencyCores, Environment.ProcessorCount, "NEON") + "\n"
                 + Loc.F("stress.graphics", hw.GpuName.Length > 0 ? hw.GpuName : "Apple GPU", hw.Metal) + (StressEngines.GpuHelper() is null ? " " + Loc.S("mac.stress.gpu_helper_missing") : "") + "\n"
                 + Loc.F("stress.free_memory_of", hw.MemoryFreePct is int p ? $"{hw.MemoryBytes * p / 100 / 1073741824.0:0.#} GB" : "?", $"{hw.MemoryBytes / 1073741824.0:0} GB") + "\n"
                 + Loc.F("stress.test_drive_free", "Macintosh HD", $"{free / 1073741824.0:0} GB");
        }
    }

    // ---------- Live ----------
    bool _stressRunning;
    public bool IsStressRunning
    {
        get => _stressRunning;
        private set { Set(ref _stressRunning, value); Raise(nameof(StressIdle)); StartStressCommand.Refresh(); StopStressCommand.Refresh(); }
    }
    public bool StressIdle => !IsStressRunning;
    string _phase = "", _stressTimer = "", _stressStatus = "", _stressLog = "", _lastTest = "";
    double _stressProgress;
    List<double> _stressSeries = new();
    public string StressPhase { get => _phase; private set => Set(ref _phase, value); }
    public string StressTimer { get => _stressTimer; private set => Set(ref _stressTimer, value); }
    public string StressStatus { get => _stressStatus; private set => Set(ref _stressStatus, value); }
    public double StressProgress { get => _stressProgress; private set => Set(ref _stressProgress, value); }
    public List<double> StressSeries { get => _stressSeries; private set { Set(ref _stressSeries, value); Raise(nameof(HasStressSeries)); } }
    public bool HasStressSeries => StressSeries.Count >= 2;
    public string StressLog { get => _stressLog; private set => Set(ref _stressLog, value); }
    public string LastTestText { get => _lastTest; private set => Set(ref _lastTest, value); }

    // ---------- Results ----------
    bool _resultVisible;
    string _resultVerdict = "", _resultTone = "", _resultMeta = "", _resultNote = "";
    public bool StressResultVisible { get => _resultVisible; private set { Set(ref _resultVisible, value); Raise(nameof(StressChartVisible)); } }
    public bool StressChartVisible => !StressResultVisible;
    public string ResultVerdict { get => _resultVerdict; private set => Set(ref _resultVerdict, value); }
    public string ResultTone { get => _resultTone; private set { Set(ref _resultTone, value); Raise(nameof(ResultGood)); Raise(nameof(ResultWarn)); Raise(nameof(ResultBad)); } }
    public bool ResultGood => ResultTone == "success";
    public bool ResultWarn => ResultTone == "warn";
    public bool ResultBad => ResultTone == "danger";
    public string ResultMeta { get => _resultMeta; private set => Set(ref _resultMeta, value); }
    public string ResultNote { get => _resultNote; private set { Set(ref _resultNote, value); Raise(nameof(HasResultNote)); } }
    public bool HasResultNote => ResultNote.Length > 0;

    void InitStress()
    {
        if (!Offline)
        {
            var crashed = StressTest.RecoverInterrupted(SystemProbe.ParseBootTime(Proc.Sysctl("kern.boottime")));
            if (crashed is { } r && r.Stages.LastOrDefault() is { Outcome: StressOutcome.Interrupted } last)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowToast("danger", Loc.F("stress.the_last_stability_test_did_not", StressTest.StageName(last.Stage))));
        }
        RenderStressIdle();
    }

    /// <summary>Before a run: the last test's results (Windows renders the latest record on load), else "Ready to start".</summary>
    void RenderStressIdle()
    {
        ResetLive();
        var last = Offline ? null : StressTest.Runs().FirstOrDefault();
        LastTestText = last is null ? Loc.S("stress.no_test_run_yet")
            : Loc.F("stress.last_test_0_g_passed", last.Time, StressTest.PlanName(last.Plan), last.Stages.Count(s => s.Outcome == StressOutcome.Passed), last.Stages.Count);
        if (last is not null) ShowResult(last, previous: true);
        Raise(nameof(StressMachine));
        Raise(nameof(StressPlans));
        Raise(nameof(StressHint));
    }

    void ResetLive()
    {
        StressPhase = Loc.S("stress_text.ready_to_start");
        StressStatus = "";
        StressProgress = 0;
        var plan = StressTest.Plan(PlanKind, StressMinutes);
        StressTimer = "00:00 / " + Clock(StressTest.Total(plan));
        StepChips.Clear();
        if (plan.Count > 1) for (int i = 0; i < plan.Count; i++) StepChips.Add(new StepChip($"{i + 1}. {StressTest.StageName(plan[i].Stage)}", ""));
        SetTiles(plan[0].Stage, null, false);
        StressSeries = new();
    }

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    void SetTiles(StressStage stage, StressTick? t, bool throttled)
    {
        string dash = "—";
        StressTiles.Clear();
        var (rateTitle, loadTitle) = stage switch
        {
            StressStage.Cpu => (Loc.S("stress_text.verified_computations"), Loc.S("stress_text.threads")),
            StressStage.Ram => (Loc.S("stress_text.throughput"), Loc.S("stress_text.memory_tested")),
            StressStage.Gpu => (Loc.S("mac.stress.compute"), Loc.S("mac.stress.graphics")),
            _ => (Loc.S("stress_text.throughput"), Loc.S("mac.stress.test_file")),
        };
        StressTiles.Add(new StressTile(rateTitle, t is null ? dash : StressTest.RateText(t.Rate, t.RateUnit), t is null ? "" : Loc.S("stress_text.no_mismatch"), 0));
        StressTiles.Add(new StressTile(loadTitle, t is null || t.Detail.Length == 0 ? dash : t.Detail, "", 0));
        StressTiles.Add(new StressTile(Loc.S("mac.stress.thermal"), t is null ? dash : Loc.S(throttled ? "mac.stress.throttled_short" : "mac.stress.not_throttled"), "pmset", throttled ? 2 : 0));
        StressTiles.Add(new StressTile(Loc.S("stress_text.errors"), t is null ? dash : t.Errors.ToString("N0", Loc.Culture),
            t is null ? "" : Loc.S(t.Errors > 0 ? "stress_runner.wrong_results" : "stress_runner.results_log"), t is null ? 0 : t.Errors > 0 ? 2 : 1));
    }

    async Task StartStressAsync()
    {
        if (IsStressRunning || _result is null) return;
        var kind = PlanKind;
        var plan = StressTest.Plan(kind, StressMinutes);
        var total = StressTest.Total(plan);
        bool disk = plan.Any(p => p.Stage == StressStage.Disk);
        var text = Loc.F("stress.expected_duration_the_pc_will_be", StressTest.PlanName(kind), StressTest.Duration(total),
            disk ? Loc.F("stress.and_about_will_be_written_then", DiskSizeText, "Macintosh HD") : "");
        if (!Offline && !await AskAsync(text)) return;

        IsStressRunning = true;
        StressResultVisible = false;
        var cts = _stressCts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var series = new List<double>();
        int currentStage = -1;
        bool throttled = false;
        StressLog = $"[{DateTime.Now:HH:mm:ss}] {StressTest.PlanName(kind)} · {StressTest.Duration(total)}";
        void Tick(int index, StressTick t) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (index != currentStage)
            {
                currentStage = index;
                series.Clear();
                StressLog += $"\n▶ {StressTest.StageName(plan[index].Stage)}";
                StepChips.Clear();
                if (plan.Count > 1) for (int i = 0; i < plan.Count; i++) StepChips.Add(new StepChip($"{i + 1}. {StressTest.StageName(plan[i].Stage)}", i == index ? "accent" : ""));
            }
            StressPhase = Loc.F("stress.stage", index + 1, plan.Count, StressTest.StageName(t.Stage));
            StressStatus = StressTest.RateText(t.Rate, t.RateUnit) + (t.Detail.Length > 0 ? " · " + t.Detail : "");
            StressTimer = Clock(sw.Elapsed) + " / " + Clock(total);
            StressProgress = Math.Min(100, 100 * sw.Elapsed.TotalSeconds / total.TotalSeconds);
            series.Add(t.Rate);
            if (series.Count > 180) series.RemoveAt(0);
            var max = series.Max() * 1.15;
            StressSeries = max > 0 ? series.Select(v => v / max * 100).ToList() : new();
            SetTiles(t.Stage, t, throttled);
            if ((int)sw.Elapsed.TotalSeconds % 15 == 0) StressLog += $"\n[+{Clock(sw.Elapsed)}] {StressStatus} | errors={t.Errors}";
        });
        using var thermalPoll = new Timer(_ =>
        {
            var hot = SystemProbe.ParseTherm(Proc.Run("/usr/bin/pmset", new[] { "-g", "therm" }, 5000).Stdout).Warning;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => throttled |= hot);
        }, null, 3000, 5000);
        var hw = _result.Hardware;
        StressRunRecord record;
        try { record = await Task.Run(() => StressTest.Run(kind, plan, hw, cts.Token, Tick)); }
        finally
        {
            IsStressRunning = false;
            _stressCts = null;
        }
        StressLog += "\n" + StressTest.RunVerdict(record);
        StressPhase = StressTest.RunVerdict(record);
        StressStatus = "";
        ShowResult(record, previous: false);
        var tone = StressTest.RunTone(record);
        ShowToast(tone == "danger" ? "danger" : tone == "warn" ? "warn" : "success", StressTest.RunVerdict(record));
        LastTestText = Loc.F("stress.last_test_0_g_passed", record.Time, StressTest.PlanName(record.Plan),
            record.Stages.Count(s => s.Outcome == StressOutcome.Passed), record.Stages.Count);
    }

    void ShowResult(StressRunRecord r, bool previous)
    {
        StressResultVisible = true;
        ResultVerdict = StressTest.RunVerdict(r);
        ResultTone = StressTest.RunTone(r);
        ResultMeta = (previous ? Loc.S("stress.last_test") + " · " : "") + StressTest.PlanName(r.Plan) + " · " + r.Time.ToString("g", Loc.Culture);
        ResultNote = r.Plan == StressPlanKind.Quick && r.Completed && !r.AnyFailure ? Loc.S("mac.stress.quick_note") : "";
        StageResults.Clear();
        foreach (var s in r.Stages)
        {
            var (label, tone, icon) = s.Outcome switch
            {
                StressOutcome.Passed => ("stress.passed", "success", "Completed"),
                StressOutcome.Warning => ("stress.watch_out", "warn", "Warning"),
                StressOutcome.Failed => ("stress.failed", "danger", "ErrorBadge"),
                StressOutcome.Interrupted => ("stress.pc_crashed", "danger", "ErrorBadge"),
                StressOutcome.Stopped => ("stress.stopped", "", "Lightbulb"),
                _ => ("stress.not_tested", "", "Lightbulb"),
            };
            StageResults.Add(new StageResultRow(icon, tone, $"{StressTest.StageName(s.Stage)} · {Loc.S(label)} · {StressTest.Duration(TimeSpan.FromSeconds(s.DurationSec))}", s.Summary, s.Advice));
        }
        StepChips.Clear();
        if (r.Stages.Count > 1)
            for (int i = 0; i < r.Stages.Count; i++)
                StepChips.Add(new StepChip($"{i + 1}. {StressTest.StageName(r.Stages[i].Stage)}",
                    r.Stages[i].Outcome switch { StressOutcome.Passed => "success", StressOutcome.Warning => "warn", StressOutcome.Failed or StressOutcome.Interrupted => "danger", _ => "" }));
    }
}
