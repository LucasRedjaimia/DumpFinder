using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace WinCrashDoctor
{
    /// <summary>
    /// Result channel from an elevated copy of this exe back to the app, without any file in a user-writable folder:
    /// the app creates a named pipe, starts "DumpFinder.exe &lt;argument&gt; &lt;pipe name&gt;" elevated, and only accepts
    /// the process it started (client process id check). The child writes text lines (typically one JSON line).
    /// Same design as FixRunner's elevated batch.
    /// </summary>
    internal static class ElevatedPipe
    {
        private const string NamePrefix = "DumpFinder_pipe_";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

        /// <summary>Parent side. Blocking: call off the UI thread. <paramref name="lines"/> holds what the child sent.</summary>
        public static ElevationOutcome Run(string argument, int timeoutMs, out List<string> lines)
        {
            var received = new List<string>();
            string pipeName = NamePrefix + Guid.NewGuid().ToString("N");
            int childPid = 0;
            ElevationOutcome outcome;

            using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Task reader = Task.Run(() =>
                {
                    try
                    {
                        server.WaitForConnection();
                        uint clientPid;
                        for (int w = 0; w < 50 && Volatile.Read(ref childPid) == 0; w++) Thread.Sleep(100);
                        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out clientPid) || clientPid != (uint)Volatile.Read(ref childPid))
                        {
                            Log.Warn("Elevated pipe: unexpected client " + clientPid + " (expected " + childPid + "), ignored");
                            return;
                        }
                        using (var sr = new StreamReader(server, new UTF8Encoding(false)))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                                if (line.Length > 0) lock (received) received.Add(line);
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                    catch (Exception ex) { Log.Warn(ex, "elevated pipe"); }
                });

                outcome = Elevation.RunSelf(argument + " " + pipeName, timeoutMs, pid => Volatile.Write(ref childPid, pid));

                if (!reader.Wait(outcome == ElevationOutcome.Success ? 5000 : 1000))
                {
                    // The child never connected: release the waiting reader with a dummy client (refused by the PID check).
                    try
                    {
                        using (var unblock = new NamedPipeClientStream(".", pipeName, PipeDirection.Out)) unblock.Connect(500);
                    }
                    catch (Exception) { }
                    try { server.Dispose(); } catch (Exception) { }
                    reader.Wait(2000);
                }
            }
            lock (received) lines = new List<string>(received);
            return outcome;
        }

        /// <summary>Child side: true for a pipe name this app would have created.</summary>
        public static bool IsValidName(string pipeName)
        {
            return !string.IsNullOrEmpty(pipeName) && pipeName.StartsWith(NamePrefix, StringComparison.Ordinal)
                && pipeName.IndexOfAny(new[] { '\\', '/', ':' }) < 0 && pipeName.Length < 100;
        }

        /// <summary>Child side: connects to the parent's pipe and sends the lines.</summary>
        public static bool Send(string pipeName, IEnumerable<string> lines)
        {
            if (!IsValidName(pipeName)) return false;
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(15000);
                    using (var sw = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        foreach (string l in lines) sw.WriteLine(l);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "elevated pipe send");
                return false;
            }
        }
    }
}
