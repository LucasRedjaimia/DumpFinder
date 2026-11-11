using System;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("Dump Finder")]
[assembly: AssemblyDescription("Windows Crash & Minidump Diagnostics, Hardware Check, AI Advisor, Native MCP Server & Stress Tester")]
[assembly: AssemblyProduct("Dump Finder")]
[assembly: AssemblyVersion("6.0.0.0")]
[assembly: AssemblyFileVersion("6.0.0.0")]

namespace WinCrashDoctor
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log.Error(e.ExceptionObject as Exception, "Unhandled (terminating=" + e.IsTerminating + ")");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            if (args != null && args.Length >= 1 && args[0] == "--mcp")
            {
                return McpServer.RunStdioLoop();
            }

            if (args != null && args.Length >= 2)
            {
                if (args[0] == "--elevated-op")
                {
                    string mFr, mEn;
                    bool ok = DiagnosticsEngine.ExecuteFixOperation(args[1], out mFr, out mEn);
                    return ok ? 0 : 1;
                }
                if (args[0] == "--analyze-dump" && args.Length >= 3)
                {
                    // Diagnostic helper: decode one .dmp and write the result as JSON.
                    var item = MinidumpAnalyzer.AnalyzeDumpFile(args[1]);
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    System.IO.File.WriteAllText(args[2], item == null ? "null" : ser.Serialize(item), System.Text.Encoding.UTF8);
                    return item == null ? 1 : 0;
                }
                if (args[0] == "--elevated-batch" && args.Length >= 3)
                    return FixRunner.ElevatedBatchMain(args[1], args[2]);
                if (args[0] == "--elevated-revert")
                    return FixRunner.ElevatedRevertMain(args[1]);
                if (args[0] == "--ai-selftest" && args.Length >= 3)
                {
                    // Diagnostic helper: one tiny request with an intentionally invalid key; the error text proves
                    // the provider stack (SDK, TLS, endpoint) works. Never reads the saved key.
                    var provider = (AiProviderType)Enum.Parse(typeof(AiProviderType), args[1], true);
                    var reply = AiAssistant.StreamAsync(provider, provider == AiProviderType.Claude ? "sk-ant-api03-invalid-selftest-key" : "invalid-selftest-key", null, "Say OK.", null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    System.IO.File.WriteAllText(args[2], (reply.Ok ? "OK: " : "ERR: ") + reply.Text, System.Text.Encoding.UTF8);
                    return 0;
                }
                if (args[0] == "--sensor-pipe" && args.Length >= 3)
                    return WinCrashDoctor.Stress.CpuTempReader.ServePipe(args[1], args[2]);
                if (args[0] == "--cpu-temp-probe" && args.Length >= 2)
                    return WinCrashDoctor.Stress.CpuTempReader.Probe(args[1]);
                if (args[0] == "--optimize-selftest" && args.Length >= 2)
                    return WinCrashDoctor.Optimize.OptimizeSelfTest.Run(args[1]);
                if (args[0] == "--detect-selftest" && args.Length >= 2)
                    return WinCrashDoctor.Detection.DetectionSelfTest.Run(args[1]);
                if (args[0] == "--update-selftest" && args.Length >= 2)
                    return UpdateSelfTest.Run(args[1]);
                if (args[0] == "--stress-selftest" && args.Length >= 2)
                    return WinCrashDoctor.Stress.StressSelfTest.Run(args[1], args.Length >= 3 ? args[2] : null);
                if (args[0] == "--deep-analyze")
                {
                    // Diagnostic helper: run WinDbg !analyze on one dump (fills the analysis cache).
                    var r = DeepAnalyzer.Analyze(args[1]);
                    return r.Ok ? 0 : 1;
                }
                if (args[0] == "--scan-admin-json")
                {
                    bool ok = DiagnosticsEngine.RunAdminDeepScanToFile(args[1]);
                    return ok ? 0 : 1;
                }
                if (args[0] == "--screenshot")
                {
                    string outDir = args[1];
                    WindowShell.ForceSolidBackground = true; // RenderTargetBitmap cannot capture Mica
                    Localizer.ReadOnlySettings = true;       // captures must never alter the user's settings
                    AppSettings sSaved = Localizer.LoadSettings();
                    // Capture-only language override (in memory; settings are read-only here): --lang fr|en|es|de|it|pt
                    int langArg = Array.IndexOf(args, "--lang");
                    if (langArg > 0 && langArg + 1 < args.Length)
                    {
                        string[] codes = { "fr", "en", "es", "de", "it", "pt" };
                        AppLang[] langs = { AppLang.FR, AppLang.EN, AppLang.ES, AppLang.DE, AppLang.IT, AppLang.PT };
                        int li = Array.IndexOf(codes, args[langArg + 1].ToLowerInvariant());
                        if (li >= 0) { sSaved.Language = (int)langs[li]; Localizer.Current = langs[li]; }
                    }
                    Application sApp = new Application();
                    MainWindow sWin = new MainWindow(sSaved);

                    // Optional capture-only overrides (never persisted): --theme light|dark, --collapsed
                    int themeArg = Array.IndexOf(args, "--theme");
                    if (themeArg > 0 && themeArg + 1 < args.Length)
                        ThemeManager.ApplyTheme(sWin, args[themeArg + 1] == "light" ? AppThemeMode.Light : AppThemeMode.Dark);
                    bool captureCollapsed = Array.IndexOf(args, "--collapsed") > 0;

                    sWin.Loaded += async (s, e) =>
                    {
                        if (captureCollapsed) sWin.SetSidebarCollapsedForCapture(true);
                        await System.Threading.Tasks.Task.Delay(3200);
                        Action<string> capture = (fileName) =>
                        {
                            sWin.UpdateLayout();
                            int w = (int)sWin.ActualWidth; if (w <= 0) w = 1190;
                            int h = (int)sWin.ActualHeight; if (h <= 0) h = 810;
                            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            rtb.Render(sWin);
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                            using (var fs = System.IO.File.Create(System.IO.Path.Combine(outDir, fileName)))
                            {
                                enc.Save(fs);
                            }
                        };

                        try
                        {
                            Func<int, string, System.Threading.Tasks.Task> shot = async (tab, name) =>
                            {
                                if (tab >= 0) sWin.SwitchTab(tab);
                                await System.Threading.Tasks.Task.Delay(450);
                                capture(name);
                            };

                            await shot(0, "v6_1_accueil.png");
                            await shot(1, "v6_2_crashs.png");
                            sWin.CaptureSubView("crash-drawer-bottom");
                            await shot(-1, "v6_2b_crashs_detail_bas.png");
                            sWin.CaptureSubView("crashes-guide");
                            await shot(-1, "v6_3_crashs_pannes.png");
                            sWin.CaptureSubView("crashes-list");
                            await shot(2, "v6_4_materiel.png");
                            sWin.CaptureSubView("conflicts-bottom");
                            await shot(-1, "v6_4b_materiel_conflits_bas.png");
                            sWin.SwitchHealthSubTab(2);
                            await shot(-1, "v6_5_materiel_stress.png");
                            if (Array.IndexOf(args, "--stress-demo") > 0)
                            {
                                var demo = sWin.RunStressDemoForCapture();
                                await System.Threading.Tasks.Task.Delay(9000);
                                capture("v6_5c_stress_en_cours.png");
                                await demo;
                                await shot(-1, "v6_5d_stress_resultats.png");
                            }
                            sWin.SwitchHealthSubTab(3);
                            await shot(-1, "v6_5b_materiel_pilotes.png");
                            sWin.SwitchHealthSubTab(0);
                            await shot(3, "v6_6_correctifs.png");
                            sWin.CaptureSubView("fixes-bottom");
                            await shot(-1, "v6_6b_correctifs_bas.png");
                            await shot(4, "v6_7a_optimiser.png");
                            sWin.CaptureSubView("optimize-apps");
                            await System.Threading.Tasks.Task.Delay(4000);
                            await shot(-1, "v6_7b_optimiser_apps.png");
                            sWin.CaptureSubView("optimize-startup");
                            await System.Threading.Tasks.Task.Delay(1500);
                            await shot(-1, "v6_7c_optimiser_demarrage.png");
                            sWin.CaptureSubView("optimize-clean");
                            await System.Threading.Tasks.Task.Delay(2500);
                            await shot(-1, "v6_7d_optimiser_nettoyage.png");
                            sWin.CaptureSubView("optimize-myths");
                            await shot(-1, "v6_7e_optimiser_mythes.png");
                            await shot(5, "v6_7_outils.png");
                            sWin.CaptureSubView("tools-report");
                            await shot(-1, "v6_8_outils_rapport.png");
                            sWin.CaptureSubView("tools-history");
                            await shot(-1, "v6_8b_outils_historique.png");
                            sWin.ExportAllReportsTo(outDir);
                            sWin.CaptureSubView("tools-windows");
                            await shot(6, "v6_9_parametres.png");

                            sWin.SwitchTab(0);
                            sWin.StartOnboarding();
                            await shot(-1, "v6_10_bienvenue_1.png");
                            sWin.NextOnboardingStep();
                            await shot(-1, "v6_11_bienvenue_2.png");
                            sWin.NextOnboardingStep();
                            await shot(-1, "v6_12_bienvenue_3.png");
                            sWin.EndOnboarding();

                            sWin.ShowToastPreviewForCapture();
                            await shot(-1, "v6_13_toasts.png");
                            sWin.ShowUpdateForCapture();
                            await shot(-1, "v6_14_mise_a_jour.png");
                        }
                        catch (Exception ex) { Log.Warn(ex); }
                        finally
                        {
                            Environment.Exit(0);
                        }
                    };
                    sApp.Run(sWin);
                    return 0;
                }
            }

            AppSettings saved = Localizer.LoadSettings();
            if (Enum.IsDefined(typeof(AppLang), saved.Language))
                Localizer.Current = (AppLang)saved.Language;
            else
                Localizer.Current = Localizer.DetectSystemLanguage();

            // Started by Windows at logon (Settings › Start with Windows): live in the tray, no window.
            bool trayStart = args != null && Array.IndexOf(args, Autostart.TrayArg) >= 0;
            if (trayStart && !saved.TraySentinelEnabled) return 0;

            Application app = new Application();
            app.DispatcherUnhandledException += (s, e) =>
            {
                // Keep the app alive on UI-thread faults (async void handlers, render code) and leave a trace.
                Log.Error(e.Exception, "Dispatcher unhandled exception");
                e.Handled = true;
            };
            MainWindow win = new MainWindow(saved);
            Autostart.RefreshPathIfEnabled();
            if (trayStart)
            {
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.MainWindow = win;
                win.StartInTray();
                app.Run();
                return 0;
            }
            app.Run(win);
            return 0;
        }
    }
}
