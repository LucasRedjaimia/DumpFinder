using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>Minimal OpenCL wrapper: one GPU (the one with the most memory), one queue, three kernels.</summary>
    internal sealed class OpenClDevice : IDisposable
    {
        [DllImport("OpenCL.dll")] private static extern int clGetPlatformIDs(uint n, IntPtr[] p, out uint np);
        [DllImport("OpenCL.dll")] private static extern int clGetDeviceIDs(IntPtr p, ulong type, uint n, IntPtr[] d, out uint nd);
        [DllImport("OpenCL.dll")] private static extern int clGetDeviceInfo(IntPtr d, uint param, UIntPtr size, byte[] val, out UIntPtr ret);
        [DllImport("OpenCL.dll")] private static extern IntPtr clCreateContext(IntPtr props, uint nd, IntPtr[] d, IntPtr cb, IntPtr ud, out int err);
        [DllImport("OpenCL.dll")] private static extern IntPtr clCreateCommandQueue(IntPtr ctx, IntPtr dev, ulong props, out int err);
        [DllImport("OpenCL.dll")] private static extern IntPtr clCreateBuffer(IntPtr ctx, ulong flags, UIntPtr size, IntPtr host, out int err);
        [DllImport("OpenCL.dll")] private static extern IntPtr clCreateProgramWithSource(IntPtr ctx, uint n, string[] src, IntPtr[] lens, out int err);
        [DllImport("OpenCL.dll")] private static extern int clBuildProgram(IntPtr prog, uint nd, IntPtr[] devs, string opts, IntPtr cb, IntPtr ud);
        [DllImport("OpenCL.dll")] private static extern IntPtr clCreateKernel(IntPtr prog, string name, out int err);
        [DllImport("OpenCL.dll")] private static extern int clSetKernelArg(IntPtr k, uint i, UIntPtr size, ref IntPtr val);
        [DllImport("OpenCL.dll")] private static extern int clSetKernelArg(IntPtr k, uint i, UIntPtr size, ref uint val);
        [DllImport("OpenCL.dll")] private static extern int clEnqueueNDRangeKernel(IntPtr q, IntPtr k, uint dim, IntPtr offset, UIntPtr[] global, IntPtr local, uint nw, IntPtr wait, IntPtr evt);
        [DllImport("OpenCL.dll")] private static extern int clEnqueueReadBuffer(IntPtr q, IntPtr buf, uint blocking, UIntPtr offset, UIntPtr size, uint[] dst, uint nw, IntPtr wait, IntPtr evt);
        [DllImport("OpenCL.dll")] private static extern int clEnqueueWriteBuffer(IntPtr q, IntPtr buf, uint blocking, UIntPtr offset, UIntPtr size, uint[] src, uint nw, IntPtr wait, IntPtr evt);
        [DllImport("OpenCL.dll")] private static extern int clFinish(IntPtr q);
        [DllImport("OpenCL.dll")] private static extern int clReleaseMemObject(IntPtr m);
        [DllImport("OpenCL.dll")] private static extern int clReleaseKernel(IntPtr k);
        [DllImport("OpenCL.dll")] private static extern int clReleaseProgram(IntPtr p);
        [DllImport("OpenCL.dll")] private static extern int clReleaseCommandQueue(IntPtr q);
        [DllImport("OpenCL.dll")] private static extern int clReleaseContext(IntPtr c);

        private const string Source = @"
uint vpat(uint i, uint seed) {
    uint v = (i + 0x9E3779B9u) * 2654435761u ^ seed;
    v ^= v >> 15; v *= 0x2C1B3C6Du; v ^= v >> 12;
    return v;
}
// Verified compute: xorshift + a chaotic float map; the same launch must always give the same bits.
__kernel void burn(__global uint* outp, uint iters, uint seed) {
    uint gid = get_global_id(0);
    uint x = (gid + 1u) * 2654435761u ^ seed;
    float4 f = (float4)(0.11f, 0.23f, 0.37f, 0.41f) + (float)(gid & 255u) * 0.0015f;
    float4 g = (float4)(0.53f, 0.61f, 0.67f, 0.71f) - (float)(gid & 127u) * 0.0021f;
    for (uint i = 0; i < iters; ++i) {
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        f = 3.99f * f * (1.0f - f);
        g = 3.97f * g * (1.0f - g);
    }
    outp[gid] = x ^ as_uint(f.x) ^ (as_uint(f.y) * 3u) ^ (as_uint(f.z) * 5u) ^ (as_uint(f.w) * 7u)
              ^ (as_uint(g.x) * 11u) ^ (as_uint(g.y) * 13u) ^ (as_uint(g.z) * 17u) ^ (as_uint(g.w) * 19u);
}
__kernel void vfill(__global uint* p, uint seed, uint base, uint inv) {
    uint i = get_global_id(0);
    p[i] = vpat(base + i, seed) ^ inv;
}
// Reads in a scrambled order (odd stride over a power-of-two chunk) to catch address and refresh faults.
__kernel void vcheck(__global const uint* p, uint seed, uint base, uint inv, uint mask, uint stride, __global uint* errs, uint slot) {
    __global uint* err = errs + slot * 4u;
    uint g = get_global_id(0);
    uint i = (g * stride) & mask;
    uint e = vpat(base + i, seed) ^ inv;
    uint a = p[i];
    if (a != e) {
        uint k = atomic_inc(&err[0]);
        if (k == 0u) { err[1] = i; err[2] = e; err[3] = a; }
    }
}";

        private IntPtr _dev, _ctx, _queue, _prog;
        public IntPtr KBurn, KFill, KCheck;
        public bool Ready { get; private set; }
        public string Name { get; private set; }
        public long GlobalMem { get; private set; }
        public long MaxAlloc { get; private set; }
        public uint ComputeUnits { get; private set; }
        public string Error { get; private set; }

        public static OpenClDevice TryOpen()
        {
            var d = new OpenClDevice();
            try
            {
                if (d.Init()) return d;
            }
            catch (DllNotFoundException) { d.Error = "OpenCL.dll"; }
            catch (EntryPointNotFoundException) { d.Error = "OpenCL.dll"; }
            catch (Exception ex) { Log.Warn(ex); d.Error = ex.Message; }
            d.Dispose();
            return null;
        }

        private bool Init()
        {
            uint np;
            if (clGetPlatformIDs(0, null, out np) != 0 || np == 0) return false;
            var plats = new IntPtr[np];
            clGetPlatformIDs(np, plats, out np);
            long bestMem = -1;
            foreach (IntPtr p in plats)
            {
                uint nd;
                if (clGetDeviceIDs(p, 4 /* GPU */, 0, null, out nd) != 0 || nd == 0) continue;
                var devs = new IntPtr[nd];
                clGetDeviceIDs(p, 4, nd, devs, out nd);
                foreach (IntPtr d in devs)
                {
                    long mem = (long)InfoU64(d, 0x101F); // GLOBAL_MEM_SIZE
                    if (mem > bestMem) { bestMem = mem; _dev = d; }
                }
            }
            if (_dev == IntPtr.Zero) return false;

            Name = InfoString(_dev, 0x102B);
            GlobalMem = (long)InfoU64(_dev, 0x101F);
            MaxAlloc = (long)InfoU64(_dev, 0x1010);
            ComputeUnits = (uint)InfoU64(_dev, 0x1002, 4);

            int err;
            _ctx = clCreateContext(IntPtr.Zero, 1, new[] { _dev }, IntPtr.Zero, IntPtr.Zero, out err);
            if (_ctx == IntPtr.Zero) return false;
            _queue = clCreateCommandQueue(_ctx, _dev, 0, out err);
            if (_queue == IntPtr.Zero) return false;
            _prog = clCreateProgramWithSource(_ctx, 1, new[] { Source }, null, out err);
            if (_prog == IntPtr.Zero || clBuildProgram(_prog, 1, new[] { _dev }, null, IntPtr.Zero, IntPtr.Zero) != 0) return false;
            KBurn = clCreateKernel(_prog, "burn", out err);
            KFill = clCreateKernel(_prog, "vfill", out err);
            KCheck = clCreateKernel(_prog, "vcheck", out err);
            Ready = KBurn != IntPtr.Zero && KFill != IntPtr.Zero && KCheck != IntPtr.Zero;
            return Ready;
        }

        private static ulong InfoU64(IntPtr d, uint param, int size = 8)
        {
            var b = new byte[8];
            UIntPtr r;
            if (clGetDeviceInfo(d, param, (UIntPtr)(uint)size, b, out r) != 0) return 0;
            return size == 4 ? BitConverter.ToUInt32(b, 0) : BitConverter.ToUInt64(b, 0);
        }

        private static string InfoString(IntPtr d, uint param)
        {
            var b = new byte[512];
            UIntPtr r;
            if (clGetDeviceInfo(d, param, (UIntPtr)512u, b, out r) != 0) return "GPU";
            int n = Math.Max(0, Math.Min(511, (int)r.ToUInt32() - 1));
            return Encoding.ASCII.GetString(b, 0, n).Trim();
        }

        public IntPtr NewBuffer(long bytes, out int err)
        {
            return clCreateBuffer(_ctx, 1 /* READ_WRITE */, (UIntPtr)(ulong)bytes, IntPtr.Zero, out err);
        }

        public static void Free(IntPtr mem) { if (mem != IntPtr.Zero) clReleaseMemObject(mem); }

        public int Arg(IntPtr k, uint i, IntPtr mem) { return clSetKernelArg(k, i, (UIntPtr)(uint)IntPtr.Size, ref mem); }
        public int Arg(IntPtr k, uint i, uint v) { return clSetKernelArg(k, i, (UIntPtr)4u, ref v); }

        public int Launch(IntPtr k, long global)
        {
            return clEnqueueNDRangeKernel(_queue, k, 1, IntPtr.Zero, new[] { (UIntPtr)(ulong)global }, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public int Finish() { return clFinish(_queue); }

        public int Read(IntPtr buf, uint[] dst, long count)
        {
            return clEnqueueReadBuffer(_queue, buf, 1, UIntPtr.Zero, (UIntPtr)(ulong)(count * 4), dst, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public int Write(IntPtr buf, uint[] src, long count)
        {
            return clEnqueueWriteBuffer(_queue, buf, 1, UIntPtr.Zero, (UIntPtr)(ulong)(count * 4), src, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            try
            {
                if (KBurn != IntPtr.Zero) clReleaseKernel(KBurn);
                if (KFill != IntPtr.Zero) clReleaseKernel(KFill);
                if (KCheck != IntPtr.Zero) clReleaseKernel(KCheck);
                if (_prog != IntPtr.Zero) clReleaseProgram(_prog);
                if (_queue != IntPtr.Zero) clReleaseCommandQueue(_queue);
                if (_ctx != IntPtr.Zero) clReleaseContext(_ctx);
            }
            catch { }
            KBurn = KFill = KCheck = _prog = _queue = _ctx = IntPtr.Zero;
            Ready = false;
        }
    }

    /// <summary>A calibrated compute launch and its reference output.</summary>
    internal sealed class BurnJob : IDisposable
    {
        public long N;
        public uint Iters;
        public double LaunchMs;
        public uint[] Golden, Scratch;
        public IntPtr Out;
        public string Error;
        private bool _calibrated;
        private OpenClDevice _d;
        private const uint Seed = 0xC0FFEE11u;

        /// <summary>
        /// Sizes one launch to about <paramref name="targetMs"/> (far below the 2 s TDR limit, so the test never
        /// causes the driver reset it is looking for), then records its output twice as the reference.
        /// </summary>
        public static BurnJob Calibrate(OpenClDevice d, int targetMs)
        {
            var j = new BurnJob { _d = d };
            j.N = Math.Max(65536, Math.Min(1L << 21, d.ComputeUnits * 8192L));
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int err;
                Free(j);
                j.Out = d.NewBuffer(j.N * 4, out err);
                if (j.Out == IntPtr.Zero) { j.Error = "clCreateBuffer " + err; return j; }
                j.Iters = 256;
                d.Arg(d.KBurn, 0, j.Out); d.Arg(d.KBurn, 2, Seed);
                double t = j.Time();
                if (t < 0) return j;
                t = j.Time();
                if (t < 0) return j;
                if (t > 400 && j.N > 65536) { j.N /= 4; continue; } // very slow device: smaller launches
                j.Iters = (uint)Math.Max(256, Math.Min(1 << 24, 256.0 * targetMs / Math.Max(0.05, t)));
                break;
            }
            j.LaunchMs = j.Time();
            if (j.LaunchMs < 0) return j;
            j.Golden = new uint[j.N];
            j.Scratch = new uint[j.N];
            int rc = d.Read(j.Out, j.Golden, j.N);
            if (rc != 0) { j.Error = "clEnqueueReadBuffer " + rc; return j; }
            int mism;
            rc = j.Run(out mism);
            if (rc != 0) { j.Error = "OpenCL " + rc; return j; }
            if (mism > 0) j.Error = "inconsistent";
            j._calibrated = true;
            return j;
        }

        private double Time()
        {
            _d.Arg(_d.KBurn, 0, Out); _d.Arg(_d.KBurn, 1, Iters); _d.Arg(_d.KBurn, 2, Seed);
            var sw = Stopwatch.StartNew();
            int rc = _d.Launch(_d.KBurn, N);
            if (rc == 0) rc = _d.Finish();
            if (rc != 0) { Error = "OpenCL " + rc; return -1; }
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>One verified launch. Returns the OpenCL error code (0 = fine) and how many outputs differ.</summary>
        public int Run(out int mismatches)
        {
            mismatches = 0;
            _d.Arg(_d.KBurn, 0, Out); _d.Arg(_d.KBurn, 1, Iters); _d.Arg(_d.KBurn, 2, Seed);
            int rc = _d.Launch(_d.KBurn, N);
            if (rc == 0) rc = _d.Finish();
            if (rc == 0) rc = _d.Read(Out, Scratch, N);
            if (rc != 0) return rc;
            if (_calibrated && StressFaults.Take(ref StressFaults.Gpu)) Scratch[N / 3] ^= 0x10u;
            for (long i = 0; i < N; i++) if (Scratch[i] != Golden[i]) mismatches++;
            return 0;
        }

        private static void Free(BurnJob j) { if (j.Out != IntPtr.Zero) { OpenClDevice.Free(j.Out); j.Out = IntPtr.Zero; } }
        public void Dispose() { Free(this); }
    }

    internal static class GpuStress
    {
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        private static OpenClDevice Device(StageContext ctx, out StressStageResult skip)
        {
            skip = null;
            OpenClDevice d = ctx.GpuDevice != null ? ctx.GpuDevice() : null;
            if (d == null || !d.Ready)
                skip = StressAdvice.GpuUnavailable(ctx, L("pas de carte graphique OpenCL utilisable (pilote absent ou trop ancien).",
                                                          "no usable OpenCL graphics card (driver missing or too old).",
                                                          "no hay GPU OpenCL utilizable (controlador ausente o antiguo).",
                                                          "keine nutzbare OpenCL-Grafikkarte (Treiber fehlt oder zu alt).",
                                                          "nessuna GPU OpenCL utilizzabile (driver assente o troppo vecchio).",
                                                          "nenhuma GPU OpenCL utilizável (driver ausente ou antigo)."));
            return d;
        }

        private static string Num(double v, string unit) { return double.IsNaN(v) ? StressText.NotAvailable : v.ToString("N0") + " " + unit; }
        private static string NumPeak(double v, string unit) { return StressText.Max + " " + Num(v, unit); }

        private static StressTile[] SensorTiles(StageContext ctx, string clockSub, int errors)
        {
            GpuSensor g = ctx.Gpu;
            return new[]
            {
                StressTile.Make(StressText.GpuClock, Num(g.ClockMhz, "MHz"), clockSub),
                StressTile.Make(StressText.GpuPower, Num(g.PowerW, "W"), g.Available ? NumPeak(g.PeakPowerW, "W") : L("capteur indisponible", "no sensor", "sin sensor", "kein Sensor", "nessun sensore", "sem sensor")),
                StressTile.Make(StressText.GpuTemp, Num(g.TempC, "°C"), g.Available ? NumPeak(g.PeakTempC, "°C") : L("capteur indisponible", "no sensor", "sin sensor", "kein Sensor", "nessun sensore", "sem sensor")),
                ctx.ErrorTile(errors)
            };
        }

        private static string PowerLegend
        {
            get
            {
                return L("Violet : puissance GPU (W)   •   Ambre : température GPU (°C)", "Violet: GPU power (W)   •   Amber: GPU temperature (°C)",
                         "Violeta: potencia GPU (W)   •   Ámbar: temperatura GPU (°C)", "Violett: GPU-Leistung (W)   •   Gelb: GPU-Temperatur (°C)",
                         "Viola: potenza GPU (W)   •   Ambra: temperatura GPU (°C)", "Violeta: potência GPU (W)   •   Âmbar: temperatura GPU (°C)");
            }
        }

        // ==================== Verified compute ====================
        public static StressStageResult RunCompute(StageContext ctx)
        {
            StressStageResult skip;
            OpenClDevice d = Device(ctx, out skip);
            if (skip != null) return skip;

            ctx.Publish(StressText.Calibrating, SensorTiles(ctx, d.Name, 0), double.NaN, double.NaN, PowerLegend);
            using (BurnJob job = BurnJob.Calibrate(d, 120))
            {
                if (job.Error == "inconsistent") return StressAdvice.GpuComputeFailed(ctx, L("dès le calibrage", "already during calibration", "ya en la calibración", "schon bei der Kalibrierung", "già in calibrazione", "já na calibração"));
                if (job.Error != null) return StressAdvice.GpuDriverReset(ctx, job.Error);

                long launches = 0;
                var tick = Stopwatch.StartNew();
                while (!ctx.ShouldStop)
                {
                    int bad;
                    int rc = job.Run(out bad);
                    if (rc != 0) return StressAdvice.GpuDriverReset(ctx, "OpenCL " + rc);
                    launches++;
                    if (bad > 0)
                        return StressAdvice.GpuComputeFailed(ctx, string.Format(L("{0} valeur(s) fausse(s) sur {1:N0}", "{0} wrong value(s) out of {1:N0}", "{0} valor(es) erróneo(s) de {1:N0}", "{0} falsche(r) Wert(e) von {1:N0}", "{0} valore/i errato/i su {1:N0}", "{0} valor(es) errado(s) de {1:N0}"), bad, job.N));
                    if (tick.ElapsedMilliseconds >= 1000)
                    {
                        tick.Restart();
                        string sub = string.Format(L("{0:N0} calculs vérifiés", "{0:N0} verified launches", "{0:N0} cálculos verificados", "{0:N0} geprüfte Durchläufe", "{0:N0} calcoli verificati", "{0:N0} cálculos verificados"), launches);
                        ctx.Publish(d.Name + " · " + string.Format(L("{0:F0} ms par calcul", "{0:F0} ms per launch", "{0:F0} ms por cálculo", "{0:F0} ms pro Durchlauf", "{0:F0} ms per calcolo", "{0:F0} ms por cálculo"), job.LaunchMs),
                            SensorTiles(ctx, sub, 0), ctx.Gpu.PowerW, ctx.Gpu.TempC, PowerLegend);
                    }
                }
                return StressAdvice.Passed(ctx, string.Format(L(
                    "{0:N0} calculs vérifiés sur {1}, aucun écart. Puissance max : {2}, température max : {3}.",
                    "{0:N0} verified launches on {1}, no mismatch. Peak power: {2}, peak temperature: {3}.",
                    "{0:N0} cálculos verificados en {1}, sin diferencias. Potencia máx.: {2}, temperatura máx.: {3}.",
                    "{0:N0} geprüfte Durchläufe auf {1}, keine Abweichung. Max. Leistung: {2}, max. Temperatur: {3}.",
                    "{0:N0} calcoli verificati su {1}, nessuna discrepanza. Potenza max: {2}, temperatura max: {3}.",
                    "{0:N0} cálculos verificados em {1}, nenhuma divergência. Potência máx.: {2}, temperatura máx.: {3}."),
                    launches, d.Name, Num(ctx.Gpu.PeakPowerW, "W"), Num(ctx.Gpu.PeakTempC, "°C")));
            }
        }

        // ==================== Video memory ====================
        public static StressStageResult RunVram(StageContext ctx)
        {
            StressStageResult skip;
            OpenClDevice d = Device(ctx, out skip);
            if (skip != null) return skip;

            long chunkWords = d.MaxAlloc >= (256L << 20) ? (1L << 26) : (1L << 24);
            long chunkBytes = chunkWords * 4;
            long target = (long)(d.GlobalMem * 0.80);
            var chunks = new List<IntPtr>();
            int err;
            int maxChunks = (int)Math.Max(1, target / chunkBytes);
            IntPtr errBuf = d.NewBuffer(16L * maxChunks, out err); // one 4-word error slot per chunk
            var zero = new uint[4 * maxChunks];
            var errHost = new uint[4 * maxChunks];
            try
            {
                if (errBuf == IntPtr.Zero) return StressAdvice.GpuDriverReset(ctx, "clCreateBuffer " + err);

                // Allocate chunk by chunk; the driver only commits memory on first use, so fill each one right away.
                while (chunks.Count * chunkBytes + chunkBytes <= target && !ctx.ShouldStop)
                {
                    IntPtr b = d.NewBuffer(chunkBytes, out err);
                    if (b == IntPtr.Zero) break;
                    if (Fill(d, b, (uint)(chunks.Count * chunkWords), 0x13579BDFu, 0, chunkWords) != 0) { OpenClDevice.Free(b); break; }
                    chunks.Add(b);
                    if (chunks.Count % 4 == 0)
                        ctx.Publish(L("Allocation de la mémoire vidéo… ", "Allocating video memory… ", "Reservando memoria de vídeo… ", "Grafikspeicher wird belegt… ", "Allocazione memoria video… ", "Alocando memória de vídeo… ") + StressText.Gb(chunks.Count * chunkBytes),
                            VramTiles(ctx, chunks.Count * chunkBytes, d.GlobalMem, 0, 0), double.NaN, ctx.Gpu.TempC);
                }
                if (chunks.Count == 0)
                    return StressAdvice.GpuUnavailable(ctx, L("impossible d'allouer de la mémoire vidéo.", "could not allocate video memory.", "no se pudo reservar memoria de vídeo.", "Grafikspeicher konnte nicht belegt werden.", "impossibile allocare memoria video.", "não foi possível alocar memória de vídeo."));

                long tested = chunks.Count * chunkBytes;
                long passes = 0, errors = 0;
                double bytesDone = 0, lastBytes = 0;
                var tick = Stopwatch.StartNew();
                uint mask = (uint)(chunkWords - 1);
                while (!ctx.ShouldStop)
                {
                    uint seed = 0x5A5A5A5Au ^ (uint)(passes * 0x01000193L);
                    uint inv = (passes & 1) == 0 ? 0u : 0xFFFFFFFFu;
                    // One pass = fill every chunk, then read each back twice in two scrambled orders; everything is
                    // queued without waiting and the error slots are read once at the end (the GPU never idles).
                    int rc = d.Write(errBuf, zero, 4L * chunks.Count);
                    for (int c = 0; c < chunks.Count && rc == 0; c++)
                        rc = Fill(d, chunks[c], (uint)(c * chunkWords), seed, inv, chunkWords, false);
                    for (int c = 0; c < chunks.Count && rc == 0; c++)
                    {
                        rc = Check(d, chunks[c], (uint)(c * chunkWords), seed, inv, mask, 4099u, errBuf, (uint)c, chunkWords);
                        if (rc == 0) rc = Check(d, chunks[c], (uint)(c * chunkWords), seed, inv, mask, 65537u, errBuf, (uint)c, chunkWords);
                    }
                    if (rc == 0) rc = d.Read(errBuf, errHost, 4L * chunks.Count);
                    if (StressFaults.Take(ref StressFaults.Vram)) { errHost[1] = 4242; errHost[2] = 0xF0F0F0F0u; errHost[3] = 0xF0F0F0F1u; errHost[0] = 1; }
                    if (rc != 0) return StressAdvice.GpuDriverReset(ctx, "OpenCL " + rc);
                    for (int c = 0; c < chunks.Count; c++)
                    {
                        uint n = errHost[c * 4];
                        if (n == 0) continue;
                        for (int k = c; k < chunks.Count; k++) errors += errHost[k * 4];
                        int bits = Popcount(errHost[c * 4 + 2] ^ errHost[c * 4 + 3]);
                        return StressAdvice.VramFailed(ctx, c * (double)chunkBytes + errHost[c * 4 + 1] * 4.0, bits, errors);
                    }
                    passes++;
                    bytesDone += tested * 3.0;
                    if (tick.ElapsedMilliseconds >= 1000)
                    {
                        double gbps = (bytesDone - lastBytes) / tick.Elapsed.TotalSeconds / (1024.0 * 1024 * 1024);
                        lastBytes = bytesDone;
                        tick.Restart();
                        ctx.Publish(string.Format(L("Passe {0:N0} · motif {1}", "Pass {0:N0} · {1} pattern", "Pasada {0:N0} · patrón {1}", "Durchlauf {0:N0} · Muster {1}", "Passaggio {0:N0} · schema {1}", "Passagem {0:N0} · padrão {1}"), passes, inv == 0 ? "A" : "Ā"),
                            VramTiles(ctx, tested, d.GlobalMem, passes, gbps), gbps, ctx.Gpu.TempC,
                            L("Violet : débit mémoire vidéo (Go/s)   •   Ambre : température GPU (°C)", "Violet: video memory throughput (GB/s)   •   Amber: GPU temperature (°C)",
                              "Violeta: velocidad de memoria de vídeo (GB/s)   •   Ámbar: temperatura GPU (°C)", "Violett: Grafikspeicher-Durchsatz (GB/s)   •   Gelb: GPU-Temperatur (°C)",
                              "Viola: velocità memoria video (GB/s)   •   Ambra: temperatura GPU (°C)", "Violeta: taxa da memória de vídeo (GB/s)   •   Âmbar: temperatura GPU (°C)"));
                    }
                }
                return StressAdvice.Passed(ctx, string.Format(L(
                    "{0} de mémoire vidéo écrits et relus {1:N0} fois dans le désordre, aucune donnée corrompue.",
                    "{0} of video memory written and read back {1:N0} times in scrambled order, no corrupted data.",
                    "{0} de memoria de vídeo escritos y releídos {1:N0} veces en desorden, sin datos corruptos.",
                    "{0} Grafikspeicher {1:N0}-mal geschrieben und ungeordnet zurückgelesen, keine beschädigten Daten.",
                    "{0} di memoria video scritti e riletti {1:N0} volte in ordine sparso, nessun dato corrotto.",
                    "{0} de memória de vídeo gravados e relidos {1:N0} vezes fora de ordem, nenhum dado corrompido."),
                    StressText.Gb(tested), passes));
            }
            finally
            {
                foreach (IntPtr b in chunks) OpenClDevice.Free(b);
                OpenClDevice.Free(errBuf);
            }
        }

        private static int Fill(OpenClDevice d, IntPtr buf, uint baseIdx, uint seed, uint inv, long words, bool wait = true)
        {
            d.Arg(d.KFill, 0, buf); d.Arg(d.KFill, 1, seed); d.Arg(d.KFill, 2, baseIdx); d.Arg(d.KFill, 3, inv);
            int rc = d.Launch(d.KFill, words);
            return rc != 0 || !wait ? rc : d.Finish();
        }

        /// <summary>Queued without waiting: the blocking read of the error slots ends the pass.</summary>
        private static int Check(OpenClDevice d, IntPtr buf, uint baseIdx, uint seed, uint inv, uint mask, uint stride, IntPtr errBuf, uint slot, long words)
        {
            d.Arg(d.KCheck, 0, buf); d.Arg(d.KCheck, 1, seed); d.Arg(d.KCheck, 2, baseIdx); d.Arg(d.KCheck, 3, inv);
            d.Arg(d.KCheck, 4, mask); d.Arg(d.KCheck, 5, stride); d.Arg(d.KCheck, 6, errBuf); d.Arg(d.KCheck, 7, slot);
            return d.Launch(d.KCheck, words);
        }

        private static StressTile[] VramTiles(StageContext ctx, long tested, long total, long passes, double gbps)
        {
            return new[]
            {
                StressTile.Make(StressText.Tested, StressText.Gb(tested), Localizer.L("sur ", "of ", "de ", "von ", "su ", "de ") + StressText.Gb(total)),
                StressTile.Make(Localizer.L("Passes", "Passes", "Pasadas", "Durchläufe", "Passaggi", "Passagens"), passes.ToString("N0"), Localizer.L("écriture + 2 relectures", "write + 2 read-backs", "escritura + 2 relecturas", "Schreiben + 2× Lesen", "scrittura + 2 riletture", "gravação + 2 releituras")),
                StressTile.Make(StressText.Throughput, gbps > 0 ? gbps.ToString("N0") + " " + Localizer.L("Go/s", "GB/s", "GB/s", "GB/s", "GB/s", "GB/s") : "—", StressText.GpuTemp + " " + Num(ctx.Gpu.TempC, "°C")),
                ctx.ErrorTile(0)
            };
        }

        private static int Popcount(uint v) { int c = 0; while (v != 0) { v &= v - 1; c++; } return c; }

        // ==================== CPU + GPU load transients ====================
        /// <summary>
        /// Square-wave load on CPU and GPU together (0 % ↔ 100 %), with half-periods from 2 s down to 100 ms.
        /// Sudden swings stress the power supply, the 12V-2x6 / PCIe cables and voltage regulation —
        /// steady loads miss that. Results stay verified on both sides.
        /// </summary>
        public static StressStageResult RunTransient(StageContext ctx)
        {
            StressStageResult skip;
            OpenClDevice d = Device(ctx, out skip);
            if (skip != null) return skip;

            ctx.Publish(StressText.Calibrating, TransientTiles(ctx, 0, 0), double.NaN, double.NaN, PowerLegend);
            var calib = CpuCalib.Make(40);
            if (!calib.Consistent) return StressAdvice.CpuFailed(ctx, -1, StressText.CpuInconsistent);
            using (BurnJob job = BurnJob.Calibrate(d, 30))
            {
                if (job.Error == "inconsistent") return StressAdvice.GpuComputeFailed(ctx, L("dès le calibrage", "already during calibration", "ya en la calibración", "schon bei der Kalibrierung", "già in calibrazione", "già na calibração"));
                if (job.Error != null) return StressAdvice.GpuDriverReset(ctx, job.Error);

                int[] halfPeriods = { 2000, 1000, 500, 250, 100 };
                bool on = false;
                int gpuBad = 0, gpuRc = 0;
                long swings = 0;
                using (var pool = new CpuLoadPool(Environment.ProcessorCount, calib) { On = false })
                {
                    var gpuThread = new Thread(() =>
                    {
                        while (!ctx.ShouldStop && gpuBad == 0 && gpuRc == 0)
                        {
                            if (!on) { Thread.Sleep(2); continue; }
                            int bad;
                            int rc = job.Run(out bad);
                            if (rc != 0) gpuRc = rc;
                            if (bad > 0) gpuBad = bad;
                        }
                    }) { IsBackground = true, Name = "df-gpu-transient" };
                    gpuThread.Start();

                    var sw = Stopwatch.StartNew();
                    long lastPublish = 0;
                    int half = halfPeriods[0];
                    while (!ctx.ShouldStop && gpuBad == 0 && gpuRc == 0 && pool.Errors == 0)
                    {
                        long ms = sw.ElapsedMilliseconds;
                        half = halfPeriods[(int)(ms / 12000) % halfPeriods.Length]; // 12 s per rhythm, then faster
                        bool next = (ms / half) % 2 == 0;
                        if (next != on) { on = next; pool.On = next; if (next) swings++; }
                        if (ms - lastPublish >= 1000)
                        {
                            lastPublish = ms;
                            ctx.Publish(string.Format(L("Créneaux 0 ↔ 100 % toutes les {0} ms", "0 ↔ 100 % square wave every {0} ms", "Onda cuadrada 0 ↔ 100 % cada {0} ms", "Rechteck 0 ↔ 100 % alle {0} ms", "Onda quadra 0 ↔ 100 % ogni {0} ms", "Onda quadrada 0 ↔ 100 % a cada {0} ms"), half),
                                TransientTiles(ctx, half, swings), ctx.Gpu.PowerW, ctx.Gpu.TempC, PowerLegend);
                        }
                        Thread.Sleep(5);
                    }
                    on = false;
                    pool.On = false;
                    gpuThread.Join(3000);

                    if (pool.Errors > 0) return StressAdvice.CpuFailed(ctx, pool.FirstErrorCpu, pool.FirstErrorWork);
                }
                if (gpuRc != 0) return StressAdvice.GpuDriverReset(ctx, "OpenCL " + gpuRc);
                if (gpuBad > 0) return StressAdvice.GpuComputeFailed(ctx, string.Format(L("{0} valeur(s) fausse(s) pendant un pic", "{0} wrong value(s) during a spike", "{0} valor(es) erróneo(s) durante un pico", "{0} falsche(r) Wert(e) während einer Spitze", "{0} valore/i errato/i durante un picco", "{0} valor(es) errado(s) durante um pico"), gpuBad));
                return StressAdvice.Passed(ctx, string.Format(L(
                    "{0:N0} passages brusques de 0 à 100 % (CPU + GPU), tous les calculs justes. Puissance GPU max : {1}.",
                    "{0:N0} sudden 0 → 100 % swings (CPU + GPU), every result correct. Peak GPU power: {1}.",
                    "{0:N0} saltos bruscos de 0 a 100 % (CPU + GPU), todos los cálculos correctos. Potencia GPU máx.: {1}.",
                    "{0:N0} abrupte Wechsel von 0 auf 100 % (CPU + GPU), alle Ergebnisse korrekt. Max. GPU-Leistung: {1}.",
                    "{0:N0} passaggi bruschi da 0 a 100 % (CPU + GPU), tutti i calcoli corretti. Potenza GPU max: {1}.",
                    "{0:N0} saltos bruscos de 0 a 100 % (CPU + GPU), todos os cálculos corretos. Potência GPU máx.: {1}."),
                    swings, Num(ctx.Gpu.PeakPowerW, "W")) + ctx.CpuTempSummary);
            }
        }

        private static StressTile[] TransientTiles(StageContext ctx, int half, long swings)
        {
            GpuSensor g = ctx.Gpu;
            return new[]
            {
                StressTile.Make(StressText.GpuPower, Num(g.PowerW, "W"), g.Available ? NumPeak(g.PeakPowerW, "W") : L("capteur indisponible", "no sensor", "sin sensor", "kein Sensor", "nessun sensore", "sem sensor")),
                StressTile.Make(L("Rythme", "Rhythm", "Ritmo", "Rhythmus", "Ritmo", "Ritmo"), half > 0 ? half + " ms" : "—", L("par demi-période", "per half-period", "por semiperiodo", "pro Halbperiode", "per semiperiodo", "por semiperíodo")),
                StressTile.Make(L("Pics", "Spikes", "Picos", "Spitzen", "Picchi", "Picos"), swings.ToString("N0"), "CPU + GPU 0 → 100 %"),
                ctx.ErrorTile(0)
            };
        }
    }
}
