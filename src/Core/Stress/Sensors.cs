using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// Effective CPU clock without a kernel driver: "% Processor Performance" × base clock
    /// (the same source as Task Manager). CPU temperature is not readable without a driver,
    /// so it is never shown rather than guessed.
    /// </summary>
    internal sealed class CpuClockSensor : IDisposable
    {
        private readonly double _baseMhz;
        private PerformanceCounter _total;
        private PerformanceCounter _one;
        private int _oneLogical = -1;

        public CpuClockSensor()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor"))
                    foreach (ManagementObject mo in s.Get()) { _baseMhz = Convert.ToDouble(mo["MaxClockSpeed"]); break; }
            }
            catch (Exception ex) { Log.Warn(ex); }
            try
            {
                _total = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                _total.NextValue();
            }
            catch (Exception ex) { Log.Warn(ex); _total = null; }
        }

        public bool Available { get { return _total != null && _baseMhz > 0; } }

        /// <summary>Average effective clock of all cores, 0 when unknown.</summary>
        public int TotalMhz()
        {
            try { return Available ? (int)(_total.NextValue() / 100.0 * _baseMhz) : 0; }
            catch { return 0; }
        }

        /// <summary>Effective clock of one logical processor (processor group 0).</summary>
        public int LogicalMhz(int logical)
        {
            if (_baseMhz <= 0) return 0;
            try
            {
                if (_one == null || _oneLogical != logical)
                {
                    if (_one != null) _one.Dispose();
                    _one = new PerformanceCounter("Processor Information", "% Processor Performance", "0," + logical, true);
                    _oneLogical = logical;
                    _one.NextValue();
                    return 0;
                }
                return (int)(_one.NextValue() / 100.0 * _baseMhz);
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            try { if (_total != null) _total.Dispose(); if (_one != null) _one.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// GPU sensors: nvidia-smi in streaming mode (one process for the whole run), or AMD ADL.
    /// When neither exists the values stay NaN and the UI shows "n/a" — never estimated numbers.
    /// </summary>
    internal sealed class GpuSensor : IDisposable
    {
        public string Source { get; private set; }
        public double ClockMhz = double.NaN, PowerW = double.NaN, TempC = double.NaN, UtilPct = double.NaN;
        public double PeakPowerW = double.NaN, PeakTempC = double.NaN;

        private Process _smi;
        private Timer _adlTimer;
        private int _bestIndex = -1;
        private double _bestMemTotal = -1;

        public bool Available { get { return Source != null; } }

        public void Start()
        {
            if (StartNvidiaSmi()) return;
            if (AmdAdl.Exists)
            {
                Source = "AMD ADL";
                _adlTimer = new Timer(_ => PollAdl(), null, 0, 1000);
            }
        }

        private bool StartNvidiaSmi()
        {
            try
            {
                string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                if (!File.Exists(exe)) exe = "nvidia-smi";
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--query-gpu=index,memory.total,clocks.gr,power.draw,temperature.gpu,utilization.gpu --format=csv,noheader,nounits -lms 1000",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => ParseSmi(e.Data);
                if (!p.Start()) return false;
                p.BeginOutputReadLine();
                _smi = p;
                Source = "nvidia-smi";
                return true;
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private void ParseSmi(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string[] f = line.Split(',');
            if (f.Length < 6) return;
            int idx;
            if (!int.TryParse(f[0].Trim(), out idx)) return;
            double mem = Num(f[1]);
            if (double.IsNaN(mem)) mem = 0;
            // Several GPUs print one line each per tick: follow the one with the most memory (the discrete card).
            if (_bestIndex != idx)
            {
                if (!(mem > _bestMemTotal)) return;
                _bestIndex = idx;
                _bestMemTotal = mem;
            }
            ClockMhz = Num(f[2]);
            PowerW = Num(f[3]);
            TempC = Num(f[4]);
            UtilPct = Num(f[5]);
            TrackPeaks();
        }

        private void PollAdl()
        {
            int clk; double w, t, act;
            if (AmdAdl.TryRead(out clk, out w, out t, out act))
            {
                ClockMhz = clk > 0 ? clk : double.NaN;
                PowerW = w > 0 ? w : double.NaN;
                TempC = t > 0 ? t : double.NaN;
                UtilPct = act >= 0 ? act : double.NaN;
                TrackPeaks();
            }
        }

        private void TrackPeaks()
        {
            if (!double.IsNaN(PowerW) && (double.IsNaN(PeakPowerW) || PowerW > PeakPowerW)) PeakPowerW = PowerW;
            if (!double.IsNaN(TempC) && (double.IsNaN(PeakTempC) || TempC > PeakTempC)) PeakTempC = TempC;
        }

        public void ResetPeaks() { PeakPowerW = double.NaN; PeakTempC = double.NaN; }

        private static double Num(string s)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        public void Dispose()
        {
            try { if (_adlTimer != null) _adlTimer.Dispose(); } catch { }
            try { if (_smi != null && !_smi.HasExited) _smi.Kill(); } catch { }
            try { if (_smi != null) _smi.Dispose(); } catch { }
            _smi = null;
        }
    }

    /// <summary>AMD Display Library (Radeon RX 6000/7000/9000): clock, power, temperature, activity.</summary>
    internal static class AmdAdl
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ADL_MAIN_MALLOC_CALLBACK(int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLSingleSensorData { public int supported; public int value; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLPMLogDataOutput
        {
            public int size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ADLSingleSensorData[] sensors;
        }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Create(ADL_MAIN_MALLOC_CALLBACK callback, int enumConnectedAdapters, out IntPtr context);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapterIndex, out ADLPMLogDataOutput pmData);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Destroy(IntPtr context);

        private static readonly ADL_MAIN_MALLOC_CALLBACK Malloc = size => Marshal.AllocHGlobal(size);

        public static bool Exists
        {
            get
            {
                try { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "atiadlxx.dll")); }
                catch { return false; }
            }
        }

        public static bool TryRead(out int clkMhz, out double watts, out double tempC, out double activity)
        {
            clkMhz = 0; watts = 0; tempC = 0; activity = -1;
            IntPtr ctx = IntPtr.Zero;
            try
            {
                if (ADL2_Main_Control_Create(Malloc, 1, out ctx) != 0 || ctx == IntPtr.Zero) return false;
                int n;
                if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out n) != 0 || n <= 0) return false;
                int bestPower = -1;
                for (int i = 0; i < n; i++)
                {
                    ADLPMLogDataOutput pm;
                    if (ADL2_New_QueryPMLogData_Get(ctx, i, out pm) != 0 || pm.sensors == null || pm.sensors.Length < 30) continue;
                    // ADL_PMLOG_CLK_GFXCLK = 1, TEMPERATURE_EDGE = 8, INFO_ACTIVITY_GFX = 19, ASIC_POWER = 23
                    int c = pm.sensors[1].supported != 0 ? pm.sensors[1].value : 0;
                    int t = pm.sensors[8].supported != 0 ? pm.sensors[8].value : 0;
                    int p = pm.sensors[23].supported != 0 ? pm.sensors[23].value : 0;
                    int a = pm.sensors[19].supported != 0 ? pm.sensors[19].value : -1;
                    if ((c > 0 || p > 0 || t > 0) && p > bestPower)
                    {
                        bestPower = p; clkMhz = c; watts = p; tempC = t; activity = a;
                    }
                }
                return bestPower >= 0;
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
            finally
            {
                if (ctx != IntPtr.Zero) { try { ADL2_Main_Control_Destroy(ctx); } catch { } }
            }
        }
    }
}
