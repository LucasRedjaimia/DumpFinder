using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCrashDoctor.Stress
{
    /// <summary>Turns what a stage observed into a verdict: what failed, what it most likely means, what to do.</summary>
    internal static class StressAdvice
    {

        private static StressStageResult R(StageContext ctx, StressOutcome o, string summary, string advice, params string[] fixes)
        {
            return R(ctx.Stage.Kind, o, summary, advice, fixes);
        }

        private static StressStageResult R(StressStageKind kind, StressOutcome o, string summary, string advice, params string[] fixes)
        {
            return new StressStageResult
            {
                Kind = kind,
                Name = StressText.StageName(kind),
                Outcome = o,
                Summary = summary,
                Advice = advice,
                FixIds = fixes != null ? fixes.ToList() : new List<string>()
            };
        }

        // ---------------- Advice blocks
        private static string CpuAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.usual_causes_overly_aggressive_overclock");
            }
        }

        private static string CoreAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.a_core_that_miscalculates_under_light");
            }
        }

        private static string GpuAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.a_sign_of_gpu_instability_overly");
            }
        }

        private static string PsuAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.sudden_load_spikes_expose_a_marginal");
            }
        }

        private static string RamAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.disable_the_expo_xmp_profile_or");
            }
        }

        private static string DiskAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.this_is_serious_the_ssd_its");
            }
        }

        private static string PcieAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.error_on_a_pcie_link_often");
            }
        }

        private static string DriverResetAdvice
        {
            get
            {
                return Localizer.Ref("stress_advice.under_load_the_graphics_driver_stopped");
            }
        }

        private static string AdviceFor(StressStageKind k)
        {
            switch (k)
            {
                case StressStageKind.CpuAllCore: return CpuAdvice;
                case StressStageKind.CpuPerCore: return CoreAdvice;
                case StressStageKind.GpuCompute:
                case StressStageKind.Vram: return GpuAdvice;
                case StressStageKind.Transient: return PsuAdvice;
                case StressStageKind.Ram: return RamAdvice;
                default: return DiskAdvice;
            }
        }

        // ---------------- Verdicts
        public static StressStageResult Passed(StageContext ctx, string summary)
        {
            return R(ctx, ctx.UserStopped ? StressOutcome.Stopped : StressOutcome.Passed, summary, null);
        }

        public static StressStageResult Skipped(StageContext ctx, string reason)
        {
            return R(ctx, StressOutcome.Skipped, reason, null);
        }

        public static StressStageResult CpuFailed(StageContext ctx, int cpu, string work)
        {
            string where = cpu >= 0
                ? Localizer.Ref("stress_advice.logical_processor_load", cpu, work)
                : (string.IsNullOrEmpty(work) ? "" : " (" + work + ")");
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.the_processor_returned_a_wrong_result") + where + ".",
                CpuAdvice);
        }

        public static StressStageResult CoresFailed(StageContext ctx, List<int> bad, int total)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.wrong_result_on_of_core_s", 
                              bad.Count, total, string.Join(", ", bad)),
                CoreAdvice);
        }

        public static StressStageResult GpuUnavailable(StageContext ctx, string reason)
        {
            return R(ctx, StressOutcome.Skipped,
                Localizer.Ref("stress_advice.gpu_not_tested") + reason, null);
        }

        public static StressStageResult GpuComputeFailed(StageContext ctx, string detail)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.the_graphics_card_returned_a_different") + " (" + detail + ").",
                ctx.Stage.Kind == StressStageKind.Transient ? GpuAdvice + " " + PsuAdvice : GpuAdvice);
        }

        public static StressStageResult GpuDriverReset(StageContext ctx, string detail)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.the_graphics_driver_dropped_out_during") + " (" + detail + ").",
                ctx.Stage.Kind == StressStageKind.Transient ? DriverResetAdvice + " " + PsuAdvice : DriverResetAdvice, "tdr");
        }

        public static StressStageResult VramFailed(StageContext ctx, double offsetBytes, int bits, long errors)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.video_memory_returned_corrupted_data_err", 
                              errors, StressText.Size(offsetBytes), bits),
                Localizer.Ref("stress_advice.remove_any_gpu_memory_overclock_afterbur"));
        }

        public static StressStageResult RamFailed(StageContext ctx, double offsetBytes, int bits, string pattern)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.memory_error_at_bit_s_differ", 
                              StressText.Size(offsetBytes), bits, pattern),
                RamAdvice);
        }

        public static StressStageResult DiskDataFailed(StageContext ctx, double offsetBytes)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.data_read_back_does_not_match", 
                              StressText.Size(offsetBytes)),
                DiskAdvice);
        }

        public static StressStageResult DiskIoFailed(StageContext ctx, int win32)
        {
            return R(ctx, StressOutcome.Failed,
                Localizer.Ref("stress_advice.the_drive_returned_an_i_o", 
                              win32, new System.ComponentModel.Win32Exception(win32).Message),
                DiskAdvice, "nvme_hmb", "pcie_aspm");
        }

        public static StressStageResult DiskSlow(StageContext ctx, string summary, int spikes)
        {
            return R(ctx, StressOutcome.Warning,
                summary + " " + Localizer.Ref("stress_advice.but_stall_s_longer_than_2", spikes),
                Localizer.Ref("stress_advice.multi_second_stalls_freeze_windows_storn"),
                "nvme_hmb", "pcie_aspm");
        }

        /// <summary>
        /// Stage stopped by the thermal watchdog. A computation error found before the stop stays the verdict (Failed);
        /// otherwise the stage is a Warning: no wrong result, but the cooling cannot hold the load.
        /// </summary>
        public static StressStageResult Overheated(StageContext ctx, StressStageResult r)
        {
            bool cpu = ctx.Overheat == OverheatPart.Cpu;
            string note = Localizer.Ref(cpu ? "stress_advice.stopped_cpu_overheat" : "stress_advice.stopped_gpu_overheat",
                                        Math.Round(ctx.OverheatTempC), StressRunner.OverheatSustainSec);
            string advice = Localizer.Ref(cpu ? "stress_advice.cpu_cooling_advice" : "stress_advice.gpu_cooling_advice");
            if (r != null && r.Outcome == StressOutcome.Failed)
            {
                r.Summary = ((r.Summary ?? "") + " " + note).Trim();
                return r;
            }
            return R(ctx, StressOutcome.Warning, note, advice);
        }

        /// <summary>Log line written when the watchdog stops a stage.</summary>
        public static string OverheatLog(OverheatPart part, double tempC)
        {
            return Localizer.F(part == OverheatPart.Cpu ? "stress_advice.stopped_cpu_overheat" : "stress_advice.stopped_gpu_overheat",
                               Math.Round(tempC), StressRunner.OverheatSustainSec);
        }

        public static StressStageResult InternalError(StageContext ctx, Exception ex)
        {
            return R(ctx, StressOutcome.Warning,
                Localizer.Ref("stress_advice.stage_interrupted_by_an_internal_error") + ex.Message,
                null);
        }

        /// <summary>Errors Windows logged while the stage ran override a clean result.</summary>
        public static StressStageResult MergeEvents(StageContext ctx, StressStageResult r, List<LiveEvent> events)
        {
            if (events == null || events.Count == 0 || r.Outcome == StressOutcome.Failed) return r;
            string list = string.Join(", ", events.Select(e => e.ToString()).Distinct().Take(4));
            var gpu = events.Where(e => e.Kind == LiveEventKind.GpuDriver).ToList();
            // Uncorrected WHEA errors fail the stage; corrected ones (WarningOnly) are an early warning only.
            var whea = events.Where(e => e.Kind == LiveEventKind.Whea && !e.WarningOnly).ToList();
            var wheaCorrected = events.Where(e => e.Kind == LiveEventKind.Whea && e.WarningOnly).ToList();
            // Disk events about another disk (or an unidentified one for disk 7/11/153) are WarningOnly too.
            var disk = events.Where(e => e.Kind == LiveEventKind.Disk && !e.WarningOnly).ToList();
            StressStageKind k = ctx.Stage.Kind;
            bool gpuStage = k == StressStageKind.GpuCompute || k == StressStageKind.Vram || k == StressStageKind.Transient;
            bool pcieStage = gpuStage || k == StressStageKind.Disk;

            if (gpu.Count > 0 && gpuStage)
                return GpuDriverReset(ctx, list);
            if (whea.Count > 0)
            {
                string wheaList = string.Join(", ", whea.Select(e => e.ToString()).Distinct().Take(4));
                string summary = Localizer.Ref("stress_advice.windows_logged_uncorrected_whea_hardware", whea.Count, wheaList);
                return R(ctx, StressOutcome.Failed, summary, pcieStage ? PcieAdvice : AdviceFor(k), pcieStage ? new[] { "pcie_aspm" } : new string[0]);
            }
            if (disk.Count > 0 && k == StressStageKind.Disk)
                return R(ctx, StressOutcome.Failed,
                    Localizer.Ref("stress_advice.the_storage_controller_reported_errors_o")
                    + string.Join(", ", disk.Select(e => e.ToString()).Distinct().Take(4)) + ").",
                    DiskAdvice, "nvme_hmb", "pcie_aspm");

            // Only warnings left: corrected WHEA errors, and/or an error unrelated to the stage (e.g. a GPU reset
            // during the RAM test, a disk event about another drive). Worth flagging, not a failure of this stage.
            var notes = new List<string>();
            string advice = null;
            var fixes = new List<string>();
            if (wheaCorrected.Count > 0)
            {
                notes.Add(Localizer.Ref("stress_advice.windows_logged_corrected_whea_hardware_e", 
                                        wheaCorrected.Count, string.Join(", ", wheaCorrected.Select(e => e.ToString()).Distinct().Take(4))));
                advice = pcieStage ? PcieAdvice : AdviceFor(k);
                if (pcieStage) fixes.Add("pcie_aspm");
            }
            var others = events.Where(e => !(e.Kind == LiveEventKind.Whea && e.WarningOnly)).ToList();
            if (others.Count > 0)
            {
                notes.Add(Localizer.Ref("stress_advice.windows_did_report")
                          + string.Join(", ", others.Select(e => e.ToString()).Distinct().Take(4)) + ".");
                if (advice == null) advice = gpu.Count > 0 ? DriverResetAdvice : DiskAdvice;
            }
            return R(ctx, StressOutcome.Warning, ((r.Summary ?? "") + " " + string.Join(" ", notes)).Trim(), advice, fixes.ToArray());
        }

        /// <summary>Verdict for a stage that was running when the PC went down (found at the next launch).</summary>
        public static StressStageResult Interrupted(StressStageKind k)
        {
            return R(k, StressOutcome.Interrupted,
                Localizer.Ref("stress_advice.the_pc_crashed_or_rebooted_during"),
                AdviceFor(k) + " " + Localizer.Ref("stress_advice.the_matching_blue_screen_is_probably"),
                k == StressStageKind.GpuCompute || k == StressStageKind.Vram || k == StressStageKind.Transient ? new[] { "tdr" } : new string[0]);
        }

        public static StressStageResult Abandoned(StressStageKind k)
        {
            return R(k, StressOutcome.Stopped,
                Localizer.Ref("stress_advice.test_interrupted_before_the_end_pc"), null);
        }

        public static StressStageResult NotRun(StressStageKind k)
        {
            return R(k, StressOutcome.Skipped, Localizer.Ref("stress_advice.not_run_test_stopped"), null);
        }

        public static StressStageResult SkippedAfterDriverReset(StressStageKind k)
        {
            return R(k, StressOutcome.Skipped,
                Localizer.Ref("stress_advice.skipped_the_graphics_driver_already_drop"), null);
        }
    }
}
