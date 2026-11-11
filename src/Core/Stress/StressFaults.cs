using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// Self-test only (--stress-selftest): each flag corrupts exactly one result on the real code path,
    /// to prove that every engine notices a wrong value and reports it. Never set in normal use.
    /// </summary>
    internal static class StressFaults
    {
        public static int Cpu, Gpu, Ram, Disk, Vram;

        /// <summary>True once per armed flag.</summary>
        public static bool Take(ref int flag) { return flag != 0 && Interlocked.Exchange(ref flag, 0) != 0; }
    }
}
