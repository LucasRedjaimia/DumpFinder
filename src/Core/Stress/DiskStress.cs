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
    /// The file is created with delete-on-close, so it disappears even if the app is killed.
    /// </summary>
    internal static class DiskStress
    {
        private const int Page = 4096;
        private const int BlockPages = 1024;            // 4 MB per I/O
        private const int Block = Page * BlockPages;
        private const double SpikeMs = 2000;

        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        private sealed class Stats
        {
            public long Written, Verified;
            public double MaxLatencyMs;
            public int Spikes;
            public double MinWriteMbps = double.NaN, WriteMs;
        }

        public static StressStageResult Run(StageContext ctx)
        {
            long span = ctx.Stage.DiskSpanBytes;
            span -= span % Block;
            string drive = StressPlanner.TestDrive;
            if (span < 1L << 30)
                return StressAdvice.Skipped(ctx, string.Format(L("Pas assez d'espace libre sur {0} pour un test fiable (il faut au moins 11 Go).",
                                                                  "Not enough free space on {0} for a meaningful test (at least 11 GB needed).",
                                                                  "No hay espacio libre suficiente en {0} (se necesitan al menos 11 GB).",
                                                                  "Zu wenig freier Speicher auf {0} (mindestens 11 GB nötig).",
                                                                  "Spazio libero insufficiente su {0} (servono almeno 11 GB).",
                                                                  "Espaço livre insuficiente em {0} (são necessários pelo menos 11 GB)."), drive));

            string path = Path.Combine(Path.GetTempPath(), "dumpfinder_stress_" + Process.GetCurrentProcess().Id + ".bin");
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
                        ulong cs = CpuWork.Mix(seed + (ulong)cycle);
                        long written;
                        StressStageResult r = WritePass(ctx, h, buf, span, cs, st, out written);
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

            string summary = string.Format(L(
                "{0} écrits puis relus directement sur {1} et vérifiés, aucune donnée corrompue. Écriture : {2} en moyenne, {3} au plus bas ; latence max {4:N0} ms.",
                "{0} written then read back from {1} and verified, no corrupted data. Writes: {2} average, {3} at the lowest; peak latency {4:N0} ms.",
                "{0} escritos y releídos de {1} y verificados, sin datos corruptos. Escritura: {2} de media, {3} como mínimo; latencia máx. {4:N0} ms.",
                "{0} auf {1} geschrieben, direkt zurückgelesen und geprüft, keine beschädigten Daten. Schreiben: {2} im Mittel, {3} minimal; max. Latenz {4:N0} ms.",
                "{0} scritti e riletti da {1} e verificati, nessun dato corrotto. Scrittura: {2} in media, {3} al minimo; latenza max {4:N0} ms.",
                "{0} gravados e relidos de {1} e verificados, nenhum dado corrompido. Gravação: {2} em média, {3} no mínimo; latência máx. {4:N0} ms."),
                StressText.Gb(st.Verified), drive,
                st.WriteMs > 0 ? StressText.MbPerSec(st.Written / (st.WriteMs / 1000.0) / (1024.0 * 1024)) : "—",
                double.IsNaN(st.MinWriteMbps) ? "—" : StressText.MbPerSec(st.MinWriteMbps),
                st.MaxLatencyMs);
            if (st.Spikes > 0 && !ctx.UserStopped) return StressAdvice.DiskSlow(ctx, summary, st.Spikes);
            return StressAdvice.Passed(ctx, summary);
        }

        private static StressStageResult WritePass(StageContext ctx, SafeFileHandle h, IntPtr buf, long span, ulong seed, Stats st, out long written)
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
                    Publish(ctx, L("Écriture ", "Writing ", "Escritura ", "Schreiben ", "Scrittura ", "Gravação ") + StressText.Gb(written) + " / " + StressText.Gb(span), mbps, secMax, st);
                    sec.Restart(); secBytes = 0; secMax = 0;
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
            while (pos < written && !(ctx.UserStopped || ctx.Abort))
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
                    Publish(ctx, L("Relecture et vérification ", "Reading back and verifying ", "Relectura y verificación ", "Rücklesen und Prüfen ", "Rilettura e verifica ", "Releitura e verificação ") + StressText.Gb(pos) + " / " + StressText.Gb(written), mbps, secMax, st);
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
                    Publish(ctx, string.Format(L("Lectures aléatoires 4K vérifiées · {0:N0} IOPS", "Verified random 4K reads · {0:N0} IOPS", "Lecturas aleatorias 4K verificadas · {0:N0} IOPS", "Geprüfte zufällige 4K-Lesezugriffe · {0:N0} IOPS", "Letture casuali 4K verificate · {0:N0} IOPS", "Leituras aleatórias 4K verificadas · {0:N0} IOPS"), iops),
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
                StressTile.Make(L("Vérifié", "Verified", "Verificado", "Geprüft", "Verificato", "Verificado"), StressText.Gb(st.Verified), L("écrit : ", "written: ", "escrito: ", "geschrieben: ", "scritto: ", "gravado: ") + StressText.Gb(st.Written)),
                ctx.ErrorTile(0)
            }, mbps, latMs,
            L("Violet : débit (Mo/s)   •   Ambre : latence la plus longue de la seconde (ms)", "Violet: throughput (MB/s)   •   Amber: slowest I/O of the second (ms)",
              "Violeta: velocidad (MB/s)   •   Ámbar: E/S más lenta del segundo (ms)", "Violett: Durchsatz (MB/s)   •   Gelb: langsamster Zugriff der Sekunde (ms)",
              "Viola: velocità (MB/s)   •   Ambra: I/O più lento del secondo (ms)", "Violeta: taxa (MB/s)   •   Âmbar: E/S mais lenta do segundo (ms)"));
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
