using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace WinCrashDoctor
{
    public class FixRunResult
    {
        public string Op { get; set; }
        public bool Ok { get; set; }
        public string MessageFr { get; set; }
        public string MessageEn { get; set; }
        public List<ChangeRecord> Changes { get; set; }

        public string Message { get { return Localizer.T(MessageFr, MessageEn); } }
    }

    /// <summary>
    /// Runs fixes as a batch with change recording.
    /// - Ops that need no rights (HKCU tweaks, your startup apps, your temp folder) always run in this process,
    ///   so they hit the signed-in user's profile even when UAC is answered with another administrator account.
    /// - Ops that need rights run in ONE elevated child (one UAC prompt). The op list travels on the command line;
    ///   the child streams each result back through a named pipe as soon as that op finishes, so a crash or a
    ///   timeout halfway never loses the undo data of the ops already applied. The pipe only accepts the child
    ///   we started (client process id check), and nothing is written to a user-writable file by the admin side.
    /// </summary>
    public static class FixRunner
    {
        /// <summary>Ops whose effect cannot be undone (files deleted, consoles launched).</summary>
        private static readonly HashSet<string> Irreversible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "shader_cache_apply", "sfc_repair_apply", "create_restore_point", "restore_snapshot"
        };

        /// <summary>The elevated batch may run long (a restore point alone can take minutes).</summary>
        private const int BatchTimeoutMs = 15 * 60 * 1000;

        public static bool IsReversible(string op, List<ChangeRecord> changes)
        {
            return !Irreversible.Contains(op) && changes != null && changes.Count > 0;
        }

        /// <summary>True for ops that cannot be undone (used to warn before applying them).</summary>
        public static bool IsIrreversibleOp(string op)
        {
            return op != null && Irreversible.Contains(op);
        }

        /// <summary>Blocking. Call off the UI thread. Results come back in the order of <paramref name="ops"/>.</summary>
        public static List<FixRunResult> Run(IList<string> ops)
        {
            if (ops == null || ops.Count == 0) return new List<FixRunResult>();
            bool admin = Elevation.IsAdministrator();
            var results = new FixRunResult[ops.Count];

            var adminIdx = new List<int>();
            for (int i = 0; i < ops.Count; i++)
            {
                if (!admin && DiagnosticsEngine.OpNeedsAdmin(ops[i])) adminIdx.Add(i);
                else results[i] = RunRecorded(ops[i]);
            }

            if (adminIdx.Count > 0)
            {
                ElevationOutcome outcome;
                List<FixRunResult> elevated = RunElevatedBatch(adminIdx.Select(i => ops[i]).ToList(), out outcome);
                for (int k = 0; k < adminIdx.Count; k++)
                {
                    int i = adminIdx[k];
                    FixRunResult r = k < elevated.Count ? elevated[k] : null;
                    results[i] = r != null && string.Equals(r.Op, ops[i], StringComparison.Ordinal) ? r : Failed(ops[i], outcome, elevated.Count > 0);
                }
            }
            return results.ToList();
        }

        /// <summary>Executed inside this process (or the elevated child) with change recording.</summary>
        public static FixRunResult RunRecorded(string op)
        {
            ChangeRecorder.Begin();
            string fr, en;
            bool ok;
            try { ok = DiagnosticsEngine.ExecuteFixOperationInProc(op, out fr, out en); }
            catch (Exception ex) { Log.Error(ex, op); ok = false; fr = en = ex.Message; }
            return new FixRunResult { Op = op, Ok = ok, MessageFr = fr, MessageEn = en, Changes = ChangeRecorder.End() };
        }

        // ==================== Elevated batch over a named pipe ====================

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

        private static List<FixRunResult> RunElevatedBatch(List<string> ops, out ElevationOutcome outcome)
        {
            var received = new List<FixRunResult>();
            string pipeName = "DumpFinder_fix_" + Guid.NewGuid().ToString("N");
            int childPid = 0;

            using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Task reader = Task.Run(() =>
                {
                    try
                    {
                        server.WaitForConnection();
                        // Only the elevated child we launched may report results (another process of this user could
                        // otherwise connect first and inject forged change records into the undo journal).
                        uint clientPid;
                        for (int w = 0; w < 50 && Volatile.Read(ref childPid) == 0; w++) Thread.Sleep(100);
                        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out clientPid) || clientPid != (uint)Volatile.Read(ref childPid))
                        {
                            Log.Warn("Fix batch pipe: unexpected client " + clientPid + " (expected " + childPid + "), ignored");
                            return;
                        }
                        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        using (var sr = new StreamReader(server, new UTF8Encoding(false)))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (line.Length == 0) continue;
                                FixRunResult r = ser.Deserialize<FixRunResult>(line);
                                if (r != null) lock (received) received.Add(r);
                            }
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                    catch (Exception ex) { Log.Warn(ex, "fix batch pipe"); }
                });

                outcome = Elevation.RunSelf("--elevated-batch " + ChangeRecorder.ToArg(ops) + " " + pipeName, BatchTimeoutMs,
                                            pid => Volatile.Write(ref childPid, pid));

                // The child has exited (or timed out): let the reader drain what was sent, then stop listening.
                if (!reader.Wait(outcome == ElevationOutcome.Success ? 5000 : 1000))
                {
                    // Child never connected (UAC refused, crash at start): release the waiting reader with a dummy
                    // client (it fails the process id check and is ignored), then close the pipe.
                    try
                    {
                        using (var unblock = new NamedPipeClientStream(".", pipeName, PipeDirection.Out)) unblock.Connect(500);
                    }
                    catch (Exception) { }
                    try { server.Dispose(); } catch (Exception) { }
                    reader.Wait(2000);
                }
            }
            lock (received) return received.ToList();
        }

        /// <summary>Elevated child entry point: --elevated-batch &lt;base64 ops&gt; &lt;pipe name&gt;.</summary>
        public static int ElevatedBatchMain(string opsArg, string pipeName)
        {
            try
            {
                var ops = ChangeRecorder.FromArg<List<string>>(opsArg) ?? new List<string>();
                if (string.IsNullOrEmpty(pipeName) || pipeName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || !pipeName.StartsWith("DumpFinder_fix_", StringComparison.Ordinal))
                    return 2;

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(15000);
                    using (var sw = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        foreach (string op in ops)
                        {
                            // Only admin ops belong here: user-profile ops run unelevated in the caller.
                            FixRunResult r = DiagnosticsEngine.OpNeedsAdmin(op)
                                ? RunRecorded(op)
                                : new FixRunResult { Op = op, Ok = false, Changes = new List<ChangeRecord>(), MessageFr = Localizer.Ref("engine.fix_runner.operation_refused_in_administrator_mode"), MessageEn = Localizer.Ref("engine.fix_runner.operation_refused_in_administrator_mode") };
                            sw.WriteLine(ser.Serialize(r)); // one result per line, sent as soon as the op is done
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "elevated batch");
                return 1;
            }
        }

        // ==================== Undo ====================

        /// <summary>
        /// Undo recorded changes. HKLM / power plan records go to one elevated child (one UAC prompt); HKCU and
        /// settings-file records are always undone here, in the signed-in user's profile. Blocking.
        /// </summary>
        public static bool Revert(List<ChangeRecord> changes, out string error)
        {
            error = null;
            if (changes == null || changes.Count == 0) return true;
            if (!ChangeRecorder.RequiresAdmin(changes) || Elevation.IsAdministrator())
            {
                ChangeRecorder.Revert(changes, out error);
                return error == null;
            }

            var adminPart = changes.Where(ChangeRecorder.IsAdminRecord).ToList();
            var userPart = changes.Where(c => !ChangeRecorder.IsAdminRecord(c)).ToList();

            ElevationOutcome outcome = Elevation.RunSelf("--elevated-revert " + ChangeRecorder.ToArg(adminPart), 60000);
            if (outcome != ElevationOutcome.Success)
                error = outcome == ElevationOutcome.Cancelled ? "UAC" : outcome.ToString();

            // The user part does not depend on the UAC answer: undo it anyway (it needs no rights).
            string userError = null;
            if (userPart.Count > 0) ChangeRecorder.Revert(userPart, out userError);
            if (error == null) error = userError;
            return error == null;
        }

        /// <summary>Elevated child entry point: --elevated-revert &lt;base64 changes&gt;. Admin records only.</summary>
        public static int ElevatedRevertMain(string changesArg)
        {
            try
            {
                var changes = ChangeRecorder.FromArg<List<ChangeRecord>>(changesArg);
                string error;
                ChangeRecorder.Revert(changes, true, out error);
                if (error != null) Log.Warn("Revert: " + error);
                return error == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "elevated revert");
                return 1;
            }
        }

        private static FixRunResult Failed(string op, ElevationOutcome outcome, bool partial)
        {
            string fr, en;
            if (outcome == ElevationOutcome.Cancelled)
            {
                fr = Localizer.Ref("engine.fix_runner.operation_cancelled_at_the_uac_administr");
                en = fr;
            }
            else if (outcome == ElevationOutcome.Success || partial || outcome == ElevationOutcome.TimedOut)
            {
                // The child ran but this op never reported back: it may have been applied in part.
                fr = Localizer.Ref("engine.fix_runner.no_result_received_for_this_operation");
                en = fr;
            }
            else
            {
                fr = Localizer.Ref("engine.fix_runner.the_administrator_operation_failed_see_l");
                en = fr;
            }
            return new FixRunResult { Op = op, Ok = false, Changes = new List<ChangeRecord>(), MessageFr = fr, MessageEn = en };
        }
    }
}
