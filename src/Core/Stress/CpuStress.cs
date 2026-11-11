using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// Deterministic CPU workloads. Instability shows up as a wrong result long before a crash
    /// (that is how Prime95 / y-cruncher / OCCT detect it), so every unit of work is compared
    /// against a reference result computed at the start.
    /// </summary>
    internal static class CpuWork
    {
        public const int ScratchWords = 32 * 1024; // 256 KB: stays in L2, keeps the core (not the RAM) busy

        /// <summary>Integer load (xorshift + multiply over an L2 table): light, highest boost clocks, CoreCycler-like.</summary>
        public static ulong Int(ulong[] s, int rounds, ulong seed)
        {
            ulong x = seed | 1;
            for (int i = 0; i < s.Length; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; s[i] = x; }
            int mask = s.Length - 1;
            for (int r = 0; r < rounds; r++)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                    ulong v = s[(int)(x & (ulong)mask)];
                    s[i] = (s[i] * 0x9E3779B97F4A7C15UL) ^ (v >> 3) ^ x;
                }
            }
            ulong h = x;
            for (int i = 0; i < s.Length; i += 61) h = (h ^ s[i]) * 0x100000001B3UL;
            return h;
        }

        /// <summary>
        /// Floating-point SIMD load (AVX2 when Vector&lt;T&gt; is hardware accelerated): eight independent
        /// logistic-map chains. The map is chaotic, so one wrong bit grows instead of fading away.
        /// </summary>
        public static ulong Vec(int iters, ulong seed)
        {
            int w = Vector<double>.Count;
            var a = new double[w];
            Vector<double> v0 = Start(a, seed, 0), v1 = Start(a, seed, 1), v2 = Start(a, seed, 2), v3 = Start(a, seed, 3),
                           v4 = Start(a, seed, 4), v5 = Start(a, seed, 5), v6 = Start(a, seed, 6), v7 = Start(a, seed, 7);
            var r = new Vector<double>(3.99);
            var one = Vector<double>.One;
            for (int i = 0; i < iters; i++)
            {
                v0 = r * v0 * (one - v0); v1 = r * v1 * (one - v1);
                v2 = r * v2 * (one - v2); v3 = r * v3 * (one - v3);
                v4 = r * v4 * (one - v4); v5 = r * v5 * (one - v5);
                v6 = r * v6 * (one - v6); v7 = r * v7 * (one - v7);
            }
            ulong h = 0xCBF29CE484222325UL;
            h = Fold(h, v0, w); h = Fold(h, v1, w); h = Fold(h, v2, w); h = Fold(h, v3, w);
            h = Fold(h, v4, w); h = Fold(h, v5, w); h = Fold(h, v6, w); h = Fold(h, v7, w);
            return h;
        }

        private static Vector<double> Start(double[] a, ulong seed, int chain)
        {
            for (int j = 0; j < a.Length; j++)
            {
                ulong z = Mix(seed + (ulong)(chain * 64 + j));
                a[j] = 0.1 + 0.8 * ((z >> 11) * (1.0 / 9007199254740992.0));
            }
            return new Vector<double>(a);
        }

        private static ulong Fold(ulong h, Vector<double> v, int w)
        {
            for (int j = 0; j < w; j++) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v[j])) * 0x100000001B3UL;
            return h;
        }

        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static bool SimdAccelerated { get { return Vector.IsHardwareAccelerated; } }

        /// <summary>"AVX2 (256 bits)", "SSE (128 bits)" or a scalar fallback.</summary>
        public static string SimdLabel
        {
            get
            {
                if (!Vector.IsHardwareAccelerated) return Localizer.L("sans SIMD", "no SIMD", "sin SIMD", "ohne SIMD", "senza SIMD", "sem SIMD");
                int bits = Vector<byte>.Count * 8;
                return (bits >= 256 ? "AVX2" : "SSE") + " (" + bits + " bits)";
            }
        }
    }

    /// <summary>Calibrated work sizes and the reference results every thread must reproduce.</summary>
    internal sealed class CpuCalib
    {
        public int Rounds, Iters;
        public ulong Seed = 0x5DEECE66DUL, GoldInt, GoldVec;
        public bool Consistent = true;

        /// <summary>Sizes one unit of each workload to about <paramref name="targetMs"/> and records its reference result (twice).</summary>
        public static CpuCalib Make(int targetMs)
        {
            var c = new CpuCalib();
            var s = new ulong[CpuWork.ScratchWords];
            var sw = Stopwatch.StartNew();
            CpuWork.Int(s, 2, c.Seed);
            double perRound = Math.Max(0.05, sw.Elapsed.TotalMilliseconds / 2.0);
            c.Rounds = Math.Max(1, (int)(targetMs / perRound));

            sw.Restart();
            CpuWork.Vec(20000, c.Seed);
            double perIter = Math.Max(1e-6, sw.Elapsed.TotalMilliseconds / 20000.0);
            c.Iters = Math.Max(20000, (int)(targetMs / perIter));

            c.GoldInt = CpuWork.Int(s, c.Rounds, c.Seed);
            c.GoldVec = CpuWork.Vec(c.Iters, c.Seed);
            c.Consistent = CpuWork.Int(s, c.Rounds, c.Seed) == c.GoldInt && CpuWork.Vec(c.Iters, c.Seed) == c.GoldVec;
            return c;
        }
    }

    /// <summary>All-core load (optionally gated on/off for load transients), with result verification.</summary>
    internal sealed class CpuLoadPool : IDisposable
    {
        private readonly Thread[] _threads;
        private readonly CpuCalib _calib;
        private volatile bool _quit;
        public volatile bool On = true;
        public long Units;
        public int Errors;
        public int FirstErrorCpu = -1;
        public string FirstErrorWork;

        public CpuLoadPool(int threads, CpuCalib calib)
        {
            _calib = calib;
            _threads = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                int idx = t;
                _threads[t] = new Thread(() => Worker(idx)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "df-cpu-" + idx };
                _threads[t].Start();
            }
        }

        private void Worker(int idx)
        {
            var s = new ulong[CpuWork.ScratchWords];
            bool integer = (idx & 1) == 0;
            while (!_quit)
            {
                if (!On) { Thread.Sleep(3); continue; }
                bool ok = integer
                    ? CpuWork.Int(s, _calib.Rounds, _calib.Seed) == _calib.GoldInt
                    : CpuWork.Vec(_calib.Iters, _calib.Seed) == _calib.GoldVec;
                if (StressFaults.Take(ref StressFaults.Cpu)) ok = false;
                if (!ok && Interlocked.Increment(ref Errors) == 1)
                {
                    FirstErrorCpu = Native.GetCurrentProcessorNumber();
                    FirstErrorWork = integer ? Localizer.L("entière", "integer", "entera", "Ganzzahl", "interi", "inteira") : CpuWork.SimdLabel;
                }
                integer = !integer;
                Interlocked.Increment(ref Units);
            }
        }

        public void Dispose()
        {
            _quit = true;
            foreach (var t in _threads) t.Join(3000);
        }
    }

    internal static class CpuStress
    {
        /// <summary>Every logical processor loaded with verified work (integer and SIMD alternating).</summary>
        public static StressStageResult RunAllCore(StageContext ctx)
        {
            ctx.Publish(StressText.Calibrating, CpuTiles(ctx, 0, 0, 0, Environment.ProcessorCount), double.NaN, double.NaN);
            var calib = CpuCalib.Make(120);
            if (!calib.Consistent)
                return StressAdvice.CpuFailed(ctx, -1, StressText.CpuInconsistent);

            int threads = Environment.ProcessorCount;
            int peak = 0;
            using (var pool = new CpuLoadPool(threads, calib))
            {
                while (!ctx.ShouldStop && pool.Errors == 0)
                {
                    ctx.Sleep(1000);
                    int mhz = ctx.Cpu.TotalMhz();
                    if (mhz > peak) peak = mhz;
                    ctx.Publish(Localizer.L("Tous les cœurs · entier + ", "All cores · integer + ", "Todos los núcleos · entero + ", "Alle Kerne · Ganzzahl + ", "Tutti i core · interi + ", "Todos os núcleos · inteiro + ") + CpuWork.SimdLabel + " · " + threads + " threads",
                        CpuTiles(ctx, mhz, peak, Interlocked.Read(ref pool.Units), threads), mhz > 0 ? mhz : double.NaN, ctx.CpuTempValue,
                        Localizer.L("Violet : fréquence effective moyenne (MHz)   •   Ambre : température CPU (°C)", "Violet: average effective clock (MHz)   •   Amber: CPU temperature (°C)", "Violeta: frecuencia efectiva media (MHz)   •   Ámbar: temperatura CPU (°C)", "Violett: mittlerer effektiver Takt (MHz)   •   Gelb: CPU-Temperatur (°C)", "Viola: frequenza effettiva media (MHz)   •   Ambra: temperatura CPU (°C)", "Violeta: frequência efetiva média (MHz)   •   Âmbar: temperatura da CPU (°C)"));
                }
                if (pool.Errors > 0)
                    return StressAdvice.CpuFailed(ctx, pool.FirstErrorCpu, pool.FirstErrorWork);
                return StressAdvice.Passed(ctx, string.Format(Localizer.L(
                    "{0:N0} calculs vérifiés sur {1} threads, aucun écart. Fréquence max observée : {2}.",
                    "{0:N0} verified computations on {1} threads, no mismatch. Peak clock seen: {2}.",
                    "{0:N0} cálculos verificados en {1} hilos, sin diferencias. Frecuencia máx.: {2}.",
                    "{0:N0} geprüfte Berechnungen auf {1} Threads, keine Abweichung. Max. Takt: {2}.",
                    "{0:N0} calcoli verificati su {1} thread, nessuna discrepanza. Frequenza max: {2}.",
                    "{0:N0} cálculos verificados em {1} threads, nenhuma divergência. Frequência máx.: {2}."),
                    Interlocked.Read(ref pool.Units), threads, Mhz(peak)) + ctx.CpuTempSummary);
            }
        }

        /// <summary>
        /// One thread pinned to each physical core in turn (CoreCycler method): light integer load first
        /// (highest boost, where Curve Optimizer / undervolt errors show), then SIMD. All cores are tested
        /// even after a failure, so the result lists every weak core.
        /// </summary>
        public static StressStageResult RunPerCore(StageContext ctx)
        {
            ctx.Publish(StressText.Calibrating, CoreTiles(ctx, 0, 0, 0, 0, 0, 0), double.NaN, double.NaN);
            var calib = CpuCalib.Make(80);
            if (!calib.Consistent)
                return StressAdvice.CpuFailed(ctx, -1, StressText.CpuInconsistent);

            List<int> cores = Native.FirstLogicalOfEachCore();
            var bad = new List<int>();
            long units = 0;
            int perCore = Math.Max(5, ctx.Stage.PerCoreSec);

            for (int c = 0; c < cores.Count && !ctx.UserStopped; c++)
            {
                int logical = cores[c];
                int coreNo = c;
                bool failed = false;
                var done = new ManualResetEventSlim(false);
                var sw = Stopwatch.StartNew();
                string phase = "";
                var worker = new Thread(() =>
                {
                    Thread.BeginThreadAffinity();
                    Native.PinCurrentThread(logical);
                    var s = new ulong[CpuWork.ScratchWords];
                    try
                    {
                        while (!ctx.UserStopped && sw.Elapsed.TotalSeconds < perCore)
                        {
                            bool light = sw.Elapsed.TotalSeconds < perCore * 0.6;
                            phase = light ? "light" : "simd";
                            bool ok = light
                                ? CpuWork.Int(s, calib.Rounds, calib.Seed) == calib.GoldInt
                                : CpuWork.Vec(calib.Iters, calib.Seed) == calib.GoldVec;
                            Interlocked.Increment(ref units);
                            if (!ok) { failed = true; break; }
                        }
                    }
                    finally
                    {
                        Native.PinCurrentThread(-1);
                        Thread.EndThreadAffinity();
                        done.Set();
                    }
                }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "df-core-" + coreNo };
                worker.Start();

                ctx.Cpu.LogicalMhz(logical); // primes the per-core counter
                int peak = 0;
                while (!done.Wait(1000))
                {
                    int mhz = ctx.Cpu.LogicalMhz(logical);
                    if (mhz > peak) peak = mhz;
                    string load = phase == "simd" ? CpuWork.SimdLabel : Localizer.L("charge légère (entier)", "light load (integer)", "carga ligera (entero)", "leichte Last (Ganzzahl)", "carico leggero (interi)", "carga leve (inteiro)");
                    ctx.Publish(string.Format(Localizer.L("Cœur {0}/{1} · {2}", "Core {0}/{1} · {2}", "Núcleo {0}/{1} · {2}", "Kern {0}/{1} · {2}", "Core {0}/{1} · {2}", "Núcleo {0}/{1} · {2}"), coreNo + 1, cores.Count, load),
                        CoreTiles(ctx, mhz, peak, coreNo + 1, cores.Count, Interlocked.Read(ref units), bad.Count + (failed ? 1 : 0)),
                        mhz > 0 ? mhz : double.NaN, ctx.CpuTempValue,
                        Localizer.L("Violet : fréquence du cœur testé (MHz)   •   Ambre : température CPU (°C)", "Violet: clock of the core under test (MHz)   •   Amber: CPU temperature (°C)", "Violeta: frecuencia del núcleo probado (MHz)   •   Ámbar: temperatura CPU (°C)", "Violett: Takt des getesteten Kerns (MHz)   •   Gelb: CPU-Temperatur (°C)", "Viola: frequenza del core testato (MHz)   •   Ambra: temperatura CPU (°C)", "Violeta: frequência do núcleo testado (MHz)   •   Âmbar: temperatura da CPU (°C)"));
                }
                if (failed)
                {
                    bad.Add(coreNo);
                    ctx.Log(string.Format(Localizer.L("Cœur {0} : résultat faux ({1}).", "Core {0}: wrong result ({1}).", "Núcleo {0}: resultado erróneo ({1}).", "Kern {0}: falsches Ergebnis ({1}).", "Core {0}: risultato errato ({1}).", "Núcleo {0}: resultado errado ({1})."),
                        coreNo, phase == "simd" ? CpuWork.SimdLabel : Localizer.L("charge légère", "light load", "carga ligera", "leichte Last", "carico leggero", "carga leve")));
                }
            }

            if (bad.Count > 0) return StressAdvice.CoresFailed(ctx, bad, cores.Count);
            return StressAdvice.Passed(ctx, string.Format(Localizer.L(
                "{0} cœurs testés un par un ({1} s chacun), {2:N0} calculs vérifiés, aucun écart.",
                "{0} cores tested one by one ({1} s each), {2:N0} verified computations, no mismatch.",
                "{0} núcleos probados uno a uno ({1} s cada uno), {2:N0} cálculos verificados, sin diferencias.",
                "{0} Kerne einzeln getestet (je {1} s), {2:N0} geprüfte Berechnungen, keine Abweichung.",
                "{0} core testati uno per uno ({1} s ciascuno), {2:N0} calcoli verificati, nessuna discrepanza.",
                "{0} núcleos testados um a um ({1} s cada), {2:N0} cálculos verificados, nenhuma divergência."),
                cores.Count, perCore, units) + ctx.CpuTempSummary);
        }

        private static StressTile[] CpuTiles(StageContext ctx, int mhz, int peak, long units, int threads)
        {
            return new[]
            {
                StressTile.Make(StressText.EffectiveClock, Mhz(mhz), StressText.Max + " " + Mhz(peak)),
                ctx.CpuTempTile(),
                StressTile.Make(StressText.Verified, units.ToString("N0"), StressText.NoMismatch),
                ctx.ErrorTile(0)
            };
        }

        private static StressTile[] CoreTiles(StageContext ctx, int mhz, int peak, int core, int cores, long units, int badCores)
        {
            return new[]
            {
                StressTile.Make(StressText.CoreClock, Mhz(mhz), StressText.Max + " " + Mhz(peak)),
                StressTile.Make(StressText.Core, core > 0 ? core + " / " + cores : "—", StressText.CpuTemp + " " + ctx.CpuTempText),
                StressTile.Make(StressText.Verified, units.ToString("N0"), badCores == 0 ? StressText.NoMismatch : badCores + " " + Localizer.L("cœur(s) en erreur", "failing core(s)", "núcleo(s) con error", "fehlerhafte(r) Kern(e)", "core in errore", "núcleo(s) com erro"), badCores == 0 ? 0 : 2),
                ctx.ErrorTile(badCores)
            };
        }

        public static string Mhz(int mhz) { return mhz > 0 ? mhz.ToString("N0") + " MHz" : StressText.NotAvailable; }
    }
}
