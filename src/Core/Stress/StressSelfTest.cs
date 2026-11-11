using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// --stress-selftest &lt;out.txt&gt; [cpu,ram,disk,gpu]: short runs of every engine, each twice — clean (must pass)
    /// and with one injected fault on the real code path (must fail). Nothing is written to the history.
    /// </summary>
    internal static class StressSelfTest
    {
        public static int Run(string outPath, string only)
        {
            Localizer.ReadOnlySettings = true; // no history, no marker
            Localizer.Current = AppLang.FR;
            var parts = new HashSet<string>((only ?? "cpu,ram,disk,gpu").Split(','), StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine("Dump Finder stress self-test " + DateTime.Now.ToString("s"));
            sb.AppendLine("SIMD: " + CpuWork.SimdLabel + " · Vector<double>.Count=" + System.Numerics.Vector<double>.Count + " · cores=" + StressPlanner.PhysicalCores + " · threads=" + Environment.ProcessorCount + " · 64-bit=" + Environment.Is64BitProcess);
            int failures = 0;

            Action<string, StressStage, Action, StressOutcome, bool> checkT = null;
            Action<string, StressStage, Action, StressOutcome> check = (label, stage, arm, expected) => checkT(label, stage, arm, expected, false);
            checkT = (label, stage, arm, expected, cpuTemp) =>
            {
                if (arm != null) arm();
                var samples = new List<StressSample>();
                var runner = new StressRunner();
                var rec = runner.RunAsync(new StressPlan { Kind = StressPlanKind.Quick, Stages = new List<StressStage> { stage } }, s => { lock (samples) samples.Add(s); }, cpuTemp)
                                .GetAwaiter().GetResult();
                StressStageResult r = rec.Stages[0];
                bool ok = r.Outcome == expected;
                if (!ok) failures++;
                sb.AppendLine();
                sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label + " → " + r.Outcome + " (attendu " + expected + ") en " + r.DurationSec + " s");
                sb.AppendLine("       " + r.Summary);
                StressSample last;
                lock (samples) last = samples.LastOrDefault(x => x.Tiles != null);
                if (last != null)
                    sb.AppendLine("       dernier relevé : " + last.Status + " | " + string.Join(" | ", last.Tiles.Select(t => t.Title + " = " + t.Value + " (" + t.Sub + ")")));
                lock (samples)
                    foreach (var l in samples.Where(x => x.LogLine != null)) sb.AppendLine("       log : " + l.LogLine);
                StressFaults.Cpu = StressFaults.Gpu = StressFaults.Ram = StressFaults.Disk = StressFaults.Vram = 0;
            };

            if (parts.Contains("cpu"))
            {
                check("CPU tous cœurs", new StressStage { Kind = StressStageKind.CpuAllCore, DurationSec = 8 }, null, StressOutcome.Passed);
                check("CPU tous cœurs + faute injectée", new StressStage { Kind = StressStageKind.CpuAllCore, DurationSec = 8 }, () => StressFaults.Cpu = 1, StressOutcome.Failed);
                check("CPU cœur par cœur (3 s/cœur)", new StressStage { Kind = StressStageKind.CpuPerCore, PerCoreSec = 3, DurationSec = 3 * StressPlanner.PhysicalCores }, null, StressOutcome.Passed);
            }
            if (parts.Contains("cputemp"))
                checkT("CPU tous cœurs + température (autorisation Windows)", new StressStage { Kind = StressStageKind.CpuAllCore, DurationSec = 12 }, null, StressOutcome.Passed, true);
            if (parts.Contains("ram"))
            {
                RamStress.MaxTestBytes = 1L << 30;
                check("RAM (1 Go)", new StressStage { Kind = StressStageKind.Ram, DurationSec = 10 }, null, StressOutcome.Passed);
                check("RAM (1 Go) + bit inversé", new StressStage { Kind = StressStageKind.Ram, DurationSec = 10 }, () => StressFaults.Ram = 1, StressOutcome.Failed);
            }
            if (parts.Contains("disk"))
            {
                check("SSD (1 Go)", new StressStage { Kind = StressStageKind.Disk, DurationSec = 12, DiskSpanBytes = 1L << 30 }, null, StressOutcome.Passed);
                check("SSD (1 Go) + octet corrompu", new StressStage { Kind = StressStageKind.Disk, DurationSec = 12, DiskSpanBytes = 1L << 30 }, () => StressFaults.Disk = 1, StressOutcome.Failed);
            }
            if (parts.Contains("gpu"))
            {
                check("GPU calcul vérifié", new StressStage { Kind = StressStageKind.GpuCompute, DurationSec = 6 }, null, StressOutcome.Passed);
                check("GPU calcul + valeur corrompue", new StressStage { Kind = StressStageKind.GpuCompute, DurationSec = 6 }, () => StressFaults.Gpu = 1, StressOutcome.Failed);
                check("VRAM", new StressStage { Kind = StressStageKind.Vram, DurationSec = 8 }, null, StressOutcome.Passed);
                check("VRAM + erreur injectée", new StressStage { Kind = StressStageKind.Vram, DurationSec = 8 }, () => StressFaults.Vram = 1, StressOutcome.Failed);
                check("Pics CPU + GPU", new StressStage { Kind = StressStageKind.Transient, DurationSec = 8 }, null, StressOutcome.Passed);
            }

            sb.AppendLine();
            sb.AppendLine(failures == 0 ? "TOUT EST CONFORME" : failures + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return failures;
        }
    }
}
