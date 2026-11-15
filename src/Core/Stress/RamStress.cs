using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// System memory test over most of the free RAM (not a cache-sized buffer): address-dependent values,
    /// moving inversions and a pseudo-random stream, every word read back and compared.
    /// Coverage is reported the way Karhu / TestMem5 users read it (100 % = all tested memory verified once).
    /// </summary>
    internal static class RamStress
    {
        private const int ChunkWords = 16 * 1024 * 1024; // 128 MB per block
        private const long ChunkBytes = ChunkWords * 8L;
        /// <summary>Self-test cap; unlimited in normal use.</summary>
        internal static long MaxTestBytes = long.MaxValue;

        private sealed class Failure
        {
            public long WordOffset;
            public ulong Expected, Actual;
            public string Pattern;
        }


        private static string PatternName(int k)
        {
            switch (k)
            {
                case 0: return Localizer.S("ram_stress.address_derived_value");
                case 1: return Localizer.S("ram_stress.moving_inversions");
                default: return Localizer.S("ram_stress.pseudo_random_stream");
            }
        }

        public static StressStageResult Run(StageContext ctx)
        {
            ulong total = Native.TotalPhys();
            ulong reserve = Math.Max(1536UL << 20, total / 16); // what Windows keeps for itself
            ulong avail = Native.AvailablePhys();
            ulong target = avail > reserve ? (ulong)((avail - reserve) * 0.92) : 0;
            if (!Environment.Is64BitProcess) target = Math.Min(target, 1UL << 30);
            target = Math.Min(target, (ulong)MaxTestBytes);

            var chunks = new List<ulong[]>();
            GCLatencyMode oldMode = GCSettings.LatencyMode;
            Failure fail = null;
            long verifiedBytes = 0;
            int passes = 0;
            try
            {
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                // 1) Allocate block by block, writing each one at once so the memory is really committed,
                //    and stop before Windows would start paging.
                var tick = Stopwatch.StartNew();
                while ((ulong)(chunks.Count + 1) * (ulong)ChunkBytes <= target && !ctx.ShouldStop)
                {
                    ulong[] c;
                    try { c = new ulong[ChunkWords]; }
                    catch (OutOfMemoryException) { break; }
                    FillAddr(c, (long)chunks.Count * ChunkWords, 0x9E3779B97F4A7C15UL);
                    chunks.Add(c);
                    if (Native.AvailablePhys() < reserve) { chunks.RemoveAt(chunks.Count - 1); break; }
                    if (tick.ElapsedMilliseconds >= 500)
                    {
                        tick.Restart();
                        ctx.Publish(Localizer.S("ram_stress.reserving_memory") + StressText.Gb(chunks.Count * (double)ChunkBytes),
                            Tiles(ctx, chunks.Count * ChunkBytes, total, 0, 0, 0), double.NaN, double.NaN);
                    }
                }
                if (chunks.Count == 0)
                    return StressAdvice.Skipped(ctx, Localizer.S("ram_stress.not_enough_free_memory_to_test"));

                long testedBytes = chunks.Count * ChunkBytes;

                // 2) Worker threads, each owning a share of the blocks.
                int threads = Math.Max(1, Math.Min(Math.Min(Environment.ProcessorCount, 16), chunks.Count));
                var stop = new ManualResetEventSlim(false);
                int curPattern = 0;
                var workers = new Thread[threads];
                int[] passByThread = new int[threads];
                for (int t = 0; t < threads; t++)
                {
                    int ti = t;
                    workers[t] = new Thread(() =>
                    {
                        int pass = 0;
                        while (!stop.IsSet && fail == null)
                        {
                            int kind = pass % 3;
                            curPattern = kind;
                            ulong seed = CpuWork.Mix((ulong)pass * 0x2545F4914F6CDD1DUL + 1);
                            for (int ci = ti; ci < chunks.Count && !stop.IsSet && fail == null; ci += threads)
                            {
                                Failure f = TestChunk(chunks[ci], (long)ci * ChunkWords, kind, seed, stop);
                                if (f != null) { f.Pattern = PatternName(kind); Interlocked.CompareExchange(ref fail, f, null); break; }
                                Interlocked.Add(ref verifiedBytes, kind == 1 ? ChunkBytes * 2 : ChunkBytes);
                            }
                            pass++;
                            passByThread[ti] = pass;
                        }
                    }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "df-ram-" + ti };
                    workers[t].Start();
                }

                long lastBytes = 0;
                var sw = Stopwatch.StartNew();
                while (!ctx.ShouldStop && fail == null)
                {
                    ctx.Sleep(1000);
                    long vb = Interlocked.Read(ref verifiedBytes);
                    double gbps = (vb - lastBytes) / Math.Max(0.2, sw.Elapsed.TotalSeconds) / (1024.0 * 1024 * 1024);
                    lastBytes = vb;
                    sw.Restart();
                    passes = int.MaxValue;
                    foreach (int p in passByThread) passes = Math.Min(passes, p);
                    double coverage = vb * 100.0 / testedBytes;
                    ctx.Publish(string.Format(Localizer.S("ram_stress.pass"), passes + 1, PatternName(curPattern)),
                        Tiles(ctx, testedBytes, total, coverage, gbps, 0), gbps, double.NaN,
                        Localizer.S("ram_stress.violet_verification_throughput_gb_s"));
                }
                stop.Set();
                foreach (var w in workers) w.Join(10000);

                if (fail != null)
                    return StressAdvice.RamFailed(ctx, fail.WordOffset * 8.0, Popcount(fail.Expected ^ fail.Actual), fail.Pattern);

                double cov = Interlocked.Read(ref verifiedBytes) * 100.0 / testedBytes;
                return StressAdvice.Passed(ctx, string.Format(Localizer.S("ram_stress.tested_out_of_of_ram_2"),
                    StressText.Gb(testedBytes), StressText.Gb(total), cov));
            }
            finally
            {
                chunks.Clear();
                GCSettings.LatencyMode = oldMode;
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private static StressTile[] Tiles(StageContext ctx, long tested, ulong total, double coverage, double gbps, int errors)
        {
            return new[]
            {
                StressTile.Make(StressText.Tested, StressText.Gb(tested), Localizer.S("gpu_stress.of") + StressText.Gb(total)),
                StressTile.Make(StressText.Coverage, coverage > 0 ? coverage.ToString("N0") + " %" : "—", Localizer.S("ram_stress.1_pass_100")),
                StressTile.Make(StressText.Throughput, gbps > 0 ? gbps.ToString("N1") + " " + Localizer.S("gpu_stress.gb_s") : "—", Localizer.S("ram_stress.write_verify")),
                ctx.ErrorTile(errors)
            };
        }

        private static unsafe void FillAddr(ulong[] c, long baseWord, ulong seed)
        {
            fixed (ulong* p = c)
            {
                for (long i = 0; i < c.Length; i++) p[i] = CpuWork.Mix((ulong)(baseWord + i) ^ seed);
            }
        }

        /// <summary>Writes then verifies one block with the given method. Returns the first mismatch, or null.</summary>
        private static unsafe Failure TestChunk(ulong[] c, long baseWord, int kind, ulong seed, ManualResetEventSlim stop)
        {
            int n = c.Length;
            fixed (ulong* p = c)
            {
                switch (kind)
                {
                    case 0: // address-derived values: catches address/decoding faults
                        for (int i = 0; i < n; i++) p[i] = CpuWork.Mix((ulong)(baseWord + i) ^ seed);
                        if (StressFaults.Take(ref StressFaults.Ram)) p[n / 2] ^= 1UL << 17;
                        for (int i = 0; i < n; i++)
                        {
                            ulong e = CpuWork.Mix((ulong)(baseWord + i) ^ seed);
                            if (p[i] != e) return new Failure { WordOffset = baseWord + i, Expected = e, Actual = p[i] };
                        }
                        return null;

                    case 1: // moving inversions (memtest86 style): every bit flipped both ways, ascending then descending
                    {
                        ulong pat = seed, inv = ~seed;
                        for (int i = 0; i < n; i++) p[i] = pat;
                        for (int i = 0; i < n; i++)
                        {
                            if (p[i] != pat) return new Failure { WordOffset = baseWord + i, Expected = pat, Actual = p[i] };
                            p[i] = inv;
                        }
                        if (stop.IsSet) return null;
                        for (int i = n - 1; i >= 0; i--)
                        {
                            if (p[i] != inv) return new Failure { WordOffset = baseWord + i, Expected = inv, Actual = p[i] };
                            p[i] = pat;
                        }
                        return null;
                    }

                    default: // pseudo-random stream: data-dependent faults
                    {
                        ulong x = CpuWork.Mix(seed ^ (ulong)baseWord) | 1;
                        for (int i = 0; i < n; i++) { x ^= x >> 12; x ^= x << 25; x ^= x >> 27; p[i] = x * 0x2545F4914F6CDD1DUL; }
                        x = CpuWork.Mix(seed ^ (ulong)baseWord) | 1;
                        for (int i = 0; i < n; i++)
                        {
                            x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
                            ulong e = x * 0x2545F4914F6CDD1DUL;
                            if (p[i] != e) return new Failure { WordOffset = baseWord + i, Expected = e, Actual = p[i] };
                        }
                        return null;
                    }
                }
            }
        }

        private static int Popcount(ulong v) { int c = 0; while (v != 0) { v &= v - 1; c++; } return c; }

    }
}
