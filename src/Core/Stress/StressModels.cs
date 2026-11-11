using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCrashDoctor.Stress
{
    /// <summary>What the user picks in the stress page.</summary>
    public enum StressPlanKind
    {
        Quick = 0,
        Thorough = 1,
        CpuAllCore = 2,
        CpuPerCore = 3,
        GpuCompute = 4,
        Vram = 5,
        Transient = 6,
        Ram = 7,
        Disk = 8
    }

    /// <summary>One building block of a plan. Every stage verifies its own results.</summary>
    public enum StressStageKind
    {
        CpuAllCore = 0,
        CpuPerCore = 1,
        GpuCompute = 2,
        Vram = 3,
        Transient = 4,
        Ram = 5,
        Disk = 6
    }

    public enum StressOutcome
    {
        Passed = 0,
        Warning = 1,
        Failed = 2,
        Skipped = 3,
        Stopped = 4,
        /// <summary>The PC crashed or rebooted while this stage was running (found at next launch).</summary>
        Interrupted = 5
    }

    public sealed class StressStage
    {
        public StressStageKind Kind { get; set; }
        public int DurationSec { get; set; }
        /// <summary>Per-core stage: seconds spent on each physical core.</summary>
        public int PerCoreSec { get; set; }
        /// <summary>Disk stage: bytes written then read back per cycle.</summary>
        public long DiskSpanBytes { get; set; }
    }

    public sealed class StressPlan
    {
        public StressPlanKind Kind { get; set; }
        public List<StressStage> Stages { get; set; }
        public int TotalSec { get { return Stages.Sum(s => s.DurationSec); } }
        public long DiskBytes { get { return Stages.Where(s => s.Kind == StressStageKind.Disk).Sum(s => s.DiskSpanBytes); } }
        public bool Uses(StressStageKind k) { return Stages.Any(s => s.Kind == k); }
    }

    /// <summary>One live tile of the page (already localized by the engine).</summary>
    public sealed class StressTile
    {
        public string Title { get; set; }
        public string Value { get; set; }
        public string Sub { get; set; }
        /// <summary>0 neutral, 1 good, 2 bad.</summary>
        public int Tone { get; set; }

        public static StressTile Make(string title, string value, string sub, int tone = 0)
        {
            return new StressTile { Title = title, Value = value, Sub = sub, Tone = tone };
        }
    }

    /// <summary>Snapshot sent to the UI about once per second.</summary>
    public sealed class StressSample
    {
        public int StageIndex { get; set; }
        public int StageCount { get; set; }
        public StressStageKind Kind { get; set; }
        public string StageName { get; set; }
        public string Status { get; set; }
        public double StageElapsedSec { get; set; }
        public double StageTotalSec { get; set; }
        public double RunElapsedSec { get; set; }
        public double RunTotalSec { get; set; }
        public StressTile[] Tiles { get; set; }
        /// <summary>Chart series; NaN = no value.</summary>
        public double Series1 { get; set; }
        public double Series2 { get; set; }
        public string Legend { get; set; }
        public int Errors { get; set; }
        public string LogLine { get; set; }
    }

    public sealed class StressStageResult
    {
        public StressStageKind Kind { get; set; }
        public string Name { get; set; }
        public StressOutcome Outcome { get; set; }
        public string Summary { get; set; }
        public string Advice { get; set; }
        public List<string> FixIds { get; set; }
        public double DurationSec { get; set; }
    }

    public sealed class StressRunRecord
    {
        public DateTime Time { get; set; }
        public StressPlanKind Plan { get; set; }
        public string PlanName { get; set; }
        public bool Completed { get; set; }
        public List<StressStageResult> Stages { get; set; }

        public bool AnyFailure
        {
            get { return Stages != null && Stages.Any(s => s.Outcome == StressOutcome.Failed || s.Outcome == StressOutcome.Interrupted); }
        }
    }

    /// <summary>Written at each stage start and removed at the end: if it survives, the PC went down mid-test.</summary>
    public sealed class StressRunningMarker
    {
        public DateTime Started { get; set; }
        public StressPlanKind Plan { get; set; }
        public int StageIndex { get; set; }
        public int StageCount { get; set; }
        public StressStageKind Stage { get; set; }
        public DateTime StageStarted { get; set; }
        public List<StressStageResult> Done { get; set; }
    }
}
