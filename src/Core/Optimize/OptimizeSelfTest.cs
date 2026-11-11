using System;
using System.Linq;
using System.Text;

namespace WinCrashDoctor.Optimize
{
    /// <summary>
    /// --optimize-selftest &lt;out&gt;: checks the Optimiser on this PC. Read-only except one round trip on a setting
    /// that is already in its default state (Game Mode on): apply, then undo through the journal mechanism.
    /// </summary>
    internal static class OptimizeSelfTest
    {
        public static int Run(string outPath)
        {
            Localizer.Current = AppLang.FR;
            var sb = new StringBuilder();
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            // Settings: op parsing, rights, titles
            foreach (var it in OptTweaks.Items())
            {
                string op = it.Id + "_apply";
                expect(it.Id + " reconnu, titre présent", OptOps.IsOp(op) && OptOps.Title(op) != null && OptOps.IsOp(it.Id + "_restore"));
                expect(it.Id + " droits admin cohérents (" + it.RequiresAdmin + ")", OptOps.NeedsAdmin(op) == it.RequiresAdmin);
            }
            expect("op inconnue refusée", !OptOps.IsOp("opt_nimportequoi_apply"));

            // Undo allowlist: only the exact keys these features write
            expect("clé de réglage autorisée", ChangeRecorderAllowed(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"));
            expect("clé StartupApproved autorisée", ChangeRecorderAllowed(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"));
            expect("clé quelconque refusée", !ChangeRecorderAllowed(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip"));
            expect("sous-clé de StartupApproved refusée", !ChangeRecorderAllowed(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\x"));

            // Startup: every entry round-trips through its op; a forged one is refused without writing
            var st = OptStartup.Scan();
            sb.AppendLine();
            sb.AppendLine("=== Démarrage (" + st.Count + ") ===");
            foreach (var e in st)
            {
                sb.AppendLine("   " + (e.Enabled ? "ON " : "off") + " · " + e.Name + " · " + (e.Publisher ?? "?") + " · " + e.Scope + (e.Admin ? " (admin)" : ""));
                expect("op retrouvée pour « " + e.Name + " »", OptOps.Title(e.Op(true)) != null && OptOps.NeedsAdmin(e.Op(true)) == e.Admin);
            }
            var fake = new StartupEntry { Scope = "cu", Name = "DumpFinder-introuvable-" + Guid.NewGuid().ToString("N") };
            string fr, en;
            expect("entrée de démarrage inexistante refusée", !OptStartup.Execute(fake.Op(true), out fr, out en));

            // Cleanup sizes
            sb.AppendLine();
            sb.AppendLine("=== Nettoyage ===");
            foreach (var c in OptCleanup.Scan())
            {
                sb.AppendLine("   " + c.Id + " : " + (c.Bytes < 0 ? "illisible sans admin" : (c.Bytes / 1048576.0).ToString("N0") + " Mo, " + c.Files + " fichier(s)"));
                expect(c.Id + " reconnu", OptOps.IsOp(c.Op) && OptOps.NeedsAdmin(c.Op) == c.Admin);
            }

            // Apps (PowerShell)
            sb.AppendLine();
            var apps = OptApps.Scan();
            sb.AppendLine("=== Applications de la liste installées (" + apps.Count + ") ===");
            foreach (var a in apps) sb.AppendLine("   " + (a.Suggested ? "[x] " : "[ ] ") + a.Title + " · " + a.Name);

            // Round trip on an already-default setting: apply then undo restores the exact previous state
            sb.AppendLine();
            var before = ReadGameMode();
            var results = FixRunner.Run(new[] { "opt_game_mode_apply" });
            var r = results.FirstOrDefault();
            expect("application de « mode Jeu » (" + (r != null ? r.Message : "aucun résultat") + ")", r != null && r.Ok && r.Changes != null && r.Changes.Count == 1);
            string err;
            int undone = r != null ? ChangeRecorder.Revert(r.Changes, out err) : 0;
            expect("annulation exacte (avant=" + (before ?? "absent") + ", après=" + (ReadGameMode() ?? "absent") + ")", undone == 1 && Equals(before, ReadGameMode()));

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }

        private static string ReadGameMode()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
            {
                object v = k != null ? k.GetValue("AutoGameModeEnabled") : null;
                return v != null ? Convert.ToString(v) : null;
            }
        }

        private static bool ChangeRecorderAllowed(string key)
        {
            var m = typeof(ChangeRecorder).GetMethod("IsAllowedKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (bool)m.Invoke(null, new object[] { key });
        }
    }
}
