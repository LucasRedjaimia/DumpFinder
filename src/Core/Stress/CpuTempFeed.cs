using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace WinCrashDoctor.Stress
{
    public enum CpuTempState { Off, Starting, Live, NoPawnIo, Declined, Unavailable }

    /// <summary>
    /// CPU temperature during a stress run. Elevated app: read in-process. Otherwise one UAC prompt starts a small
    /// elevated helper that streams readings back through a randomly named pipe and exits with the test.
    /// </summary>
    public sealed class CpuTempFeed : IDisposable
    {
        private volatile CpuTempState _state = CpuTempState.Off;
        private CpuTempReader _inProc;
        private Timer _timer;
        private NamedPipeServerStream _pipe;
        private volatile bool _closed;
        private readonly object _lock = new object();

        public CpuTempState State { get { return _state; } }
        public double TempC = double.NaN, PowerW = double.NaN, PeakTempC = double.NaN;

        public static CpuTempFeed Start()
        {
            var f = new CpuTempFeed();
            if (!CpuTempReader.PawnIoInstalled) { f._state = CpuTempState.NoPawnIo; return f; }

            if (Elevation.IsAdministrator())
            {
                f._inProc = CpuTempReader.TryOpen();
                if (f._inProc == null) { f._state = CpuTempState.Unavailable; return f; }
                f._state = CpuTempState.Live;
                f._timer = new Timer(_ => f.PollInProc(), null, 0, 1000);
                return f;
            }

            string name = CpuTempReader.PipePrefix + Guid.NewGuid().ToString("N");
            f._pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            f._state = CpuTempState.Starting;
            if (!Elevation.Launch(Elevation.ExePath, "--sensor-pipe " + name + " " + System.Diagnostics.Process.GetCurrentProcess().Id))
            {
                f._state = CpuTempState.Declined;
                f._pipe.Dispose();
                f._pipe = null;
                return f;
            }
            new Thread(f.ReadLoop) { IsBackground = true, Name = "df-cpu-temp" }.Start();
            return f;
        }

        private void PollInProc()
        {
            double t, w;
            lock (_lock)
            {
                if (_inProc == null || _closed) return;
                if (_inProc.Read(out t, out w)) Set(t, w);
            }
        }

        private void ReadLoop()
        {
            try
            {
                IAsyncResult ar = _pipe.BeginWaitForConnection(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(30000)) { _state = CpuTempState.Unavailable; return; }
                _pipe.EndWaitForConnection(ar);
                using (var sr = new StreamReader(_pipe))
                {
                    string line;
                    while (!_closed && (line = sr.ReadLine()) != null)
                    {
                        if (line == "ERR") { _state = CpuTempState.Unavailable; return; }
                        string[] p = line.Split(';');
                        double t, w;
                        if (p.Length < 2 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) continue;
                        double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w);
                        Set(t, w);
                    }
                }
                if (!_closed) _state = CpuTempState.Unavailable; // helper ended early
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { if (!_closed) { Log.Warn(ex); _state = CpuTempState.Unavailable; } }
        }

        private void Set(double t, double w)
        {
            if (double.IsNaN(t) || t <= 0) return;
            TempC = t;
            PowerW = w > 0 ? w : double.NaN;
            if (double.IsNaN(PeakTempC) || t > PeakTempC) PeakTempC = t;
            _state = CpuTempState.Live;
        }

        public void ResetPeak() { PeakTempC = double.IsNaN(TempC) ? double.NaN : TempC; }

        public void Dispose()
        {
            _closed = true;
            try { if (_timer != null) _timer.Dispose(); } catch { }
            lock (_lock) { if (_inProc != null) { _inProc.Dispose(); _inProc = null; } }
            try { if (_pipe != null) _pipe.Dispose(); } catch { } // the helper sees a broken pipe and exits
        }
    }
}
