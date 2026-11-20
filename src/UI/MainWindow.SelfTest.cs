using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>
    /// --ui-selftest &lt;out.txt&gt;: accessibility and keyboard checks on the real window (phase 7 regressions).
    /// Contrast of every text / surface pair of both palettes (WCAG AA 4.5:1), an accessible name on every button
    /// of every page, keyboard shortcuts, clickable cards exposed as buttons (Invoke / ExpandCollapse), and the
    /// pseudo-locale keeping placeholders and plural labels intact. Settings are read-only. Exit code = failures.
    /// </summary>
    public partial class MainWindow
    {
        public static int RunUiSelfTest(string outPath)
        {
            Localizer.ReadOnlySettings = true;
            WindowShell.ForceSolidBackground = true;
            AppSettings saved = Localizer.LoadSettings();
            saved.Language = (int)AppLang.EN;
            Localizer.Current = AppLang.EN;
            int failures = 0;

            var app = new Application();
            var win = new MainWindow(saved);
            win.Loaded += async (s, e) =>
            {
                var sb = new StringBuilder();
                try
                {
                    await Task.Delay(500);
                    failures = await win.UiChecks(sb);
                }
                catch (Exception ex)
                {
                    failures++;
                    sb.AppendLine("[FAIL] exception: " + ex);
                }
                sb.AppendLine(failures == 0 ? "CONFORME" : failures + " ÉCHEC(S)");
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                Environment.Exit(failures);
            };
            app.Run(win);
            return failures;
        }

        private async Task<int> UiChecks(StringBuilder sb)
        {
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            // ---- First scan: the result-dependent buttons stay hidden until it ends (slow PCs, CI runners)
            if (_lastResult == null)
            {
                expect("Pendant la 1re analyse : boutons du résumé masqués", _heroButtons.Visibility != Visibility.Visible);
                var early = new List<string>();
                UpdateLayout();
                CollectNameless(this, early, "accueil-analyse", new int[1]);
                expect("Pendant la 1re analyse : aucun contrôle sans nom" + (early.Count > 0 ? " : " + string.Join(", ", early.Distinct()) : ""), early.Count == 0);
            }
            else
                sb.AppendLine("[SKIP] analyse déjà finie au démarrage du test : état « pendant l'analyse » non vérifié");
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while ((_lastResult == null || _scanInProgress) && waited.Elapsed.TotalSeconds < 120) await Task.Delay(250);
            expect("1re analyse terminée (" + (int)waited.Elapsed.TotalSeconds + " s)", _lastResult != null && !_scanInProgress);
            expect("Après l'analyse : boutons du résumé affichés", _heroButtons.Visibility == Visibility.Visible);
            await Settle();

            // ---- Stability chart: the tooltip sits over the hovered day's point and follows it
            SwitchTab(0);
            await Settle();
            var tip = _stabilityChart.ToolTip as ToolTip;
            var placed = new List<string>();
            bool follows = tip != null && _stabilityChart.ActualWidth > 0;
            if (follows)
            {
                double lastX = double.NaN;
                foreach (int day in new[] { 9, 20 })
                {
                    _stabilityChart.SetHover(day);
                    tip.IsOpen = true;
                    await Settle();
                    Point pt = _stabilityChart.PointToScreen(_stabilityChart.HoverPoint);
                    Point tipTop = tip.PointToScreen(new Point(tip.ActualWidth / 2, 0));
                    Point tipBottom = tip.PointToScreen(new Point(tip.ActualWidth / 2, tip.ActualHeight));
                    bool centred = Math.Abs(tipTop.X - pt.X) <= 2;
                    bool clear = tipBottom.Y <= pt.Y || tipTop.Y >= pt.Y;   // above the point, or below it near the top of the screen
                    placed.Add("jour " + day + " : écart " + (tipTop.X - pt.X).ToString("0.#") + " px");
                    follows &= tip.IsOpen && centred && clear && (double.IsNaN(lastX) || tipTop.X > lastX + 20);
                    lastX = tipTop.X;
                }
                tip.IsOpen = false;
            }
            expect("Graphe de stabilité : infobulle centrée sur le point survolé et qui le suit (" + string.Join(", ", placed) + ")", follows);

            // ---- Hardware › GPU curve (only on a PC with MSI Afterburner and an NVIDIA card)
            if (await OpenGpuCurveForCapture())
            {
                ShowGpuCurveProfileForCapture(null);
                await Settle();
                VfCurveChart vc = GpuCurveChartForTest;
                var vtip = vc != null ? vc.ToolTip as ToolTip : null;
                bool ok = vc != null && vtip != null && vc.ActualWidth > 0 && vc.Count > 10;
                string gap = "";
                if (ok)
                {
                    vc.SetHover(vc.Count - 20);
                    vtip.IsOpen = true;
                    await Settle();
                    Point pt = vc.PointToScreen(vc.HoverPoint);
                    Point top = vtip.PointToScreen(new Point(vtip.ActualWidth / 2, 0));
                    gap = (top.X - pt.X).ToString("0.#") + " px";
                    ok = vtip.IsOpen && Math.Abs(top.X - pt.X) <= 2 && !string.IsNullOrEmpty(AutomationProperties.GetName(vc));
                    vtip.IsOpen = false;
                }
                expect("Courbe GPU : onglet ouvert, graphe nommé, infobulle sur le point survolé (" + gap + ")", ok);
                SwitchHealthSubTab(0);
            }
            else sb.AppendLine("[SKIP] Courbe GPU : pas de MSI Afterburner avec une carte NVIDIA sur ce PC");

            // ---- Contrast (WCAG 2.1 AA: 4.5:1 for text)
            AppThemeMode keep = ThemeManager.CurrentMode;
            foreach (AppThemeMode mode in new[] { AppThemeMode.Dark, AppThemeMode.Light })
            {
                ThemeManager.ApplyTheme(this, mode);
                var low = new List<string>();
                string[] surfaces = { "BgWindow", "BgShell", "BgCard", "BgSubtle" };
                foreach (string fg in new[] { "TextPrimary", "TextSecondary", "TextMuted", "AccentText" })
                    foreach (string bg in surfaces) CheckPair(fg, bg, low);
                foreach (string tone in new[] { "Success", "Warn", "Danger" })
                {
                    CheckPair(tone + "Fg", tone + "SoftBg", low);
                    CheckPair(tone + "Fg", "BgCard", low);
                }
                CheckPair("AccentText", "AccentSoftBg", low);
                CheckPair("OnAccent", "Accent", low);
                expect("Contraste " + mode + " ≥ 4,5:1 (texte, statuts, boutons)" + (low.Count > 0 ? " : " + string.Join(", ", low) : ""), low.Count == 0);
            }
            ThemeManager.ApplyTheme(this, keep);

            // ---- Contrast theme palette (system colours, forced): applied and reverted
            ThemeManager.ForceHighContrast = true;
            ThemeManager.ApplyTheme(this, keep);
            Brush hcText = Resources["TextMuted"] as Brush;
            expect("Thème contrasté : couleurs système appliquées", ThemeManager.HighContrastApplied && hcText == SystemColors.WindowTextBrush);
            ThemeManager.ForceHighContrast = false;
            ThemeManager.ApplyTheme(this, keep);
            expect("Thème contrasté : palette normale rétablie", !ThemeManager.HighContrastApplied && Resources["TextMuted"] != SystemColors.WindowTextBrush);

            // ---- Accessible names: every visible button on every page
            var nameless = new List<string>();
            var seen = new int[1];
            for (int tab = 0; tab < NavCount; tab++)
            {
                SwitchTab(tab);
                await Settle();
                CollectNameless(this, nameless, "tab" + tab, seen);
            }
            SwitchTab(TabHardware);
            for (int sub = 1; sub <= 3; sub++) // conflicts, stress test, drivers
            {
                SwitchHealthSubTab(sub);
                await Settle();
                CollectNameless(this, nameless, "hw" + sub, seen);
            }
            SwitchHealthSubTab(0);
            SwitchTab(TabCrashes);
            SwitchCrashSubTab(true);
            await Settle();
            CollectNameless(this, nameless, "guide", seen);
            SwitchCrashSubTab(false);
            expect("Nom accessible sur chaque bouton, liste, curseur visible (" + seen[0] + " vérifiés)" + (nameless.Count > 0 ? " : " + string.Join(", ", nameless.Distinct().Take(12)) : ""), nameless.Count == 0);

            // ---- Keyboard shortcuts
            SwitchTab(TabHome);
            bool used = HandleShortcut(Key.D3, ModifierKeys.Control);
            expect("Ctrl+3 ouvre Matériel", used && _activeTab == TabHardware);
            used = HandleShortcut(Key.NumPad7, ModifierKeys.Control);
            expect("Ctrl+7 (pavé numérique) ouvre Paramètres", used && _activeTab == TabSettings);
            expect("Ctrl+8 n'est pas utilisé", !HandleShortcut(Key.D8, ModifierKeys.Control) && _activeTab == TabSettings);
            expect("Alt+1 n'est pas utilisé", !HandleShortcut(Key.D1, ModifierKeys.Alt));
            HandleShortcut(Key.D2, ModifierKeys.Control);
            await Settle();

            // ---- Crash rows: buttons for UI Automation, Invoke selects, Esc closes the panel
            var rows = _crashListPanel.Children.OfType<ClickableCard>().ToList();
            if (rows.Count >= 2)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(rows[1]);
                var invoke = peer != null ? peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider : null;
                expect("Ligne de crash : type Bouton + nom", peer != null && peer.GetAutomationControlType() == AutomationControlType.Button && !string.IsNullOrEmpty(peer.GetName()));
                string target = rows[1].Tag as string;
                if (invoke != null) invoke.Invoke();
                expect("Ligne de crash : Invoke sélectionne le crash", target != null && _selectedCrashKey == target);
                expect("Ligne de crash : panneau de détail ouvert", _crashDrawer.Visibility == Visibility.Visible);
                expect("Échap ferme le panneau de détail", HandleShortcut(Key.Escape, ModifierKeys.None) && _crashDrawer.Visibility != Visibility.Visible);
                expect("Échap sans panneau ouvert n'est pas utilisé", !HandleShortcut(Key.Escape, ModifierKeys.None));
            }
            else
                sb.AppendLine("[SKIP] moins de 2 crashs sur ce PC : lignes de crash non testées");

            // ---- Knowledge-base cards: expandable for UI Automation
            RenderKnowledgeBase();
            var kb = _guideListPanel.Children.OfType<ClickableCard>().ToList();
            expect("Base de connaissances : cartes cliquables", kb.Count > 0);
            if (kb.Count >= 2)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(kb[1]);
                var ec = peer != null ? peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider : null;
                expect("Carte : motif Développer / Réduire, réduite au départ", ec != null && ec.ExpandCollapseState == ExpandCollapseState.Collapsed);
                if (ec != null)
                {
                    ec.Expand();
                    expect("Carte : Expand l'ouvre", ec.ExpandCollapseState == ExpandCollapseState.Expanded && kb[1].Expanded == true);
                    ec.Collapse();
                    expect("Carte : Collapse la referme", ec.ExpandCollapseState == ExpandCollapseState.Collapsed);
                }
            }

            // ---- Pseudo-locale: placeholders and plural labels kept
            string ps = Localizer.PseudoText("one: {0} crash | other: {0} crashes");
            expect("Pseudo-langue : {0} et catégories de pluriel intacts (" + ps + ")",
                ps.StartsWith("one: [", StringComparison.Ordinal) && ps.Contains("| other: [") && ps.Split(new[] { "{0}" }, StringSplitOptions.None).Length == 3);
            string pg = Localizer.PseudoText("✓ Applied");
            string glyph, rest;
            expect("Pseudo-langue : préfixe d'icône conservé en tête", Glyph.TrySplitLegacyPrefix(pg, out glyph, out rest) && rest.StartsWith("[", StringComparison.Ordinal));

            return bad;
        }

        private void CheckPair(string fgKey, string bgKey, List<string> low)
        {
            var fg = Resources[fgKey] as SolidColorBrush;
            var bg = Resources[bgKey] as SolidColorBrush;
            if (fg == null || bg == null) { low.Add(fgKey + "/" + bgKey + " absent"); return; }
            double r = Contrast(fg.Color, bg.Color);
            if (r < 4.5) low.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}/{1} {2:0.00}", fgKey, bgKey, r));
        }

        internal static double Contrast(Color a, Color b)
        {
            double la = RelLum(a), lb = RelLum(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double RelLum(Color c)
        {
            Func<byte, double> ch = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        /// <summary>Visible, enabled-or-not buttons whose UI Automation name is empty.</summary>
        private static void CollectNameless(DependencyObject root, List<string> found, string where, int[] seen)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                var fe = child as FrameworkElement;
                if (fe != null && fe.Visibility != Visibility.Visible) continue;
                // Buttons, drop-down lists, sliders and secret fields: anything the user operates.
                var btn = (child is System.Windows.Controls.Primitives.ButtonBase || child is ComboBox || child is Slider || child is PasswordBox) ? (Control)child : null;
                if (btn != null && btn.IsVisible && btn.Focusable && !(btn.TemplatedParent is Control))
                {
                    seen[0]++;
                    var peer = UIElementAutomationPeer.CreatePeerForElement(btn);
                    if (peer == null || string.IsNullOrWhiteSpace(peer.GetName()))
                        {
                        var lb = AutomationProperties.GetLabeledBy(btn) as TextBlock;
                        found.Add(where + ":" + btn.GetType().Name + "(" + Describe(btn is ContentControl ? ((ContentControl)btn).Content : null) + (lb != null ? ", libellé « " + lb.Text + " »" : "") + ")");
                    }
                }
                CollectNameless(child, found, where, seen);
            }
        }

        private static string Describe(object content)
        {
            var s = content as string;
            if (s != null) return "\"" + s + "\"";
            var tb = content as TextBlock;
            if (tb != null) return "glyph U+" + (tb.Text.Length > 0 ? ((int)tb.Text[0]).ToString("X4") : "");
            return content == null ? "vide" : content.GetType().Name;
        }

        private async Task Settle()
        {
            UpdateLayout();
            await Task.Delay(250);
            UpdateLayout();
        }
    }
}
