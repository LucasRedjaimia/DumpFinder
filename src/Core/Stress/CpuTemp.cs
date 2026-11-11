using System;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace WinCrashDoctor.Stress
{
    /// <summary>
    /// CPU temperature and package power through LibreHardwareMonitorLib, which talks to the signed PawnIO
    /// driver (not the vulnerable WinRing0). Needs PawnIO installed and administrator rights.
    /// </summary>
    internal sealed class CpuTempReader : IDisposable
    {
        private Computer _computer;
        private IHardware _cpu;
        private readonly object _lock = new object();

        public string SensorName { get; private set; }

        public static bool PawnIoInstalled
        {
            get
            {
                try
                {
                    using (var bk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                    using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
                        return k != null;
                }
                catch { return false; }
            }
        }

        /// <summary>Opens the CPU sensors; null when no temperature can be read (no PawnIO, no rights, unsupported CPU).</summary>
        public static CpuTempReader TryOpen()
        {
            var r = new CpuTempReader();
            try
            {
                r._computer = new Computer { IsCpuEnabled = true };
                r._computer.Open();
                r._cpu = r._computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                double t, w;
                if (r._cpu != null && r.Read(out t, out w) && t > 0) return r;
            }
            catch (Exception ex) { Log.Warn(ex); }
            r.Dispose();
            return null;
        }

        /// <summary>Tctl/Tdie on AMD, CPU Package on Intel, otherwise the hottest CPU sensor.</summary>
        public bool Read(out double tempC, out double packageW)
        {
            tempC = double.NaN;
            packageW = double.NaN;
            lock (_lock)
            {
                if (_cpu == null) return false;
                try
                {
                    _cpu.Update();
                    var temps = _cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0).ToList();
                    ISensor pick = temps.FirstOrDefault(s => s.Name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 || s.Name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? temps.FirstOrDefault(s => s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? temps.OrderByDescending(s => s.Value.Value).FirstOrDefault();
                    if (pick != null) { tempC = pick.Value.Value; SensorName = pick.Name; }
                    ISensor pw = _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value.HasValue && s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (pw != null) packageW = pw.Value.Value;
                    return pick != null;
                }
                catch (Exception ex) { Log.Warn(ex); return false; }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { if (_computer != null) _computer.Close(); } catch { }
                _computer = null;
                _cpu = null;
            }
        }

        // ==================== Elevated helper (--sensor-pipe) ====================
        public const string PipePrefix = "DumpFinder.Sensors.";

        /// <summary>
        /// Runs elevated: reads the CPU sensors and writes "temp;power" once per second into the parent's pipe,
        /// until the parent exits or closes the pipe. Only connects to a pipe name this app generates.
        /// </summary>
        public static int ServePipe(string pipeName, string parentPid)
        {
            int pid;
            if (pipeName == null || !System.Text.RegularExpressions.Regex.IsMatch(pipeName, "^" + System.Text.RegularExpressions.Regex.Escape(PipePrefix) + "[0-9a-f]{32}$")
                || !int.TryParse(parentPid, out pid))
                return 2;
            try
            {
                System.Diagnostics.Process parent = System.Diagnostics.Process.GetProcessById(pid);
                using (var client = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.Out))
                {
                    client.Connect(15000);
                    using (var w = new System.IO.StreamWriter(client) { AutoFlush = true })
                    using (CpuTempReader r = TryOpen())
                    {
                        if (r == null) { w.WriteLine("ERR"); return 1; }
                        var until = DateTime.Now.AddHours(14); // never outlives the longest test
                        while (!parent.HasExited && DateTime.Now < until)
                        {
                            double t, p;
                            r.Read(out t, out p);
                            w.WriteLine(t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ";" + p.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                }
            }
            catch (System.IO.IOException) { } // pipe closed by the app: normal end
            catch (Exception ex) { Log.Warn(ex); return 1; }
            return 0;
        }

        /// <summary>--cpu-temp-probe &lt;out&gt;: lists every CPU sensor LibreHardwareMonitor sees (diagnostics).</summary>
        public static int Probe(string outPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Admin: " + Elevation.IsAdministrator() + " · PawnIO: " + PawnIoInstalled);
            try
            {
                var c = new Computer { IsCpuEnabled = true };
                c.Open();
                foreach (IHardware h in c.Hardware)
                {
                    h.Update();
                    System.Threading.Thread.Sleep(500);
                    h.Update();
                    sb.AppendLine(h.HardwareType + " · " + h.Name);
                    foreach (ISensor s in h.Sensors.OrderBy(s => s.SensorType))
                        sb.AppendLine("   " + s.SensorType + " · " + s.Name + " = " + (s.Value.HasValue ? s.Value.Value.ToString("0.0") : "null"));
                }
                c.Close();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex); }
            using (var r = TryOpen())
            {
                double t, w;
                if (r != null && r.Read(out t, out w)) sb.AppendLine("Lecture retenue : " + r.SensorName + " = " + t.ToString("0.0") + " °C, " + (double.IsNaN(w) ? "puissance n/d" : w.ToString("0") + " W"));
                else sb.AppendLine("Lecture retenue : aucune");
            }
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return 0;
        }
    }
}
