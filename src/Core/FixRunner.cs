using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

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
    /// Runs fixes as a batch with change recording. When any op needs elevation and we are not admin,
    /// the whole batch runs in ONE elevated child (one UAC prompt). The op list travels on the command
    /// line; the child writes its results to a file we created.
    /// </summary>
    public static class FixRunner
    {
        /// <summary>Ops whose effect cannot be undone (files deleted, consoles launched).</summary>
        private static readonly HashSet<string> Irreversible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "shader_cache_apply", "sfc_repair_apply", "create_restore_point", "restore_snapshot"
        };

        public static bool IsReversible(string op, List<ChangeRecord> changes)
        {
            return !Irreversible.Contains(op) && changes != null && changes.Count > 0;
        }

        private static string TempDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder", "tmp"); }
        }

        /// <summary>Blocking. Call off the UI thread.</summary>
        public static List<FixRunResult> Run(IList<string> ops)
        {
            if (ops == null || ops.Count == 0) return new List<FixRunResult>();
            bool elevate = !Elevation.IsAdministrator() && ops.Any(DiagnosticsEngine.OpNeedsAdmin);
            if (!elevate) return ops.Select(RunRecorded).ToList();

            string outFile = NewTempFile("batch");
            ElevationOutcome outcome = Elevation.RunSelf("--elevated-batch " + ChangeRecorder.ToArg(ops.ToList()) + " \"" + outFile + "\"", 120000);
            if (outcome == ElevationOutcome.Success && File.Exists(outFile))
            {
                try
                {
                    var res = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<FixRunResult>>(File.ReadAllText(outFile, Encoding.UTF8));
                    if (res != null) return res;
                }
                catch (Exception ex) { Log.Error(ex, "batch result"); }
                finally { TryDelete(outFile); }
            }
            TryDelete(outFile);
            return ops.Select(op => Failed(op, outcome)).ToList();
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

        /// <summary>Elevated child entry point: --elevated-batch &lt;base64 ops&gt; &lt;out file&gt;.</summary>
        public static int ElevatedBatchMain(string opsArg, string outFile)
        {
            try
            {
                var ops = ChangeRecorder.FromArg<List<string>>(opsArg) ?? new List<string>();
                var results = ops.Select(RunRecorded).ToList();
                File.WriteAllText(outFile, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(results), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "elevated batch");
                return 1;
            }
        }

        /// <summary>Undo recorded changes (one UAC prompt if HKLM / power plan is involved). Blocking.</summary>
        public static bool Revert(List<ChangeRecord> changes, out string error)
        {
            error = null;
            if (changes == null || changes.Count == 0) return true;
            if (!ChangeRecorder.RequiresAdmin(changes) || Elevation.IsAdministrator())
            {
                ChangeRecorder.Revert(changes, out error);
                return error == null;
            }
            ElevationOutcome outcome = Elevation.RunSelf("--elevated-revert " + ChangeRecorder.ToArg(changes), 60000);
            if (outcome == ElevationOutcome.Success) return true;
            error = outcome == ElevationOutcome.Cancelled ? "UAC" : outcome.ToString();
            return false;
        }

        /// <summary>Elevated child entry point: --elevated-revert &lt;base64 changes&gt;.</summary>
        public static int ElevatedRevertMain(string changesArg)
        {
            try
            {
                var changes = ChangeRecorder.FromArg<List<ChangeRecord>>(changesArg);
                string error;
                ChangeRecorder.Revert(changes, out error);
                if (error != null) Log.Warn("Revert: " + error);
                return error == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "elevated revert");
                return 1;
            }
        }

        private static FixRunResult Failed(string op, ElevationOutcome outcome)
        {
            bool cancelled = outcome == ElevationOutcome.Cancelled;
            return new FixRunResult
            {
                Op = op,
                Ok = false,
                Changes = new List<ChangeRecord>(),
                MessageFr = cancelled ? "Opération annulée au contrôle Administrateur (UAC)." : "L'opération Administrateur a échoué (détails dans le journal).",
                MessageEn = cancelled ? "Operation cancelled at the UAC Administrator prompt." : "The Administrator operation failed (see log for details)."
            };
        }

        private static string NewTempFile(string prefix)
        {
            Directory.CreateDirectory(TempDir);
            return Path.Combine(TempDir, prefix + "_" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static void TryDelete(string f)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (Exception ex) { Log.Warn(ex, f); }
        }
    }
}
