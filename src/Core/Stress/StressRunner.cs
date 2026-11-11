using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinCrashDoctor.Stress
{
    /// <summary>What one stage engine can see and do while it runs.</summary>
    internal sealed class StageContext
    {
        private readonly StressRunner _runner;
        private readonly Action<StressSample> _emit;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly object _lock = new object();
        private readonly List<LiveEvent> _events = new List<LiveEvent>();

        public StressStage Stage;
        public int Index, Count;
        public double RunOffsetSec, RunTotalSec;
        public CpuClockSensor Cpu;
        public GpuSensor Gpu;
        public CpuTempFeed CpuTemp;
        public Func<OpenClDevice> GpuDevice;
        public volatile bool Abort;

        public StageContext(StressRunner runner, Action<StressSample> emit)
        {
            _runner = runner;
            _emit = emit;
        }

        public double Elapsed { get { return _sw.Elapsed.TotalSeconds; } }
        public bool UserStopped { get { return _runner.StopRequested; } }
        public bool TimeUp { get { return Elapsed >= Stage.DurationSec; } }
        public bool ShouldStop { get { return UserStopped || Abort || TimeUp; } }

        /// <summary>Sleeps, but wakes up early when the user stops the test.</summary>
        public void Sleep(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (!UserStopped && !Abort)
            {
                int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) break;
                Thread.Sleep(Math.Min(100, left));
            }
        }

        public void AddEvents(IEnumerable<LiveEvent> evs) { lock (_lock) _events.AddRange(evs); }
        public List<LiveEvent> Events { get { lock (_lock) return _events.ToList(); } }
        public int EventCount { get { lock (_lock) return _events.Count; } }

        // ---------------- CPU temperature (PawnIO through LibreHardwareMonitor)
        public double CpuTempValue { get { return CpuTemp != null && CpuTemp.State == CpuTempState.Live ? CpuTemp.TempC : double.NaN; } }

        public string CpuTempText { get { double t = CpuTempValue; return double.IsNaN(t) ? StressText.NotAvailable : t.ToString("0") + " °C"; } }

        /// <summary>"Max 81 °C · 88 W" when live, otherwise why there is no reading.</summary>
        public string CpuTempSub
        {
            get
            {
                if (CpuTemp == null) return StressText.CpuTempState(CpuTempState.Off);
                if (CpuTemp.State != CpuTempState.Live) return StressText.CpuTempState(CpuTemp.State);
                return StressText.Max + " " + CpuTemp.PeakTempC.ToString("0") + " °C" + (double.IsNaN(CpuTemp.PowerW) ? "" : " · " + CpuTemp.PowerW.ToString("0") + " W");
            }
        }

        public StressTile CpuTempTile()
        {
            double t = CpuTempValue;
            return StressTile.Make(StressText.CpuTemp, CpuTempText, CpuTempSub, double.IsNaN(t) ? 0 : (t >= 95 ? 2 : 0));
        }

        /// <summary>" Température CPU max : 81 °C." for the stage summary (empty without a reading).</summary>
        public string CpuTempSummary
        {
            get
            {
                if (CpuTemp == null || double.IsNaN(CpuTemp.PeakTempC)) return "";
                return " " + StressText.CpuTemp + " max : " + CpuTemp.PeakTempC.ToString("0") + " °C.";
            }
        }

        public StressTile ErrorTile(int ownErrors)
        {
            int ev = EventCount;
            int n = ownErrors + ev;
            string sub = n == 0
                ? Localizer.L("calculs + journal", "results + log", "cálculos + registro", "Ergebnisse + Protokoll", "calcoli + registro", "cálculos + log")
                : (ev > 0 ? Events.Last().ToString() : Localizer.L("résultats faux", "wrong results", "resultados erróneos", "falsche Ergebnisse", "risultati errati", "resultados errados"));
            return StressTile.Make(StressText.Errors, n.ToString(), sub, n == 0 ? 1 : 2);
        }

        public void Publish(string status, StressTile[] tiles, double s1, double s2, string legend = null)
        {
            if (_emit == null) return;
            double el = Math.Min(Elapsed, Stage.DurationSec);
            int errors = 0;
            if (tiles != null && tiles.Length > 3) int.TryParse(tiles[3].Value, out errors);
            _emit(new StressSample
            {
                StageIndex = Index,
                StageCount = Count,
                Kind = Stage.Kind,
                StageName = StressText.StageName(Stage.Kind),
                Status = status,
                StageElapsedSec = el,
                StageTotalSec = Stage.DurationSec,
                RunElapsedSec = RunOffsetSec + el,
                RunTotalSec = RunTotalSec,
                Tiles = tiles,
                Series1 = s1,
                Series2 = s2,
                Legend = legend,
                Errors = errors
            });
        }

        public void Log(string line)
        {
            if (_emit != null)
                _emit(new StressSample { StageIndex = Index, StageCount = Count, Kind = Stage.Kind, StageName = StressText.StageName(Stage.Kind), LogLine = line, Series1 = double.NaN, Series2 = double.NaN });
        }
    }

    /// <summary>What the page shows before a run (detected once, in the background).</summary>
    public sealed class StressCapabilities
    {
        public int Cores, Threads;
        public string Simd;
        public string GpuName;
        public long GpuMemBytes;
        public string GpuSensor;
        public ulong RamAvailBytes, RamTotalBytes;
        public string Drive;
        public bool PawnIo;
        public bool IsAdmin;
        public long DiskFreeBytes;
    }

    /// <summary>Runs a plan stage by stage, watches the Windows log, and records the outcome.</summary>
    public sealed class StressRunner
    {
        private volatile bool _stop;
        public bool IsRunning { get; private set; }
        public bool StopRequested { get { return _stop; } }

        public void RequestStop() { _stop = true; }

        public Task<StressRunRecord> RunAsync(StressPlan plan, Action<StressSample> onSample, bool cpuTemp = false)
        {
            _stop = false;
            IsRunning = true;
            return Task.Run(() =>
            {
                try { return Run(plan, onSample, cpuTemp); }
                finally { IsRunning = false; }
            });
        }

        private StressRunRecord Run(StressPlan plan, Action<StressSample> emit, bool cpuTemp)
        {
            var record = new StressRunRecord
            {
                Time = DateTime.Now,
                Plan = plan.Kind,
                PlanName = StressText.PlanName(plan.Kind),
                Stages = new List<StressStageResult>()
            };
            bool needsGpu = plan.Uses(StressStageKind.GpuCompute) || plan.Uses(StressStageKind.Vram) || plan.Uses(StressStageKind.Transient);
            OpenClDevice device = null;
            bool deviceTried = false;
            bool gpuDriverDown = false;
            double offset = 0;

            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED); // no sleep mid-test (same thread clears it)
            using (var cpu = new CpuClockSensor())
            using (var gpu = new GpuSensor())
            {
                if (needsGpu) gpu.Start();
                bool needsCpuTemp = cpuTemp && (plan.Uses(StressStageKind.CpuAllCore) || plan.Uses(StressStageKind.CpuPerCore) || plan.Uses(StressStageKind.Transient));
                CpuTempFeed temp = needsCpuTemp ? CpuTempFeed.Start() : null;
                Func<OpenClDevice> getDevice = () =>
                {
                    if (!deviceTried) { deviceTried = true; device = OpenClDevice.TryOpen(); }
                    return device;
                };

                try
                {
                    for (int i = 0; i < plan.Stages.Count; i++)
                    {
                        StressStage st = plan.Stages[i];
                        if (_stop) { record.Stages.Add(StressAdvice.NotRun(st.Kind)); continue; }
                        bool gpuStage = st.Kind == StressStageKind.GpuCompute || st.Kind == StressStageKind.Vram || st.Kind == StressStageKind.Transient;
                        if (gpuStage && gpuDriverDown) { record.Stages.Add(StressAdvice.SkippedAfterDriverReset(st.Kind)); offset += st.DurationSec; continue; }

                        History.SaveStressMarker(new StressRunningMarker
                        {
                            Started = record.Time,
                            Plan = plan.Kind,
                            StageIndex = i,
                            StageCount = plan.Stages.Count,
                            Stage = st.Kind,
                            StageStarted = DateTime.Now,
                            Done = record.Stages.ToList()
                        });

                        var ctx = new StageContext(this, emit)
                        {
                            Stage = st,
                            Index = i,
                            Count = plan.Stages.Count,
                            RunOffsetSec = offset,
                            RunTotalSec = plan.TotalSec,
                            Cpu = cpu,
                            Gpu = gpu,
                            CpuTemp = temp,
                            GpuDevice = getDevice
                        };
                        gpu.ResetPeaks();
                        if (temp != null) temp.ResetPeak();
                        StressStageResult res = RunStage(ctx);
                        res.DurationSec = Math.Round(ctx.Elapsed);
                        record.Stages.Add(res);
                        if (gpuStage && res.Outcome == StressOutcome.Failed && res.FixIds != null && res.FixIds.Contains("tdr")) gpuDriverDown = true;
                        offset += st.DurationSec;
                    }
                }
                finally
                {
                    if (device != null) device.Dispose();
                    if (temp != null) temp.Dispose();
                }
            }

            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            record.Completed = !_stop;
            History.ClearStressMarker();
            History.AddStressRun(record);
            return record;
        }

        private StressStageResult RunStage(StageContext ctx)
        {
            var watch = new EventWatch(DateTime.Now.AddSeconds(-1));
            StressStageKind kind = ctx.Stage.Kind;
            bool gpuStage = kind == StressStageKind.GpuCompute || kind == StressStageKind.Vram || kind == StressStageKind.Transient;

            using (var timer = new Timer(_ =>
            {
                var evs = watch.Poll();
                if (evs.Count == 0) return;
                ctx.AddEvents(evs);
                foreach (var e in evs) ctx.Log("⚠ Windows : " + e);
                // Errors tied to what is being tested end the stage right away.
                if (evs.Any(e => e.Kind == LiveEventKind.Whea
                              || (e.Kind == LiveEventKind.GpuDriver && gpuStage)
                              || (e.Kind == LiveEventKind.Disk && kind == StressStageKind.Disk)))
                    ctx.Abort = true;
            }, null, 2000, 2000))
            {
                StressStageResult res;
                try
                {
                    switch (kind)
                    {
                        case StressStageKind.CpuAllCore: res = CpuStress.RunAllCore(ctx); break;
                        case StressStageKind.CpuPerCore: res = CpuStress.RunPerCore(ctx); break;
                        case StressStageKind.GpuCompute: res = GpuStress.RunCompute(ctx); break;
                        case StressStageKind.Vram: res = GpuStress.RunVram(ctx); break;
                        case StressStageKind.Transient: res = GpuStress.RunTransient(ctx); break;
                        case StressStageKind.Ram: res = RamStress.Run(ctx); break;
                        default: res = DiskStress.Run(ctx); break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(ex);
                    res = StressAdvice.InternalError(ctx, ex);
                }
                ctx.AddEvents(watch.Poll());
                return StressAdvice.MergeEvents(ctx, res, ctx.Events);
            }
        }

        /// <summary>
        /// Called at startup: if the marker of a running test is still there, the PC went down during that stage.
        /// Returns the reconstructed record (also saved in the history), or null.
        /// </summary>
        public static StressRunRecord RecoverInterrupted()
        {
            StressRunningMarker m = History.PeekStressMarker();
            if (m == null) return null;
            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1) return null; // another instance may be testing
            var stages = (m.Done ?? new List<StressStageResult>()).ToList();
            stages.Add(CrashedAfter(History.Local(m.StageStarted)) ? StressAdvice.Interrupted(m.Stage) : StressAdvice.Abandoned(m.Stage));
            var rec = new StressRunRecord
            {
                Time = History.Local(m.Started),
                Plan = m.Plan,
                PlanName = StressText.PlanName(m.Plan),
                Completed = false,
                Stages = stages
            };
            History.ClearStressMarker();
            History.AddStressRun(rec);
            return rec;
        }

        /// <summary>Unexpected shutdown (Kernel-Power 41, EventLog 6008) or blue screen (WER 1001) since the stage started?</summary>
        private static bool CrashedAfter(DateTime t)
        {
            try
            {
                string utc = t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", System.Globalization.CultureInfo.InvariantCulture);
                string xpath = "*[System[(EventID=41 or EventID=6008 or EventID=1001) and TimeCreated[@SystemTime >= '" + utc + "']]]";
                using (var r = new System.Diagnostics.Eventing.Reader.EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName, xpath)))
                {
                    System.Diagnostics.Eventing.Reader.EventRecord rec;
                    while ((rec = r.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            string p = rec.ProviderName ?? "";
                            if ((rec.Id == 41 && p.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0)
                                || (rec.Id == 6008 && p.IndexOf("EventLog", StringComparison.OrdinalIgnoreCase) >= 0)
                                || (rec.Id == 1001 && p.IndexOf("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) >= 0))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return false;
        }

        public static StressCapabilities Probe()
        {
            var c = new StressCapabilities
            {
                Cores = StressPlanner.PhysicalCores,
                Threads = Environment.ProcessorCount,
                Simd = CpuWork.SimdLabel,
                RamAvailBytes = Native.AvailablePhys(),
                RamTotalBytes = Native.TotalPhys(),
                Drive = StressPlanner.TestDrive,
                PawnIo = CpuTempReader.PawnIoInstalled,
                IsAdmin = Elevation.IsAdministrator()
            };
            try { c.DiskFreeBytes = new System.IO.DriveInfo(c.Drive).AvailableFreeSpace; } catch { }
            using (var dev = OpenClDevice.TryOpen())
            {
                if (dev != null && dev.Ready) { c.GpuName = dev.Name; c.GpuMemBytes = dev.GlobalMem; }
            }
            if (System.IO.File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"))) c.GpuSensor = "nvidia-smi";
            else if (AmdAdl.Exists) c.GpuSensor = "AMD ADL";
            return c;
        }
    }
}
