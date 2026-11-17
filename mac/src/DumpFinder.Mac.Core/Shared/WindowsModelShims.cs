// The two Windows types that src/Core/Stability.cs needs, reduced to what it reads.
// Same names, namespace and enum order as src/Core/Models.cs so the shared file compiles unchanged:
// if Windows adds a category, add it here too (the shared Stability.Penalty switch will use it).
namespace WinCrashDoctor
{
    public enum CrashCategory
    {
        BSOD,             // Mac: kernel panic
        GpuDriver,        // Mac: GPU restart (no panic)
        StorageNvme,      // Mac: storage error
        HardwareWhea,     // Mac: hardware error reported by the firmware
        MemoryExhaustion, // Mac: Jetsam (low-memory kill)
        BrowserGpu,       // Mac: browser crash
        AppCrash,
        UnexpectedReboot  // Mac: watchdog / forced restart without a panic report
    }

    public class CrashEventItem
    {
        public System.DateTime Timestamp { get; set; }
        public CrashCategory Category { get; set; }
    }
}
