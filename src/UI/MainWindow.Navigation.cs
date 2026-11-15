using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinCrashDoctor.Stress;

namespace WinCrashDoctor
{
    /// <summary>
    /// In-app navigation commands ("nav:…") shared by the health quick actions, the stress results
    /// and the tray alerts. A nav: command never reaches the shell (Process.Start).
    /// <list type="bullet">
    /// <item>nav:home · nav:crashes · nav:hardware · nav:conflicts · nav:drivers · nav:fixes · nav:optimize · nav:tools · nav:settings</item>
    /// <item>nav:stress · nav:stress_ram · nav:stress_cpu: stability test page, with that plan preselected</item>
    /// <item>nav:fixes:&lt;fixId&gt;: the fix card, made visible, scrolled into view and highlighted (<see cref="OpenFix"/>)</item>
    /// <item>nav:crash:&lt;key&gt;: that crash in Crashs (<see cref="OpenCrash"/>)</item>
    /// </list>
    /// </summary>
    public partial class MainWindow
    {
        private const string NavPrefix = "nav:";

        /// <summary>Fix requested before the first scan result: focused once the fix cards are rendered.</summary>
        private string _pendingFixFocusId;
        private Border _highlightedFixCard;
        private RegistryFixItem _highlightedFix;
        private Thickness _highlightedFixPadding;
        private DispatcherTimer _fixHighlightTimer;

        private static bool IsNavCommand(string cmd)
        {
            return !string.IsNullOrEmpty(cmd) && cmd.StartsWith(NavPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Handles any nav: command (true), known or not, so the caller never falls through to the shell.
        /// Returns false only when <paramref name="cmd"/> is not a nav: command.
        /// </summary>
        private bool TryNavigate(string cmd, bool reportUnknown = true)
        {
            if (!IsNavCommand(cmd)) return false;
            if (!Navigate(cmd))
            {
                Log.Warn("Unknown navigation command: " + cmd);
                if (reportUnknown) SetToast(Localizer.S("health.unable_to_open_action") + cmd, "WarnFg");
            }
            return true;
        }

        /// <summary>Runs one nav: command on the UI thread. False (and nothing changes) when it is unknown.</summary>
        private bool Navigate(string cmd)
        {
            if (!IsNavCommand(cmd)) return false;
            string body = cmd.Substring(NavPrefix.Length).Trim();
            int colon = body.IndexOf(':');
            string head = (colon >= 0 ? body.Substring(0, colon) : body).Trim().ToLowerInvariant();
            string arg = colon >= 0 ? body.Substring(colon + 1).Trim() : null;

            switch (head)
            {
                case "home": SwitchTab(TabHome); return true;
                case "crashes": SwitchTab(TabCrashes); return true;
                case "crash":
                    if (string.IsNullOrEmpty(arg)) return false;
                    OpenCrash(arg);
                    return true;
                case "hardware": OpenHardwareSub(0); return true;
                case "conflicts": OpenHardwareSub(1); return true;
                case "drivers": OpenHardwareSub(3); return true;
                case "stress": OpenStress(null); return true;
                case "stress_ram": OpenStress(StressPlanKind.Ram); return true;
                case "stress_cpu": OpenStress(StressPlanKind.CpuAllCore); return true;
                case "fixes":
                    if (string.IsNullOrEmpty(arg)) SwitchTab(TabFixes);
                    else OpenFix(arg);
                    return true;
                case "optimize": SwitchTab(TabOptimize); return true;
                case "tools": SwitchTab(TabTools); return true;
                case "settings": SwitchTab(TabSettings); return true;
                default: return false;
            }
        }

        /// <summary>Tray notification click: brings the window back, then navigates (UI thread).</summary>
        private void NavigateFromTray(string cmd)
        {
            Action go = () =>
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                TryNavigate(cmd);
            };
            if (Dispatcher.CheckAccess()) go();
            else Dispatcher.BeginInvoke(go);
        }

        private void OpenHardwareSub(int sub)
        {
            SwitchTab(TabHardware);
            SwitchHealthSubTab(sub);
        }

        /// <summary>Stability test page; preselects <paramref name="plan"/> unless a test is already running.</summary>
        private void OpenStress(StressPlanKind? plan)
        {
            OpenHardwareSub(2);
            if (!plan.HasValue || _stressRunner.IsRunning || _cbStressPlan == null) return;
            int idx = (int)plan.Value;
            _stressPlan = plan.Value;
            if (idx < _cbStressPlan.Items.Count && _cbStressPlan.SelectedIndex != idx)
                _cbStressPlan.SelectedIndex = idx; // SelectionChanged refreshes the hint
            else
                UpdateStressPlanHint();
        }

        /// <summary>
        /// Opens Fixes on one fix card: clears the filter that hides it (group pill or "my hardware"),
        /// scrolls it into view and highlights it for a few seconds. Unknown id: just the Fixes tab.
        /// </summary>
        public void OpenFix(string fixId)
        {
            SwitchTab(TabFixes);
            if (string.IsNullOrEmpty(fixId)) return;
            if (_lastResult == null || _lastResult.Fixes == null)
            {
                _pendingFixFocusId = fixId; // first scan still running: focus it when the cards appear
                return;
            }
            _pendingFixFocusId = null;

            RegistryFixItem fix = _lastResult.Fixes.FirstOrDefault(f => string.Equals(f.Id, fixId, StringComparison.OrdinalIgnoreCase));
            if (fix == null)
            {
                Log.Warn("OpenFix: unknown fix id " + fixId);
                return;
            }

            bool rerender = false;
            if (_fixGroupFilter != FixGroup.All && fix.Group != _fixGroupFilter)
            {
                _fixGroupFilter = FixGroup.All;
                UpdateFixGroupStyles();
                rerender = true;
            }
            if (_onlyHardwareRelevantFixes && _lastResult.Hardware != null && !fix.IsApplicableTo(_lastResult.Hardware))
            {
                _onlyHardwareRelevantFixes = false;
                rerender = true;
            }
            Border card = FindFixCard(fix.Id);
            if (rerender || card == null)
            {
                RenderFixesCards(_lastResult.Fixes);
                card = FindFixCard(fix.Id);
            }
            if (card == null) return;

            HighlightFixCard(card, fix);
            ScrollFixCardIntoView(card);
            // Once more after the page fade-in / first layout pass, when the sizes are final.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_activeTab == TabFixes && _highlightedFixCard == card) ScrollFixCardIntoView(card);
            }));
        }

        /// <summary>Called at the end of RenderFixesCards: applies an OpenFix requested before the first scan.</summary>
        private void ApplyPendingFixFocus()
        {
            string id = _pendingFixFocusId;
            if (id == null) return;
            _pendingFixFocusId = null;
            if (_activeTab != TabFixes) return; // the user went elsewhere meanwhile
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => OpenFix(id)));
        }

        private Border FindFixCard(string fixId)
        {
            if (_fixesListPanel == null) return null;
            return _fixesListPanel.Children.OfType<Border>()
                .FirstOrDefault(b => string.Equals(b.Tag as string, fixId, StringComparison.OrdinalIgnoreCase));
        }

        private void ScrollFixCardIntoView(Border card)
        {
            ScrollViewer sv = FindScrollParent(_fixesListPanel);
            if (sv == null) { card.BringIntoView(); return; }
            sv.UpdateLayout();
            try
            {
                // Card top a little below the viewport top (BringIntoView would stop at the bottom edge).
                Point top = card.TransformToAncestor(sv).Transform(new Point(0, 0));
                sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + top.Y - 12));
                sv.UpdateLayout();
            }
            catch (InvalidOperationException) { card.BringIntoView(); } // not connected yet
        }

        private void HighlightFixCard(Border card, RegistryFixItem fix)
        {
            ClearFixHighlight();
            _highlightedFixCard = card;
            _highlightedFix = fix;
            _highlightedFixPadding = card.Padding;
            Thickness p = card.Padding;
            // 2 px accent border; padding shrinks by 1 px so the content does not move.
            card.Padding = new Thickness(Math.Max(0, p.Left - 1), Math.Max(0, p.Top - 1), Math.Max(0, p.Right - 1), Math.Max(0, p.Bottom - 1));
            card.BorderThickness = new Thickness(2);
            card.SetResourceReference(Border.BorderBrushProperty, "Accent");
            card.SetResourceReference(Border.BackgroundProperty, "AccentSoftBg");

            if (_fixHighlightTimer == null)
            {
                _fixHighlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
                _fixHighlightTimer.Tick += (s, e) => ClearFixHighlight();
            }
            _fixHighlightTimer.Stop();
            _fixHighlightTimer.Start();
        }

        private void ClearFixHighlight()
        {
            if (_fixHighlightTimer != null) _fixHighlightTimer.Stop();
            Border card = _highlightedFixCard;
            RegistryFixItem fix = _highlightedFix;
            _highlightedFixCard = null;
            _highlightedFix = null;
            if (card == null || fix == null) return;
            card.Padding = _highlightedFixPadding;
            card.BorderThickness = new Thickness(1);
            card.SetResourceReference(Border.BorderBrushProperty, FixCardBorderKey(fix));
            card.SetResourceReference(Border.BackgroundProperty, "BgCard");
        }

        // ==================== CAPTURE SELF-CHECK ====================

        /// <summary>
        /// Capture harness only: runs every nav: command and tray alert action on the live window, checks
        /// where it landed (tab, sub-tab, stress plan, visible + highlighted fix card, selected crash), writes
        /// nav_selftest.txt, then restores the navigation state it touched. Returns true when all checks pass.
        /// </summary>
        public bool RunNavSelfCheck(string outFile)
        {
            var sb = new StringBuilder();
            int fails = 0;
            Action<bool, string> check = (ok, label) =>
            {
                if (!ok) fails++;
                sb.AppendLine((ok ? "OK    " : "ECHEC ") + label);
            };

            FixGroup savedGroup = _fixGroupFilter;
            bool savedHwFilter = _onlyHardwareRelevantFixes;
            StressPlanKind savedPlan = _stressPlan;
            int savedTab = _activeTab, savedSub = _healthSubTab;

            try
            {
                UpdateLayout();

                // Stress plans
                bool running = _stressRunner.IsRunning;
                check(TryNavigate("nav:stress_ram", false) && _activeTab == TabHardware && _healthSubTab == 2
                      && (running || (_stressPlan == StressPlanKind.Ram && _cbStressPlan.SelectedIndex == (int)StressPlanKind.Ram)),
                      "nav:stress_ram -> Matériel › Test de stabilité, plan RAM");
                check(TryNavigate("nav:stress_cpu", false) && _activeTab == TabHardware && _healthSubTab == 2
                      && (running || (_stressPlan == StressPlanKind.CpuAllCore && _cbStressPlan.SelectedIndex == (int)StressPlanKind.CpuAllCore)),
                      "nav:stress_cpu -> Matériel › Test de stabilité, plan CPU");
                check(TryNavigate("nav:hardware", false) && _activeTab == TabHardware && _healthSubTab == 0, "nav:hardware -> Matériel › Matériel & BIOS");

                // Fix cards (the three tray targets + one hidden by both filters when this PC has one)
                foreach (string id in new[] { "tdr", "nvme_hmb", "pagefile_auto" })
                    check(CheckFixLanding("nav:fixes:" + id, id, null), "nav:fixes:" + id + " -> carte visible et surlignée");

                RegistryFixItem hidden = _lastResult != null && _lastResult.Fixes != null && _lastResult.Hardware != null
                    ? _lastResult.Fixes.FirstOrDefault(f => !f.IsApplicableTo(_lastResult.Hardware))
                    : null;
                if (hidden != null)
                {
                    FixGroup other = new[] { FixGroup.Gpu, FixGroup.BrowserApps, FixGroup.StoragePcie, FixGroup.SystemMaint }
                        .First(g => g != hidden.Group);
                    check(CheckFixLanding("nav:fixes:" + hidden.Id, hidden.Id, () =>
                    {
                        _fixGroupFilter = other;
                        _onlyHardwareRelevantFixes = true;
                        UpdateFixGroupStyles();
                        RenderFixesCards(_lastResult.Fixes);
                    }) && _fixGroupFilter == FixGroup.All && !_onlyHardwareRelevantFixes,
                    "nav:fixes:" + hidden.Id + " (masqué par les deux filtres) -> filtres levés, carte visible");
                }
                else sb.AppendLine("SKIP  aucun correctif masqué par le filtre matériel sur ce PC");

                // Crash
                CrashEntry crash = _crashEntries.FirstOrDefault();
                if (crash != null)
                    check(TryNavigate("nav:crash:" + crash.Key, false) && _activeTab == TabCrashes && _selectedCrashKey == crash.Key,
                          "nav:crash:<clé> -> Crashs, crash sélectionné");
                else sb.AppendLine("SKIP  aucun crash sur ce PC pour nav:crash:<clé>");

                // Unknown nav: ids are swallowed (never handed to the shell) and change nothing.
                SwitchTab(TabHome);
                check(TryNavigate("nav:does_not_exist", false) && _activeTab == TabHome, "nav:does_not_exist -> ignoré, pas de Process.Start");
                check(TryNavigate("nav:crash:", false) && _activeTab == TabHome, "nav:crash: (sans clé) -> ignoré");
                check(!TryNavigate("ms-settings:windowsupdate", false), "ms-settings:… -> pas une commande nav:");

                // Tray alerts: classification, then the click action itself.
                var samples = new[]
                {
                    Tuple.Create("nvlddmkm", 153, TraySentinel.LiveAlert.Gpu),
                    Tuple.Create("nvlddmkm", 14, TraySentinel.LiveAlert.Gpu),
                    Tuple.Create("amdkmdag", 13, TraySentinel.LiveAlert.Gpu),
                    Tuple.Create("Display", 4101, TraySentinel.LiveAlert.Gpu),
                    Tuple.Create("Microsoft-Windows-WHEA-Logger", 18, TraySentinel.LiveAlert.Whea),
                    Tuple.Create("stornvme", 129, TraySentinel.LiveAlert.Nvme),
                    Tuple.Create("Microsoft-Windows-Resource-Exhaustion-Detector", 2004, TraySentinel.LiveAlert.Commit),
                    Tuple.Create("disk", 153, TraySentinel.LiveAlert.None),
                };
                foreach (var s in samples)
                    check(TraySentinel.Classify(s.Item1, s.Item2) == s.Item3, "alerte " + s.Item1 + "/" + s.Item2 + " -> " + s.Item3);

                foreach (TraySentinel.LiveAlert kind in new[] { TraySentinel.LiveAlert.Gpu, TraySentinel.LiveAlert.Nvme, TraySentinel.LiveAlert.Commit, TraySentinel.LiveAlert.Whea })
                {
                    SwitchTab(TabHome);
                    Action click = _traySentinel != null ? _traySentinel.ClickActionFor(kind) : null;
                    if (click == null) { check(false, "clic alerte " + kind + " -> aucune action"); continue; }
                    click();
                    UpdateLayout();
                    bool ok;
                    switch (kind)
                    {
                        case TraySentinel.LiveAlert.Gpu: ok = IsFixFocused("tdr"); break;
                        case TraySentinel.LiveAlert.Nvme: ok = IsFixFocused("nvme_hmb"); break;
                        case TraySentinel.LiveAlert.Commit: ok = IsFixFocused("pagefile_auto"); break;
                        default: ok = _activeTab == TabHardware && _healthSubTab == 0; break;
                    }
                    check(ok, "clic alerte " + kind + " -> " + TraySentinel.NavCommandFor(kind));
                }
            }
            catch (Exception ex)
            {
                fails++;
                sb.AppendLine("ECHEC exception: " + ex);
            }
            finally
            {
                // Leave the window as the capture harness had it.
                ClearFixHighlight();
                _fixGroupFilter = savedGroup;
                _onlyHardwareRelevantFixes = savedHwFilter;
                UpdateFixGroupStyles();
                if (_lastResult != null && _lastResult.Fixes != null) RenderFixesCards(_lastResult.Fixes);
                if (!_stressRunner.IsRunning && _cbStressPlan != null && (int)savedPlan < _cbStressPlan.Items.Count)
                {
                    _stressPlan = savedPlan;
                    _cbStressPlan.SelectedIndex = (int)savedPlan;
                }
                SwitchHealthSubTab(savedSub);
                SwitchTab(savedTab);
            }

            sb.AppendLine(fails == 0 ? "NAVIGATION : TOUT EST CONFORME" : "NAVIGATION : " + fails + " ECHEC(S)");
            try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Warn(ex, "nav self-check output"); }
            return fails == 0;
        }

        private bool CheckFixLanding(string cmd, string fixId, Action hideFirst)
        {
            SwitchTab(TabHome);
            if (hideFirst != null) hideFirst();
            if (!TryNavigate(cmd, false)) return false;
            UpdateLayout();
            return IsFixFocused(fixId);
        }

        /// <summary>Fixes tab shown, card rendered, highlighted and inside the scroll viewport.</summary>
        private bool IsFixFocused(string fixId)
        {
            if (_activeTab != TabFixes) return false;
            Border card = FindFixCard(fixId);
            if (card == null || card != _highlightedFixCard) return false;
            ScrollViewer sv = FindScrollParent(_fixesListPanel);
            if (sv == null) return false;
            sv.UpdateLayout();
            Point top = card.TransformToAncestor(sv).Transform(new Point(0, 0));
            return top.Y >= -1 && top.Y < sv.ViewportHeight;
        }
    }
}
