using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>Minimal OpenCL wrapper: one GPU (the one with the most memory), one queue, three kernels.</summary>
    internal sealed class OpenClDevice : IDisposable
    {
        [DllImport("OpenCL.dll")] private static extern int clGetPlatformIDs(uint n, IntPtr[] p, out uint np);
        [DllImport("OpenCL.dll")] private static extern int clGetPlatformInfo(IntPtr p, uint param, UIntPtr size, byte[] val, out UIntPtr ret);
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
        /// <summary>The GPU shares system RAM (integrated GPU): its "global memory" is the PC's own memory.</summary>
        public bool UnifiedMemory { get; private set; }

        private volatile bool _abandoned;

        /// <summary>
        /// A worker thread may still be inside an OpenCL call on this device: from now on Dispose leaks the objects
        /// instead of releasing them under the driver's feet (a crash risk); the process exit reclaims them.
        /// </summary>
        public void Abandon() { _abandoned = true; }
        public bool Abandoned { get { return _abandoned; } }

        private sealed class Candidate
        {
            public IntPtr Dev;
            public long Mem;
            public bool Unified, MsLayer;
            public int VendorRank;
        }

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
            // Picking simply the largest GLOBAL_MEM_SIZE goes wrong on hybrid laptops: an integrated GPU (or the
            // Microsoft "OpenCLOn12" layer) reports shared system RAM, so it can "win" over the discrete card.
            var cands = new List<Candidate>();
            foreach (IntPtr p in plats)
            {
                if (p == IntPtr.Zero) continue;
                uint nd;
                if (clGetDeviceIDs(p, 4 /* GPU */, 0, null, out nd) != 0 || nd == 0) continue;
                var devs = new IntPtr[nd];
                if (clGetDeviceIDs(p, 4, nd, devs, out nd) != 0) continue;
                string plat = PlatformString(p, 0x0902 /* NAME */) + " " + PlatformString(p, 0x0903 /* VENDOR */);
                bool msLayer = Has(plat, "OpenCLOn12") || Has(plat, "Microsoft");
                foreach (IntPtr d in devs)
                {
                    if (d == IntPtr.Zero) continue;
                    uint vendorId = (uint)InfoU64(d, 0x1001 /* VENDOR_ID */, 4);
                    string vendor = InfoString(d, 0x102C /* VENDOR */);
                    int rank = 1;
                    if (vendorId == 0x10DE || vendorId == 0x1002 || Has(vendor, "NVIDIA") || Has(vendor, "Advanced Micro Devices") || Has(vendor, "AMD")) rank = 2;
                    else if (vendorId == 0x8086 || Has(vendor, "Intel")) rank = 0;
                    cands.Add(new Candidate
                    {
                        Dev = d,
                        Mem = (long)InfoU64(d, 0x101F /* GLOBAL_MEM_SIZE */),
                        Unified = InfoU64(d, 0x1035 /* HOST_UNIFIED_MEMORY */, 4) != 0,
                        MsLayer = msLayer,
                        VendorRank = rank
                    });
                }
            }
            if (cands.Count == 0) return false;
            if (cands.Any(c => !c.MsLayer)) cands.RemoveAll(c => c.MsLayer);   // a native driver exists: skip the D3D12 translation layer
            if (cands.Any(c => !c.Unified)) cands.RemoveAll(c => c.Unified);   // a discrete card exists: skip shared-memory GPUs
            Candidate best = cands.OrderByDescending(c => c.VendorRank).ThenByDescending(c => c.Mem).First(); // NVIDIA/AMD before Intel
            _dev = best.Dev;
            UnifiedMemory = best.Unified;

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

        private static string PlatformString(IntPtr p, uint param)
        {
            var b = new byte[512];
            UIntPtr r;
            if (clGetPlatformInfo(p, param, (UIntPtr)512u, b, out r) != 0) return "";
            int n = Math.Max(0, Math.Min(511, (int)r.ToUInt32() - 1));
            return Encoding.ASCII.GetString(b, 0, n).Trim();
        }

        private static bool Has(string s, string part) { return s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }

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
            if (_abandoned)
            {
                Log.Warn("OpenCL device still in use by a worker thread: its objects are leaked, not released.");
                Ready = false;
                return;
            }
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

        private static OpenClDevice Device(StageContext ctx, out StressStageResult skip)
        {
            skip = null;
            OpenClDevice d = ctx.GpuDevice != null ? ctx.GpuDevice() : null;
            if (d != null && d.Abandoned)
                skip = StressAdvice.GpuUnavailable(ctx, Localizer.S("gpu_stress.the_graphics_card_stopped_responding_in"));
            else if (d == null || !d.Ready)
                skip = StressAdvice.GpuUnavailable(ctx, Localizer.S("gpu_stress.no_usable_opencl_graphics_card_driver"));
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
                StressTile.Make(StressText.GpuPower, Num(g.PowerW, "W"), g.Available ? NumPeak(g.PeakPowerW, "W") : Localizer.S("gpu_stress.no_sensor")),
                StressTile.Make(StressText.GpuTemp, Num(g.TempC, "°C"), g.Available ? NumPeak(g.PeakTempC, "°C") : Localizer.S("gpu_stress.no_sensor")),
                ctx.ErrorTile(errors)
            };
        }

        private static string PowerLegend
        {
            get
            {
                return Localizer.S("gpu_stress.violet_gpu_power_w_amber_gpu");
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
                if (job.Error == "inconsistent") return StressAdvice.GpuComputeFailed(ctx, Localizer.S("gpu_stress.already_during_calibration"));
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
                        return StressAdvice.GpuComputeFailed(ctx, string.Format(Localizer.S("gpu_stress.wrong_value_s_out_of_1"), bad, job.N));
                    if (tick.ElapsedMilliseconds >= 1000)
                    {
                        tick.Restart();
                        string sub = string.Format(Localizer.S("gpu_stress.0_n0_verified_launches"), launches);
                        ctx.Publish(d.Name + " · " + string.Format(Localizer.S("gpu_stress.0_f0_ms_per_launch"), job.LaunchMs),
                            SensorTiles(ctx, sub, 0), ctx.Gpu.PowerW, ctx.Gpu.TempC, PowerLegend);
                    }
                }
                return StressAdvice.Passed(ctx, string.Format(Localizer.S("gpu_stress.0_n0_verified_launches_on_no"),
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
            if (d.UnifiedMemory)
            {
                // Integrated GPU: its "video memory" is system RAM. Never take more than half of what is free right now.
                ulong avail = Native.AvailablePhys();
                long cap = avail > 0 ? (long)(avail / 2) : (1L << 30);
                if (cap < target)
                {
                    ctx.Log(string.Format(Localizer.S("gpu_stress.shared_memory_graphics_test_limited_to"), StressText.Gb(cap)));
                    target = cap;
                }
            }
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
                    if (Fill(d, b, 0u, ChunkSeed(0x13579BDFu, chunks.Count), 0, chunkWords) != 0) { OpenClDevice.Free(b); break; }
                    chunks.Add(b);
                    if (chunks.Count % 4 == 0)
                        ctx.Publish(Localizer.S("gpu_stress.allocating_video_memory") + StressText.Gb(chunks.Count * chunkBytes),
                            VramTiles(ctx, chunks.Count * chunkBytes, d.GlobalMem, 0, 0), double.NaN, ctx.Gpu.TempC);
                }
                if (chunks.Count == 0)
                    return StressAdvice.GpuUnavailable(ctx, Localizer.S("gpu_stress.could_not_allocate_video_memory"));

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
                    // Each chunk gets its own hashed seed (a 32-bit "c * chunkWords" base wrapped every 16 GB, so two
                    // chunks could hold identical data and aliasing between them would go unnoticed).
                    for (int c = 0; c < chunks.Count && rc == 0; c++)
                        rc = Fill(d, chunks[c], 0u, ChunkSeed(seed, c), inv, chunkWords, false);
                    for (int c = 0; c < chunks.Count && rc == 0; c++)
                    {
                        uint cs = ChunkSeed(seed, c);
                        rc = Check(d, chunks[c], 0u, cs, inv, mask, 4099u, errBuf, (uint)c, chunkWords);
                        if (rc == 0) rc = Check(d, chunks[c], 0u, cs, inv, mask, 65537u, errBuf, (uint)c, chunkWords);
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
                        ctx.Publish(string.Format(Localizer.S("gpu_stress.pass_0_n0_pattern"), passes, inv == 0 ? "A" : "Ā"),
                            VramTiles(ctx, tested, d.GlobalMem, passes, gbps), gbps, ctx.Gpu.TempC,
                            Localizer.S("gpu_stress.violet_video_memory_throughput_gb_s"));
                    }
                }
                return StressAdvice.Passed(ctx, string.Format(Localizer.S("gpu_stress.of_video_memory_written_and_read"),
                    StressText.Gb(tested), passes));
            }
            finally
            {
                foreach (IntPtr b in chunks) OpenClDevice.Free(b);
                OpenClDevice.Free(errBuf);
            }
        }

        /// <summary>Per-chunk pattern seed: the pass seed mixed with a hash of the chunk index.</summary>
        private static uint ChunkSeed(uint seed, int chunk)
        {
            ulong h = CpuWork.Mix(((ulong)(uint)chunk << 32) ^ 0xD1B54A32D192ED03UL);
            return seed ^ (uint)h ^ (uint)(h >> 32);
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
                StressTile.Make(StressText.Tested, StressText.Gb(tested), Localizer.S("gpu_stress.of") + StressText.Gb(total)),
                StressTile.Make(Localizer.S("gpu_stress.passes"), passes.ToString("N0"), Localizer.S("gpu_stress.write_2_read_backs")),
                StressTile.Make(StressText.Throughput, gbps > 0 ? gbps.ToString("N0") + " " + Localizer.S("gpu_stress.gb_s") : "—", StressText.GpuTemp + " " + Num(ctx.Gpu.TempC, "°C")),
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
            BurnJob job = BurnJob.Calibrate(d, 30);
            bool workerHung = false;
            try
            {
                if (job.Error == "inconsistent") return StressAdvice.GpuComputeFailed(ctx, Localizer.S("gpu_stress.already_during_calibration"));
                if (job.Error != null) return StressAdvice.GpuDriverReset(ctx, job.Error);

                int[] halfPeriods = { 2000, 1000, 500, 250, 100 };
                bool on = false;
                int gpuBad = 0, gpuRc = 0, quit = 0;
                long swings = 0;
                using (var pool = new CpuLoadPool(Environment.ProcessorCount, calib) { On = false })
                {
                    var gpuThread = new Thread(() =>
                    {
                        try
                        {
                            // "quit" also ends it when the stage stops for another reason (e.g. a CPU error).
                            while (Volatile.Read(ref quit) == 0 && !ctx.ShouldStop && gpuBad == 0 && gpuRc == 0)
                            {
                                if (!on) { Thread.Sleep(2); continue; }
                                int bad;
                                int rc = job.Run(out bad);
                                if (rc != 0) gpuRc = rc;
                                if (bad > 0) gpuBad = bad;
                            }
                        }
                        catch (Exception ex) { Log.Warn(ex); gpuRc = -1; } // never let a worker exception kill the app
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
                            ctx.Publish(string.Format(Localizer.S("gpu_stress.0_100_square_wave_every_ms"), half),
                                TransientTiles(ctx, half, swings), ctx.Gpu.PowerW, ctx.Gpu.TempC, PowerLegend);
                        }
                        Thread.Sleep(5);
                    }
                    on = false;
                    pool.On = false;
                    Volatile.Write(ref quit, 1);
                    // One launch is ~30 ms: a worker still busy after 5 s is stuck inside the driver. Its job buffer
                    // and the device must then NOT be released while OpenCL may still use them: leak them instead.
                    if (!gpuThread.Join(5000))
                    {
                        workerHung = true;
                        d.Abandon();
                        Log.Warn("GPU transient worker still inside OpenCL after 5 s: its OpenCL objects are leaked instead of released.");
                    }

                    if (pool.Errors > 0) return StressAdvice.CpuFailed(ctx, pool.FirstErrorCpu, pool.FirstErrorWork);
                }
                if (workerHung)
                    return StressAdvice.GpuDriverReset(ctx, Localizer.S("gpu_stress.gpu_computation_stopped_responding"));
                if (gpuRc != 0) return StressAdvice.GpuDriverReset(ctx, "OpenCL " + gpuRc);
                if (gpuBad > 0) return StressAdvice.GpuComputeFailed(ctx, string.Format(Localizer.S("gpu_stress.wrong_value_s_during_a_spike"), gpuBad));
                return StressAdvice.Passed(ctx, string.Format(Localizer.S("gpu_stress.0_n0_sudden_0_100_swings"),
                    swings, Num(ctx.Gpu.PeakPowerW, "W")) + ctx.CpuTempSummary);
            }
            finally
            {
                if (!workerHung) job.Dispose();
            }
        }

        private static StressTile[] TransientTiles(StageContext ctx, int half, long swings)
        {
            GpuSensor g = ctx.Gpu;
            return new[]
            {
                StressTile.Make(StressText.GpuPower, Num(g.PowerW, "W"), g.Available ? NumPeak(g.PeakPowerW, "W") : Localizer.S("gpu_stress.no_sensor")),
                StressTile.Make(Localizer.S("gpu_stress.rhythm"), half > 0 ? half + " ms" : "—", Localizer.S("gpu_stress.per_half_period")),
                StressTile.Make(Localizer.S("gpu_stress.spikes"), swings.ToString("N0"), "CPU + GPU 0 → 100 %"),
                ctx.ErrorTile(0)
            };
        }
    }
}
