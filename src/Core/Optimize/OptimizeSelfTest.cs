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
            sb.AppendLine("=== Stockage ===");
            StorageChecks(sb, expect);

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }

        /// <summary>
        /// Storage in a temporary profile: folder sums, largest first, largest files, protected items, then a real
        /// trip to the Recycle Bin and back through the journal's undo.
        /// </summary>
        private static void StorageChecks(StringBuilder sb, Action<string, bool> expect)
        {
            string home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
            string profileBefore = OptSpace.Profile;
            try
            {
                OptSpace.Profile = home;
                Func<string, string> P = rel => System.IO.Path.Combine(home, rel);
                System.IO.Directory.CreateDirectory(P(@"Projects\big"));
                System.IO.Directory.CreateDirectory(P("Documents"));
                System.IO.Directory.CreateDirectory(P(".ssh"));
                System.IO.File.WriteAllBytes(P(@"Projects\big\video.mp4"), new byte[3 << 20]);
                System.IO.File.WriteAllBytes(P(@"Projects\notes.txt"), new byte[1 << 20]);
                System.IO.File.WriteAllBytes(P(@"Documents\cv.pdf"), new byte[2 << 20]);
                System.IO.File.WriteAllBytes(P(@".ssh\id_ed25519"), new byte[100]);

                var scan = SpaceScan.Scan(home, System.Threading.CancellationToken.None, null, 1 << 20);
                var top = SpaceScan.ChildrenOf(scan.Root);
                SpaceNode projects = scan.Find(P("Projects"));
                expect("stockage : tailles des dossiers additionnées, plus gros d'abord, plus gros fichiers",
                    projects != null && projects.Bytes == 4 << 20 && top[0].Name == "Projects" && top[1].Name == "Documents"
                    && scan.Root.Bytes == (6 << 20) + 100 && scan.LargestFiles.Count == 3 && scan.LargestFiles[0].Name == "video.mp4"
                    && scan.LargestFiles[0].FullPath == P(@"Projects\big\video.mp4"));

                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                expect("stockage : l'essentiel protégé, le reste peut aller à la corbeille",
                    OptSpace.IsProtected(home) && OptSpace.IsProtected(P("Documents")) && OptSpace.IsProtected(P(@".ssh\id_ed25519"))
                    && OptSpace.IsProtected(P(@"AppData\Roaming")) && OptSpace.IsProtected(P(@"AppData\Local\Microsoft\Credentials"))
                    && OptSpace.IsProtected(P("NTUSER.DAT")) && OptSpace.IsProtected(P("OneDrive - Contoso"))
                    && OptSpace.IsProtected(win) && OptSpace.IsProtected(System.IO.Path.Combine(win, "System32"))
                    && OptSpace.IsProtected(System.IO.Path.Combine(pf, "Something")) && OptSpace.IsProtected(@"C:\") && OptSpace.IsProtected(@"C:\pagefile.sys")
                    && OptSpace.IsProtected(@"D:\$Recycle.Bin\x") && OptSpace.IsProtected(@"\\server\share\x")
                    && !OptSpace.IsProtected(P("Projects")) && !OptSpace.IsProtected(P(@"Documents\cv.pdf"))
                    && !OptSpace.IsProtected(P(@"AppData\Local\Google\Chrome\User Data\Default\Cache")) && !OptSpace.IsProtected(@"D:\Games\Old"));

                string exe, args;
                expect("stockage : commande de désinstallation découpée",
                    OptSpace.SplitCommand("\"C:\\Program Files\\X\\unins000.exe\" /SILENT", out exe, out args) && exe == @"C:\Program Files\X\unins000.exe" && args == "/SILENT"
                    && OptSpace.SplitCommand("MsiExec.exe /X{1234}", out exe, out args) && exe == "MsiExec.exe" && args == "/X{1234}");

                string target = P(@"Projects\big");
                string msg;
                var res = OptSpace.MoveToRecycleBin(target, "big", 3 << 20, IntPtr.Zero, out msg);
                var entry = History.Journal().FirstOrDefault(j => j.Op == "space_recycle" && j.Changes != null && j.Changes.Count == 1 && j.Changes[0].FilePath == target);
                bool recycled = res == OptSpace.RecycleResult.Recycled && !System.IO.Directory.Exists(target) && entry != null && entry.Reversible;
                scan.Forget(scan.Find(target));
                bool shrank = projects != null && projects.Bytes == 1 << 20 && scan.LargestFiles.All(f => f.Name != "video.mp4");
                string undoErr = "aucune entrée";
                bool undone = entry != null && FixRunner.Revert(entry.Changes, out undoErr);
                if (undone) History.MarkReverted(entry.Id);
                expect("stockage : corbeille, journalisé, annulé (" + msg + (undone ? "" : " / " + undoErr) + ")",
                    recycled && shrank && undone && System.IO.File.Exists(P(@"Projects\big\video.mp4")));

                string refused;
                expect("stockage : élément protégé refusé, journal forgé refusé",
                    OptSpace.MoveToRecycleBin(P("Documents"), "Documents", 0, IntPtr.Zero, out refused) == OptSpace.RecycleResult.Protected
                    && OptSpace.RestoreRecycled(new ChangeRecord { Kind = "recycle", FilePath = @"C:\Windows\System32\x.dll", BackupPath = P(@"$Ixyz.dll") }) != null);
            }
            catch (Exception ex) { expect("stockage : " + ex.Message, false); }
            finally
            {
                OptSpace.Profile = profileBefore;
                try { System.IO.Directory.Delete(home, true); } catch (Exception) { }
            }

            // This PC, read-only: the programs and the size of the user folder.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var progs = OptSpace.Programs(System.Threading.CancellationToken.None);
            sb.AppendLine("   " + progs.Count + " programme(s) en " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            foreach (var p in progs.Take(8)) sb.AppendLine("   " + OptSpace.Human(p.Bytes).PadLeft(10) + (p.Estimated ? " ≈ " : "   ") + p.Name);
            sw.Restart();
            var mine = SpaceScan.Scan(OptSpace.Profile, System.Threading.CancellationToken.None);
            sb.AppendLine("   Profil : " + OptSpace.Human(mine.ScannedBytes) + " dans " + mine.ScannedFiles.ToString("N0") + " fichiers, " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            foreach (var k in SpaceScan.ChildrenOf(mine.Root).Take(8)) sb.AppendLine("   " + OptSpace.Human(k.Bytes).PadLeft(10) + "   " + k.Name + (OptSpace.IsProtected(k.FullPath) ? "  [protégé]" : ""));
            foreach (var f in mine.LargestFiles.Take(5)) sb.AppendLine("   " + OptSpace.Human(f.Bytes).PadLeft(10) + "   " + f.FullPath);
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
