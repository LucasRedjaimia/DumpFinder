using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// SSD test: writes several GB of known data with unbuffered I/O (straight to the drive, past the Windows
    /// cache), reads everything back from the drive and compares it, then does random 4K reads (verified too).
    /// A large span goes past the SLC cache and the HMB mapping window, where DRAM-less drives misbehave.
    /// The file is created with delete-on-close, so it disappears even if the app is killed — but not after a blue
    /// screen or power loss (exactly what this test can provoke): <see cref="SweepOrphans"/> removes those leftovers.
    /// </summary>
    internal static class DiskStress
    {
        private const int Page = 4096;
        private const int BlockPages = 1024;            // 4 MB per I/O
        private const int Block = Page * BlockPages;
        private const double SpikeMs = 2000;
        private const string FilePrefix = "dumpfinder_stress_";


        /// <summary>
        /// Deletes test files (%TEMP%\dumpfinder_stress_&lt;pid&gt;.bin) left behind by a run that ended in a crash or
        /// power loss. A file still in use by a running test is held open without sharing, so its deletion simply
        /// fails and is skipped. Safe to call at any time (app startup, start of the disk stage); never throws.
        /// </summary>
        public static void SweepOrphans()
        {
            try
            {
                string dir = Path.GetTempPath();
                foreach (string f in Directory.GetFiles(dir, FilePrefix + "*.bin"))
                {
                    try
                    {
                        // "*.bin" also matches ".bin?" (8.3 quirk): only touch exactly our file names.
                        string name = Path.GetFileName(f);
                        if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;
                        int pid;
                        if (!int.TryParse(name.Substring(FilePrefix.Length, name.Length - FilePrefix.Length - 4), out pid)) continue;
                        File.Delete(f);
                        Log.Info("Removed leftover stress test file " + name);
                    }
                    catch (IOException) { }                 // in use by a running test
                    catch (UnauthorizedAccessException) { }
                    catch (Exception ex) { Log.Warn(ex); }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        /// <summary>
        /// The read-back may run a little past the stage time so that what was written still gets verified, but it is
        /// bounded (25 % of the stage, at least 30 s) and stops at once on user stop / abort.
        /// </summary>
        private static bool VerifyMustStop(StageContext ctx)
        {
            if (ctx.UserStopped || ctx.Abort) return true;
            int d = ctx.Stage.DurationSec;
            return ctx.Elapsed >= d + Math.Max(30, d / 4);
        }

        private static long FreeBytes(string root)
        {
            try { return new DriveInfo(root).AvailableFreeSpace; } catch { return long.MaxValue; }
        }

        private sealed class Stats
        {
            public long Written, Verified;
            public double MaxLatencyMs;
            public int Spikes;
            public double MinWriteMbps = double.NaN, WriteMs;
        }

        public static StressStageResult Run(StageContext ctx)
        {
            SweepOrphans(); // leftovers of a crashed run would eat the free space measured below
            string drive = StressPlanner.TestDrive;
            string root = Path.GetPathRoot(Path.GetTempPath());
            long reserve = 10L << 30;
            try { reserve = StressPlanner.DiskReserve(new DriveInfo(root).TotalSize); } catch (Exception ex) { Log.Warn(ex); }
            // Free space may have changed since the plan was built (it can be hours old): measure it again now.
            long span = Math.Min(ctx.Stage.DiskSpanBytes, StressPlanner.DiskSpanFor(ctx.Stage.DiskSpanBytes));
            span -= span % Block;
            if (span < 1L << 30)
                return StressAdvice.Skipped(ctx, string.Format(Localizer.S("disk_stress.not_enough_free_space_on_for"), drive, StressText.Gb(reserve + (1L << 30))));

            long writeCap = StressPlanner.DiskWriteCap(span);
            bool capped = false;
            string path = Path.Combine(Path.GetTempPath(), FilePrefix + Process.GetCurrentProcess().Id + ".bin");
            IntPtr buf = Native.AllocAligned(Block);
            if (buf == IntPtr.Zero) throw new OutOfMemoryException("VirtualAlloc");
            var st = new Stats();
            ulong seed = (ulong)DateTime.Now.Ticks;
            try
            {
                using (SafeFileHandle h = Native.CreateFileW(path, Native.GENERIC_READ | Native.GENERIC_WRITE, 0, IntPtr.Zero, Native.CREATE_ALWAYS,
                                                               Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_WRITE_THROUGH | Native.FILE_FLAG_DELETE_ON_CLOSE, IntPtr.Zero))
                {
                    if (h.IsInvalid) return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());

                    int cycle = 0;
                    while (!ctx.ShouldStop)
                    {
                        // Total write volume cap: the stage ends early (and passes) rather than wearing the SSD for nothing.
                        if (cycle > 0 && st.Written + span > writeCap) { capped = true; break; }
                        ulong cs = CpuWork.Mix(seed + (ulong)cycle);
                        long written;
                        StressStageResult r = WritePass(ctx, h, buf, span, cs, st, root, reserve / 2, out written);
                        if (r != null) return r;
                        r = VerifyPass(ctx, h, buf, written, cs, st);
                        if (r != null) return r;
                        r = RandomPass(ctx, h, buf, written, cs, st, cycle == 0 ? 45 : 60);
                        if (r != null) return r;
                        cycle++;
                    }
                }
            }
            finally
            {
                Native.FreeAligned(buf);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            string summary = string.Format(Localizer.S("disk_stress.written_then_read_back_from_and"),
                StressText.Gb(st.Verified), drive,
                st.WriteMs > 0 ? StressText.MbPerSec(st.Written / (st.WriteMs / 1000.0) / (1024.0 * 1024)) : "—",
                double.IsNaN(st.MinWriteMbps) ? "—" : StressText.MbPerSec(st.MinWriteMbps),
                st.MaxLatencyMs);
            if (capped)
                summary += " " + string.Format(Localizer.S("disk_stress.write_limit_of_reached_stage_ended"), StressText.Gb(writeCap));
            if (st.Spikes > 0 && !ctx.UserStopped) return StressAdvice.DiskSlow(ctx, summary, st.Spikes);
            return StressAdvice.Passed(ctx, summary);
        }

        private static StressStageResult WritePass(StageContext ctx, SafeFileHandle h, IntPtr buf, long span, ulong seed, Stats st, string root, long minFree, out long written)
        {
            written = 0;
            long dummy;
            if (!Native.SetFilePointerEx(h, 0, out dummy, 0)) return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());
            var sec = Stopwatch.StartNew();
            long secBytes = 0;
            double secMax = 0;
            while (written < span && !ctx.ShouldStop)
            {
                long firstPage = written / Page;
                Generate(buf, firstPage, seed);
                var io = Stopwatch.StartNew();
                uint n;
                if (!Native.WriteFile(h, buf, Block, out n, IntPtr.Zero) || n != Block)
                {
                    int e = Marshal.GetLastWin32Error();
                    if (e == 112 /* DISK_FULL */) break;
                    return StressAdvice.DiskIoFailed(ctx, e);
                }
                st.WriteMs += io.Elapsed.TotalMilliseconds;
                Track(st, io.Elapsed.TotalMilliseconds, ref secMax);
                written += Block;
                st.Written += Block;
                secBytes += Block;
                if (sec.ElapsedMilliseconds >= 1000)
                {
                    double mbps = secBytes / sec.Elapsed.TotalSeconds / (1024.0 * 1024);
                    if (written > (2L << 30)) // ignore the first GB or two (cache warm-up)
                        st.MinWriteMbps = double.IsNaN(st.MinWriteMbps) ? mbps : Math.Min(st.MinWriteMbps, mbps);
                    Publish(ctx, Localizer.S("disk_stress.writing") + StressText.Gb(written) + " / " + StressText.Gb(span), mbps, secMax, st);
                    sec.Restart(); secBytes = 0; secMax = 0;
                    if (FreeBytes(root) < minFree) break; // something else is filling the drive: stop growing the file
                }
            }
            return null;
        }

        private static StressStageResult VerifyPass(StageContext ctx, SafeFileHandle h, IntPtr buf, long written, ulong seed, Stats st)
        {
            long dummy;
            if (!Native.SetFilePointerEx(h, 0, out dummy, 0)) return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());
            var sec = Stopwatch.StartNew();
            long secBytes = 0, pos = 0;
            double secMax = 0;
            while (pos < written && !VerifyMustStop(ctx))
            {
                var io = Stopwatch.StartNew();
                uint n;
                if (!Native.ReadFile(h, buf, Block, out n, IntPtr.Zero) || n != Block)
                    return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());
                Track(st, io.Elapsed.TotalMilliseconds, ref secMax);
                if (StressFaults.Take(ref StressFaults.Disk)) Marshal.WriteByte(buf, Block / 2, (byte)(Marshal.ReadByte(buf, Block / 2) ^ 0x40));
                long bad = Verify(buf, pos / Page, BlockPages, seed);
                if (bad >= 0) return StressAdvice.DiskDataFailed(ctx, bad * (double)Page);
                pos += Block;
                st.Verified += Block;
                secBytes += Block;
                if (sec.ElapsedMilliseconds >= 1000)
                {
                    double mbps = secBytes / sec.Elapsed.TotalSeconds / (1024.0 * 1024);
                    Publish(ctx, Localizer.S("disk_stress.reading_back_and_verifying") + StressText.Gb(pos) + " / " + StressText.Gb(written), mbps, secMax, st);
                    sec.Restart(); secBytes = 0; secMax = 0;
                }
            }
            return null;
        }

        private static StressStageResult RandomPass(StageContext ctx, SafeFileHandle h, IntPtr buf, long written, ulong seed, Stats st, int seconds)
        {
            long pages = written / Page;
            if (pages <= 0) return null;
            var rnd = new Random((int)seed);
            var total = Stopwatch.StartNew();
            var sec = Stopwatch.StartNew();
            long ops = 0;
            double secMax = 0;
            while (!ctx.ShouldStop && total.Elapsed.TotalSeconds < seconds)
            {
                long page = (long)(rnd.NextDouble() * pages);
                long dummy;
                if (!Native.SetFilePointerEx(h, page * Page, out dummy, 0)) return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());
                var io = Stopwatch.StartNew();
                uint n;
                if (!Native.ReadFile(h, buf, Page, out n, IntPtr.Zero) || n != Page)
                    return StressAdvice.DiskIoFailed(ctx, Marshal.GetLastWin32Error());
                Track(st, io.Elapsed.TotalMilliseconds, ref secMax);
                long bad = Verify(buf, page, 1, seed);
                if (bad >= 0) return StressAdvice.DiskDataFailed(ctx, bad * (double)Page);
                ops++;
                if (sec.ElapsedMilliseconds >= 1000)
                {
                    double iops = ops / sec.Elapsed.TotalSeconds;
                    Publish(ctx, string.Format(Localizer.S("disk_stress.verified_random_4k_reads_0_n0"), iops),
                        iops * Page / (1024.0 * 1024), secMax, st);
                    sec.Restart(); ops = 0; secMax = 0;
                }
            }
            return null;
        }

        private static void Track(Stats st, double ms, ref double secMax)
        {
            if (ms > secMax) secMax = ms;
            if (ms > st.MaxLatencyMs) st.MaxLatencyMs = ms;
            if (ms > SpikeMs) st.Spikes++;
        }

        private static void Publish(StageContext ctx, string status, double mbps, double latMs, Stats st)
        {
            ctx.Publish(status, new[]
            {
                StressTile.Make(StressText.Throughput, StressText.MbPerSec(mbps), StressPlanner.TestDrive),
                StressTile.Make(StressText.Latency, latMs.ToString("N0") + " ms", StressText.Max + " " + st.MaxLatencyMs.ToString("N0") + " ms", st.Spikes > 0 ? 2 : 0),
                StressTile.Make(Localizer.S("disk_stress.verified"), StressText.Gb(st.Verified), Localizer.S("disk_stress.written") + StressText.Gb(st.Written)),
                ctx.ErrorTile(0)
            }, mbps, latMs,
            Localizer.S("disk_stress.violet_throughput_mb_s_amber_slowest"));
        }

        /// <summary>Each 4 KB page holds a pseudo-random stream seeded by its own index, so any page can be checked alone.</summary>
        private static unsafe void Generate(IntPtr buf, long firstPage, ulong seed)
        {
            Parallel.For(0, BlockPages, k =>
            {
                ulong* p = (ulong*)buf + (long)k * (Page / 8);
                ulong x = CpuWork.Mix(seed ^ (ulong)(firstPage + k)) | 1;
                for (int i = 0; i < Page / 8; i++) { x ^= x >> 12; x ^= x << 25; x ^= x >> 27; p[i] = x * 0x2545F4914F6CDD1DUL; }
            });
        }

        /// <summary>Returns the first corrupted page index, or -1.</summary>
        private static unsafe long Verify(IntPtr buf, long firstPage, int pages, ulong seed)
        {
            long bad = long.MaxValue;
            Parallel.For(0, pages, k =>
            {
                ulong* p = (ulong*)buf + (long)k * (Page / 8);
                ulong x = CpuWork.Mix(seed ^ (ulong)(firstPage + k)) | 1;
                for (int i = 0; i < Page / 8; i++)
                {
                    x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
                    if (p[i] != x * 0x2545F4914F6CDD1DUL)
                    {
                        long pg = firstPage + k;
                        long cur;
                        do { cur = System.Threading.Interlocked.Read(ref bad); }
                        while (pg < cur && System.Threading.Interlocked.CompareExchange(ref bad, pg, cur) != cur);
                        return;
                    }
                }
            });
            return bad == long.MaxValue ? -1 : bad;
        }
    }
}
