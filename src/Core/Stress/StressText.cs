namespace WinCrashDoctor.Stress
{
    /// <summary>Short labels shared by the stress engines and the page (6 languages).</summary>
    internal static class StressText
    {

        public static string PlanName(StressPlanKind k)
        {
            switch (k)
            {
                case StressPlanKind.Quick: return Localizer.S("stress_text.quick_test");
                case StressPlanKind.Thorough: return Localizer.S("stress_text.thorough_test");
                case StressPlanKind.CpuAllCore: return StageName(StressStageKind.CpuAllCore);
                case StressPlanKind.CpuPerCore: return StageName(StressStageKind.CpuPerCore);
                case StressPlanKind.GpuCompute: return StageName(StressStageKind.GpuCompute);
                case StressPlanKind.Vram: return StageName(StressStageKind.Vram);
                case StressPlanKind.Transient: return StageName(StressStageKind.Transient);
                case StressPlanKind.Ram: return StageName(StressStageKind.Ram);
                default: return StageName(StressStageKind.Disk);
            }
        }

        public static string StageName(StressStageKind k)
        {
            switch (k)
            {
                case StressStageKind.CpuAllCore: return Localizer.S("stress_text.cpu_all_cores");
                case StressStageKind.CpuPerCore: return Localizer.S("stress_text.cpu_core_by_core");
                case StressStageKind.GpuCompute: return Localizer.S("stress_text.gpu_verified_compute");
                case StressStageKind.Vram: return Localizer.S("stress_text.video_memory_vram");
                case StressStageKind.Transient: return Localizer.S("stress_text.cpu_gpu_load_spikes");
                case StressStageKind.Ram: return Localizer.S("stress_text.system_memory_ram");
                default: return Localizer.S("stress_text.ssd_write_read_back");
            }
        }

        public static string CpuTemp { get { return Localizer.S("stress_text.cpu_temperature"); } }

        public static string CpuTempState(Stress.CpuTempState s)
        {
            switch (s)
            {
                case Stress.CpuTempState.Starting: return Localizer.S("stress_text.waiting_for_permission");
                case Stress.CpuTempState.NoPawnIo: return Localizer.S("stress_text.pawnio_driver_missing");
                case Stress.CpuTempState.Declined: return Localizer.S("stress_text.permission_declined");
                case Stress.CpuTempState.Unavailable: return Localizer.S("stress_text.sensor_unreadable");
                default: return Localizer.S("stress_text.turned_off");
            }
        }

        public static string Calibrating { get { return Localizer.S("stress_text.calibrating"); } }
        public static string NotAvailable { get { return Localizer.S("stress_text.n_a"); } }
        public static string Max { get { return Localizer.S("stress_text.max"); } }
        public static string Errors { get { return Localizer.S("stress_text.errors"); } }
        public static string Verified { get { return Localizer.S("stress_text.verified_computations"); } }
        public static string NoMismatch { get { return Localizer.S("stress_text.no_mismatch"); } }
        public static string EffectiveClock { get { return Localizer.S("stress_text.cpu_clock"); } }
        public static string CoreClock { get { return Localizer.S("stress_text.core_clock"); } }
        public static string Threads { get { return Localizer.S("stress_text.threads"); } }
        public static string Core { get { return Localizer.S("stress_text.core_under_test"); } }
        public static string GpuClock { get { return Localizer.S("stress_text.gpu_clock"); } }
        public static string GpuPower { get { return Localizer.S("stress_text.gpu_power"); } }
        public static string GpuTemp { get { return Localizer.S("stress_text.gpu_temperature"); } }
        public static string Throughput { get { return Localizer.S("stress_text.throughput"); } }
        public static string Latency { get { return Localizer.S("stress_text.peak_latency"); } }
        public static string Tested { get { return Localizer.S("stress_text.memory_tested"); } }
        public static string Coverage { get { return Localizer.S("stress_text.coverage"); } }
        public static string Ready { get { return Localizer.S("stress_text.ready_to_start"); } }
        public static string CpuInconsistent
        {
            get
            {
                return Localizer.S("stress_text.the_same_computation_gave_two_different");
            }
        }

        public static string Gb(double bytes) { return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " " + Localizer.S("stress_text.gb"); }
        /// <summary>Mo below 1 GB, Go above (for error positions).</summary>
        public static string Size(double bytes) { return bytes < (1L << 30) ? (bytes / (1024.0 * 1024.0)).ToString("N0") + " " + Localizer.S("stress_text.mb") : Gb(bytes); }
        public static string MbPerSec(double mb) { return mb.ToString("N0") + " " + Localizer.S("stress_text.mb_s"); }
    }
}
