using System;
using System.Collections.Generic;
using System.IO;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// Builds the test plans. "Quick" is a ~20 min screening, "Thorough" a ~3 h run to leave overnight.
    /// Durations follow common practice (CoreCycler per-core cycling, memtest_vulkan warm-up, RAM coverage),
    /// scaled down for a screening tool.
    /// </summary>
    public static class StressPlanner
    {
        private const long GB = 1024L * 1024L * 1024L;

        public static int PhysicalCores { get { return Math.Max(1, Native.FirstLogicalOfEachCore().Count); } }

        public static StressPlan Build(StressPlanKind kind, int minutes)
        {
            int cores = PhysicalCores;
            var stages = new List<StressStage>();
            switch (kind)
            {
                case StressPlanKind.Quick:
                    stages.Add(Stage(StressStageKind.CpuAllCore, 4 * 60));
                    stages.Add(PerCore(cores, Clamp(240 / cores, 20, 45)));
                    stages.Add(Stage(StressStageKind.Ram, 5 * 60));
                    stages.Add(Stage(StressStageKind.GpuCompute, 3 * 60));
                    stages.Add(Stage(StressStageKind.Vram, 3 * 60));
                    stages.Add(Stage(StressStageKind.Transient, 2 * 60));
                    stages.Add(Disk(150, 8 * GB));
                    break;
                case StressPlanKind.Thorough:
                    stages.Add(Stage(StressStageKind.CpuAllCore, 20 * 60));
                    stages.Add(PerCore(cores, Clamp(90 * 60 / cores, 120, 360)));
                    stages.Add(Stage(StressStageKind.Ram, 60 * 60));
                    stages.Add(Stage(StressStageKind.GpuCompute, 20 * 60));
                    stages.Add(Stage(StressStageKind.Vram, 20 * 60));
                    stages.Add(Stage(StressStageKind.Transient, 10 * 60));
                    stages.Add(Disk(30 * 60, 64 * GB));
                    break;
                case StressPlanKind.CpuPerCore:
                    stages.Add(PerCore(cores, Clamp(minutes, 1, 30) * 60));
                    break;
                case StressPlanKind.Disk:
                    stages.Add(Disk(Clamp(minutes, 1, 120) * 60, 16 * GB));
                    break;
                default:
                    stages.Add(Stage(StageOf(kind), Clamp(minutes, 1, 120) * 60));
                    break;
            }
            return new StressPlan { Kind = kind, Stages = stages };
        }

        public static bool IsSuite(StressPlanKind k) { return k == StressPlanKind.Quick || k == StressPlanKind.Thorough; }

        private static StressStageKind StageOf(StressPlanKind k)
        {
            switch (k)
            {
                case StressPlanKind.CpuAllCore: return StressStageKind.CpuAllCore;
                case StressPlanKind.GpuCompute: return StressStageKind.GpuCompute;
                case StressPlanKind.Vram: return StressStageKind.Vram;
                case StressPlanKind.Transient: return StressStageKind.Transient;
                case StressPlanKind.Ram: return StressStageKind.Ram;
                default: return StressStageKind.CpuAllCore;
            }
        }

        private static StressStage Stage(StressStageKind k, int sec) { return new StressStage { Kind = k, DurationSec = sec }; }

        private static StressStage PerCore(int cores, int perCoreSec)
        {
            return new StressStage { Kind = StressStageKind.CpuPerCore, PerCoreSec = perCoreSec, DurationSec = perCoreSec * cores };
        }

        private static StressStage Disk(int sec, long span)
        {
            return new StressStage { Kind = StressStageKind.Disk, DurationSec = sec, DiskSpanBytes = DiskSpanFor(span) };
        }

        /// <summary>Never use more than 40 % of the free space, and keep 10 GB free.</summary>
        public static long DiskSpanFor(long wanted)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetTempPath()));
                long free = drive.AvailableFreeSpace;
                long span = Math.Min(wanted, Math.Min((long)(free * 0.4), free - 10 * GB));
                span -= span % (4L * 1024 * 1024);
                return Math.Max(0, span);
            }
            catch (Exception ex) { Log.Warn(ex); return 0; }
        }

        public static string TestDrive
        {
            get { try { return Path.GetPathRoot(Path.GetTempPath()).TrimEnd('\\'); } catch { return "C:"; } }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
