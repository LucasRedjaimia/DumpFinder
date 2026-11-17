using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace DumpFinder.Mac.Core;

public enum StressStage { Cpu, Ram, Disk, Gpu }

/// <summary>A live reading of a stage: how far it is, its speed and the errors found so far.</summary>
public sealed record StressTick(StressStage Stage, double Fraction, double Rate, string RateUnit, long Errors, string Detail = "");

/// <summary>What a stage did: whether it ran, errors, speed, and why it stopped early ("" when it ran to the end).</summary>
public sealed record StageOutcome(StressStage Stage, bool Ran, long Errors, double Rate, string RateUnit, TimeSpan Elapsed, string Stopped = "", string Detail = "");

/// <summary>
/// Load generators that check their own work (Windows src/Core/Stress): every computation has a known answer, so a
/// single wrong result is an error. A stable Mac reports 0 errors whatever the load.
/// </summary>
public static class StressEngines
{
    /// <summary>Test hook (Windows StressFaults): each engine corrupts one result on purpose, to prove it is caught.</summary>
    public static bool InjectFault { get; set; }

    // ---------- Processor: every core, integer hashing + floating point chains checked against a reference ----------
    /// <summary>Same work on every core; the answers must match the reference computed before the load.</summary>
    public static StageOutcome Cpu(TimeSpan duration, CancellationToken ct, Action<StressTick> tick, int? threads = null)
    {
        int n = threads ?? Environment.ProcessorCount;
        var block = new byte[1 << 16];
        new Random(1234).NextBytes(block);
        var expectedHash = SHA256.HashData(block);
        double expectedFloat = FloatChain(0.5, 200_000) + (InjectFault ? 1e-9 : 0);
        long ops = 0, errors = 0;
        var sw = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, n).Select(_ => Task.Factory.StartNew(() =>
        {
            Span<byte> hash = stackalloc byte[32];
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                SHA256.HashData(block, hash);
                if (!hash.SequenceEqual(expectedHash)) Interlocked.Increment(ref errors);
                if (FloatChain(0.5, 200_000) != expectedFloat) Interlocked.Increment(ref errors);
                Interlocked.Increment(ref ops);
            }
        }, TaskCreationOptions.LongRunning)).ToArray();
        return Watch(StressStage.Cpu, duration, ct, tick, sw, workers, () => (Interlocked.Read(ref ops), Interlocked.Read(ref errors)), "ops/s", 1);
    }

    static double FloatChain(double x, int rounds)
    {
        double a = x, b = 1.0000001, c = 0.0000003;
        for (int i = 0; i < rounds; i++) a = Math.FusedMultiplyAdd(a, b, c) * 0.9999999 + 0.0000001;
        return a;
    }

    // ---------- Memory: patterns written then verified, pass after pass ----------
    /// <summary>
    /// Fills <paramref name="bytes"/> of memory with an address-derived pattern (a new seed each pass), then reads it
    /// all back: a bit that changes on its own is an error. Native memory, released at the end.
    /// </summary>
    public static unsafe StageOutcome Ram(long bytes, TimeSpan duration, CancellationToken ct, Action<StressTick> tick)
    {
        const long chunk = 64L << 20;
        int chunks = (int)Math.Max(1, bytes / chunk);
        var blocks = new List<IntPtr>();
        long errors = 0, done = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < chunks && !ct.IsCancellationRequested; i++) blocks.Add((IntPtr)NativeMemory.Alloc((nuint)chunk));
            var worker = Task.Factory.StartNew(() =>
            {
                ulong seed = 0x9E3779B97F4A7C15;
                while (sw.Elapsed < duration && !ct.IsCancellationRequested)
                {
                    seed = seed * 6364136223846793005 + 1442695040888963407;
                    var s = seed;
                    Parallel.ForEach(blocks, new ParallelOptions { CancellationToken = CancellationToken.None }, (b, _, bi) =>
                    {
                        var p = (ulong*)b;
                        long words = chunk / 8;
                        ulong k = s ^ ((ulong)bi << 40);
                        for (long w = 0; w < words; w++) p[w] = k ^ (ulong)w * 0xD6E8FEB86659FD93;
                        if (InjectFault && bi == 0) p[words / 2] ^= 1UL << 17;   // one flipped bit
                        long bad = 0;
                        for (long w = 0; w < words; w++) if (p[w] != (k ^ (ulong)w * 0xD6E8FEB86659FD93)) bad++;
                        if (bad > 0) Interlocked.Add(ref errors, bad);
                    });
                    Interlocked.Add(ref done, blocks.Count * chunk * 2);   // written + read
                }
            }, TaskCreationOptions.LongRunning);
            return Watch(StressStage.Ram, duration, ct, tick, sw, new[] { worker }, () => (Interlocked.Read(ref done), Interlocked.Read(ref errors)), "MB/s", 1048576,
                         $"{blocks.Count * chunk / 1073741824.0:0.#} GB");
        }
        finally
        {
            foreach (var b in blocks) NativeMemory.Free((void*)b);
        }
    }

    /// <summary>How much memory the test may take: 60 % of what is free, at most 70 % of the total (no swap, no Jetsam).</summary>
    public static long RamBudget(long totalBytes, int? freePct)
    {
        long free = totalBytes * (freePct ?? 50) / 100;
        return Math.Max(256L << 20, Math.Min(free * 6 / 10, totalBytes * 7 / 10));
    }

    // ---------- Disk: blocks written with a seed, flushed, read back past the cache and verified ----------
    [DllImport("libc", SetLastError = true)] static extern int fcntl(int fd, int cmd, int arg);
    const int F_NOCACHE = 48;

    /// <summary>
    /// Writes a test file of <paramref name="bytes"/> in 4 MB blocks (each block from its own seed), flushes it, reads
    /// it back with the cache bypassed and compares every block. The file is deleted at the end.
    /// </summary>
    public static StageOutcome Disk(string folder, long bytes, TimeSpan duration, CancellationToken ct, Action<StressTick> tick)
    {
        const int blockSize = 4 << 20;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "dumpfinder-stress.bin");
        long errors = 0, done = 0;
        var sw = Stopwatch.StartNew();
        var worker = Task.Factory.StartNew(() =>
        {
            var buf = new byte[blockSize];
            var check = new byte[blockSize];
            int blocks = (int)Math.Max(1, bytes / blockSize);
            int pass = 0;
            try
            {
                while (sw.Elapsed < duration && !ct.IsCancellationRequested)
                {
                    pass++;
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.None))
                    {
                        NoCache(fs);
                        for (int i = 0; i < blocks && sw.Elapsed < duration && !ct.IsCancellationRequested; i++)
                        {
                            FillBlock(buf, (ulong)(pass * 100_003 + i));
                            fs.Write(buf);
                            Interlocked.Add(ref done, blockSize);
                        }
                        fs.Flush(true);
                    }
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.SequentialScan))
                    {
                        NoCache(fs);
                        for (int i = 0; i < blocks && !ct.IsCancellationRequested; i++)
                        {
                            int read = 0;
                            while (read < blockSize && fs.Read(check, read, blockSize - read) is var r && r > 0) read += r;
                            if (read == 0) break;
                            FillBlock(buf, (ulong)(pass * 100_003 + i));
                            if (InjectFault && i == 0) check[read / 2] ^= 0x10;
                            if (!buf.AsSpan(0, read).SequenceEqual(check.AsSpan(0, read))) Interlocked.Increment(ref errors);
                            Interlocked.Add(ref done, read);
                        }
                    }
                }
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        }, TaskCreationOptions.LongRunning);
        return Watch(StressStage.Disk, duration, ct, tick, sw, new[] { worker }, () => (Interlocked.Read(ref done), Interlocked.Read(ref errors)), "MB/s", 1048576,
                     $"{bytes / 1073741824.0:0.#} GB");

        // xorshift64*: deterministic from the block's seed, far faster than Random.NextBytes (the disk sets the pace).
        static void FillBlock(byte[] buf, ulong seed)
        {
            var words = MemoryMarshal.Cast<byte, ulong>(buf.AsSpan());
            ulong x = seed * 0x9E3779B97F4A7C15 | 1;
            for (int w = 0; w < words.Length; w++)
            {
                x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
                words[w] = x * 0x2545F4914F6CDD1D;
            }
        }

        static void NoCache(FileStream fs)
        {
            try { fcntl((int)fs.SafeFileHandle.DangerousGetHandle(), F_NOCACHE, 1); } catch (EntryPointNotFoundException) { }
        }
    }

    /// <summary>Size of the disk test file: 2 GB, or 5 % of the free space when that is less (at least 256 MB).</summary>
    public static long DiskBudget(long freeBytes) => Math.Max(256L << 20, Math.Min(2L << 30, freeBytes / 20));

    // ---------- Graphics: the Metal helper (gpu-stress) ----------
    public static string? GpuHelper()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        var p = Path.Combine(dir, "gpu-stress");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Runs the helper for the duration and relays its once-a-second readings (passes, errors, GFLOPS).</summary>
    public static StageOutcome Gpu(TimeSpan duration, CancellationToken ct, Action<StressTick> tick)
    {
        var helper = GpuHelper();
        if (helper is null) return new StageOutcome(StressStage.Gpu, false, 0, 0, "GFLOPS", TimeSpan.Zero, "helper");
        var psi = new ProcessStartInfo(helper) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(((int)duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        _ = p.StandardError.ReadToEndAsync();
        long errors = 0;
        double rate = 0;
        string device = "";
        using var reg = ct.Register(() => { try { p.Kill(); } catch (InvalidOperationException) { } });
        while (p.StandardOutput.ReadLine() is { } line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("device", out var d)) { device = d.GetString() ?? ""; continue; }
                if (root.TryGetProperty("errors", out var e)) errors = e.GetInt64();
                if (root.TryGetProperty("gflops", out var g)) rate = g.GetDouble();
                tick(new StressTick(StressStage.Gpu, Math.Min(1, sw.Elapsed / duration), rate, "GFLOPS", errors, device));
            }
            catch (JsonException) { }
        }
        p.WaitForExit();
        string stopped = ct.IsCancellationRequested ? "stopped" : p.ExitCode != 0 ? "helper" : "";
        return new StageOutcome(StressStage.Gpu, p.ExitCode == 0 || ct.IsCancellationRequested, errors, rate, "GFLOPS", sw.Elapsed, stopped, device);
    }

    /// <summary>Reports progress once a second while the workers run; rate = units per second over the last second.</summary>
    static StageOutcome Watch(StressStage stage, TimeSpan duration, CancellationToken ct, Action<StressTick> tick, Stopwatch sw, Task[] workers,
                              Func<(long Done, long Errors)> read, string unit, double divisor, string detail = "")
    {
        long last = 0;
        double rate = 0;
        var lastTime = sw.Elapsed;
        while (!Task.WaitAll(workers, 1000))
        {
            var (done, errors) = read();
            var now = sw.Elapsed;
            rate = (done - last) / divisor / Math.Max(0.001, (now - lastTime).TotalSeconds);
            last = done;
            lastTime = now;
            tick(new StressTick(stage, Math.Min(1, now / duration), rate, unit, errors, detail));
        }
        var (_, finalErrors) = read();
        if (rate == 0 && sw.Elapsed.TotalSeconds > 0) rate = read().Done / divisor / sw.Elapsed.TotalSeconds;
        return new StageOutcome(stage, true, finalErrors, rate, unit, sw.Elapsed, ct.IsCancellationRequested ? "stopped" : "", detail);
    }
}
