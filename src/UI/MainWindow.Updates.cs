using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinCrashDoctor
{
    /// <summary>
    /// "New version available" card: checks the GitHub releases at launch (and on demand from Settings) and shows
    /// the version, its release notes and a download link. Nothing is downloaded or installed by the app itself.
    /// </summary>
    public partial class MainWindow
    {
        private Grid _updateOverlay;
        private TextBlock _lblUpdKicker, _lblUpdTitle, _lblUpdSub;
        private StackPanel _updNotes;
        private Button _btnUpdDownload, _btnUpdLater, _btnUpdSkip, _btnUpdPage;
        private UpdateInfo _shownUpdate, _pendingUpdate;
        private bool _updateChecking, _updateInstalling;
        private TextBlock _lblUpdStatus;

        private Button _btnUpdToggle, _btnUpdCheckNow;
        private TextBlock _lblSetUpdTitle, _lblSetUpdDesc;

        // ==================== Overlay ====================
        private void BuildUpdateOverlay()
        {
            _updateOverlay = new Grid { Visibility = Visibility.Collapsed };
            Border scrim = new Border();
            scrim.SetResourceReference(Border.BackgroundProperty, "ScrimBg");
            _updateOverlay.Children.Add(scrim);

            Border card = ResBorder(ResBg(new Border
            {
                Width = 580,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(30, 26, 30, 22),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 14, Direction = 270, Opacity = 0.4, Color = Colors.Black }
            }, "BgCard"), "BorderStrong");

            StackPanel sp = new StackPanel();
            _lblUpdKicker = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold }, "AccentText");
            _lblUpdTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) }, "TextPrimary");
            _lblUpdSub = ResFg(new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) }, "TextSecondary");
            sp.Children.Add(_lblUpdKicker);
            sp.Children.Add(_lblUpdTitle);
            sp.Children.Add(_lblUpdSub);

            _updNotes = new StackPanel();
            Border notesBox = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Child = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _updNotes }
            }, "BgSubtle"), "BorderCard");
            sp.Children.Add(notesBox);

            // "View on GitHub" sits under the notes, on its own: in the button row the four items did not fit the
            // 580 px card in French ("Mettre à jour vers 6.5.2" was cut off on the right).
            _btnUpdPage = CreateFlatButton("", "BgCard", "AccentText", "BgCard", new Thickness(0, 4, 0, 4), 12.5);
            _btnUpdPage.BorderThickness = new Thickness(0);
            _btnUpdPage.HorizontalAlignment = HorizontalAlignment.Left;
            _btnUpdPage.Margin = new Thickness(0, 8, 0, 0);
            _btnUpdPage.Click += (s, e) => { if (_shownUpdate != null) OpenRepoUrl(_shownUpdate.PageUrl); };
            sp.Children.Add(_btnUpdPage);

            _lblUpdStatus = ResFg(new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed }, "TextSecondary");
            sp.Children.Add(_lblUpdStatus);

            // Buttons on the right; in a longer language they move to a second line instead of being cut off.
            WrapPanel btns = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            _btnUpdSkip = CreateFlatButton("", "BgCard", "TextSecondary", "BgCard", new Thickness(12, 7, 12, 7), 13);
            _btnUpdSkip.BorderThickness = new Thickness(0);
            _btnUpdSkip.Margin = new Thickness(0, 4, 0, 0);
            _btnUpdSkip.Click += (s, e) => SkipShownUpdate();
            _btnUpdLater = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnUpdLater.Margin = new Thickness(6, 4, 0, 0);
            _btnUpdLater.Click += (s, e) => HideUpdate();
            _btnUpdDownload = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(18, 7, 18, 7), 13);
            _btnUpdDownload.Margin = new Thickness(8, 4, 0, 0);
            _btnUpdDownload.Click += async (s, e) =>
            {
                if (_shownUpdate == null) return;
                if (SelfUpdate.CannotInstall(_shownUpdate) == null) await InstallShownUpdateAsync();
                else { OpenRepoUrl(_shownUpdate.DownloadUrl); HideUpdate(); }
            };
            btns.Children.Add(_btnUpdSkip);
            btns.Children.Add(_btnUpdLater);
            btns.Children.Add(_btnUpdDownload);
            sp.Children.Add(btns);

            card.Child = sp;
            _updateOverlay.Children.Add(card);
            _updateOverlay.KeyDown += (s, e) => { if (e.Key == Key.Escape) { HideUpdate(); e.Handled = true; } };
        }

        private void ShowUpdate(UpdateInfo u)
        {
            if (u == null || _updateOverlay == null) return;
            if (_onboardingActive) { _pendingUpdate = u; return; } // shown once the welcome screens are closed
            _shownUpdate = u;
            string ver = u.Version.ToString(3);

            _lblUpdKicker.Text = Localizer.S("updates.new_version_available");
            _lblUpdTitle.Text = "Dump Finder " + ver;
            string date = u.PublishedAt.HasValue ? u.PublishedAt.Value.ToLocalTime().ToString("d", Localizer.Culture) : null;
            _lblUpdSub.Text = Localizer.S("updates.you_are_using_version") + UpdateChecker.CurrentVersionText +
                (date != null ? Localizer.S("updates.released_on") + date : "");

            RenderReleaseNotes(u.Notes);

            // In place when the release has a checkable exe; otherwise (older releases) the download page as before.
            _btnUpdDownload.Content = SelfUpdate.CannotInstall(u) == null ? Localizer.F("self_update.install", ver) : Localizer.S("updates.download") + ver;
            _lblUpdStatus.Visibility = Visibility.Collapsed;
            SetUpdateButtonsEnabled(true);
            _btnUpdLater.Content = Localizer.S("updates.later");
            _btnUpdSkip.Content = Localizer.S("updates.skip_this_version");
            _btnUpdPage.Content = Localizer.S("updates.view_on_github");

            _updateOverlay.Visibility = Visibility.Visible;
            _btnUpdDownload.Focus();
        }

        private void HideUpdate()
        {
            if (_updateInstalling) return;          // the card stays until the install ends (restart or error)
            if (_updateOverlay != null) _updateOverlay.Visibility = Visibility.Collapsed;
            _shownUpdate = null;
        }

        private void SetUpdateButtonsEnabled(bool on)
        {
            foreach (Button b in new[] { _btnUpdDownload, _btnUpdLater, _btnUpdSkip, _btnUpdPage }) if (b != null) b.IsEnabled = on;
        }

        private void SetUpdateStatus(string text, string fgKey)
        {
            _lblUpdStatus.Text = text;
            _lblUpdStatus.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
            _lblUpdStatus.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Downloads and checks the new exe, swaps it in place of this one and restarts. Refused while a change or a
        /// stress test runs (restarting would cut it). On failure the current version stays, with the reason and the
        /// download page as a fallback.
        /// </summary>
        private async Task InstallShownUpdateAsync()
        {
            UpdateInfo u = _shownUpdate;
            if (u == null || _updateInstalling) return;
            if (_fixInProgress || _stressRunner.IsRunning)
            {
                SetUpdateStatus(Localizer.S("self_update.busy"), "WarnFg");
                return;
            }
            _updateInstalling = true;
            SetUpdateButtonsEnabled(false);
            string ver = u.Version.ToString(3);
            try
            {
                var progress = new Progress<double>(p => SetUpdateStatus(Localizer.F("self_update.downloading", ver, (int)Math.Round(p * 100)), "TextSecondary"));
                SetUpdateStatus(Localizer.F("self_update.downloading", ver, 0), "TextSecondary");
                string file;
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                    file = await SelfUpdate.DownloadAsync(u, progress, cts.Token);
                SetUpdateStatus(Localizer.F("self_update.restarting", ver), "SuccessFg");
                string exe = SelfUpdate.ExePath;
                string old = SelfUpdate.Swap(file, exe);
                Log.Info("Updated " + UpdateChecker.CurrentVersionText + " → " + ver + " (previous exe: " + old + ")");
                // Other processes of the replaced exe (the tray Sentinel started at logon, a second window) still run
                // the old version: end them so only the new one is left.
                int others = await Task.Run(() => RunningCopies.Close(RunningCopies.From(RunningCopies.Find(), exe, old)));
                if (others > 0) Log.Info("Closed " + others + " other process(es) of the previous version");
                await Task.Delay(400);                  // let the status show
                SelfUpdate.Restart(exe, u.Version);
                _forceExit = true;
                RunExitCleanup();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                var ue = ex as UpdateException;
                Log.Warn(ex, "in-app update" + (ue != null ? ": " + ue.Detail : ""));
                _updateInstalling = false;
                SetUpdateButtonsEnabled(true);
                SetUpdateStatus((ue != null ? ue.Message : Localizer.S("self_update.failed_download")) + " " + Localizer.S("self_update.fallback"), "WarnFg");
                _btnUpdDownload.Content = Localizer.S("updates.download") + ver;      // next click: the download page
                _shownUpdate = new UpdateInfo { Version = u.Version, Tag = u.Tag, Title = u.Title, Notes = u.Notes, DownloadUrl = u.DownloadUrl, PageUrl = u.PageUrl, PublishedAt = u.PublishedAt };
            }
        }

        /// <summary>
        /// At launch: an older Dump Finder still running (typically the previous version in the tray after a manual
        /// update) is offered to be closed, so only this version is left.
        /// </summary>
        public async void OfferToCloseOlderCopies()
        {
            List<RunningCopy> older;
            try { older = await Task.Run(() => RunningCopies.OlderThan(RunningCopies.Find(), UpdateChecker.CurrentVersion)); }
            catch (Exception ex) { Log.Warn(ex, "older copies"); return; }
            if (older.Count == 0) return;
            string versions = string.Join(", ", older.Select(c => c.Version.ToString(3)).Distinct());
            string msg = Localizer.F("running_copies.older_running", versions, UpdateChecker.CurrentVersionText);
            if (MessageBox.Show(this, msg, "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
            int closed = await Task.Run(() => RunningCopies.Close(older));
            SetToast(closed == older.Count ? Localizer.F("running_copies.closed", versions) : Localizer.S("running_copies.not_closed"), closed == older.Count ? "SuccessFg" : "WarnFg");
        }

        /// <summary>First launch after an in-app update.</summary>
        public void ShowUpdatedToast(string version)
        {
            Version v = UpdateChecker.ParseVersion(version);
            if (v != null && v == UpdateChecker.CurrentVersion) SetToast(Localizer.F("self_update.updated", UpdateChecker.CurrentVersionText), "SuccessFg");
        }

        private void SkipShownUpdate()
        {
            if (_shownUpdate != null && _appSettings != null)
            {
                _appSettings.SkippedUpdateVersion = _shownUpdate.Version.ToString(3);
                Localizer.SaveFullSettings(_appSettings);
                SetToast(Localizer.S("updates.this_version_won_t_be_offered"), "TextSecondary");
            }
            HideUpdate();
        }

        /// <summary>Release notes are Markdown: headings, bullets, bold and links are turned into plain readable text.</summary>
        private void RenderReleaseNotes(string md)
        {
            _updNotes.Children.Clear();
            if (string.IsNullOrWhiteSpace(md))
            {
                _updNotes.Children.Add(ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = Localizer.S("updates.no_description_for_this_release") }, "TextSecondary"));
                return;
            }
            bool firstLine = true;
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) { if (!firstLine) _updNotes.Children.Add(new Border { Height = 6 }); continue; }
                string t = line.TrimStart();
                bool heading = t.StartsWith("#");
                bool bullet = Regex.IsMatch(t, @"^[-*+]\s+");
                int indent = bullet ? (line.Length - t.Length) / 2 : 0;
                if (heading) t = t.TrimStart('#').Trim();
                if (bullet) t = Regex.Replace(t, @"^[-*+]\s+", "");
                t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]+\)", "$1");      // [text](url) → text
                t = Regex.Replace(t, @"(\*\*|__|`)", "");                   // bold / code markers
                var tb = ResFg(new TextBlock
                {
                    Text = (bullet ? "•  " : "") + t,
                    FontSize = heading ? 14 : 13,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19,
                    Margin = new Thickness(indent * 16 + (bullet ? 2 : 0), heading && !firstLine ? 6 : 0, 0, 2)
                }, heading ? "TextPrimary" : "TextSecondary");
                _updNotes.Children.Add(tb);
                firstLine = false;
            }
        }

        private void OpenRepoUrl(string url)
        {
            if (!UpdateChecker.IsOwnUrl(url)) url = UpdateChecker.RepoUrl + "/releases/latest";
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn(ex, url); }
        }

        /// <summary>Screenshot harness: the card with sample release notes (nothing is fetched).</summary>
        public void ShowUpdateForCapture()
        {
            ShowUpdate(new UpdateInfo
            {
                Version = new Version(UpdateChecker.CurrentVersion.Major, UpdateChecker.CurrentVersion.Minor + 1, 0),
                PublishedAt = DateTime.Now,
                Notes = "## What's new\n- **Optimise** tab: settings, apps, startup and cleanup\n- CPU temperature during the stress test\n\n## Fixes\n- The HMB advice no longer shows outside Windows 11 24H2\n- \"It's already the latest version\" button on the BIOS warning",
                DownloadUrl = UpdateChecker.RepoUrl + "/releases/latest",
                PageUrl = UpdateChecker.RepoUrl + "/releases/latest",
                // Installable from the app, so the card shows its real button ("Update to x"), the longest one.
                ExeUrl = UpdateChecker.RepoUrl + "/releases/latest",
                ExeSha256 = new string('0', 64),
                ExeSize = 1
            });
        }

        // ==================== Checks ====================
        /// <summary>Launch-time check, a few seconds after the window opens, unless turned off in Settings.</summary>
        private async void StartupUpdateCheck()
        {
            if (Localizer.ReadOnlySettings || (_appSettings != null && _appSettings.UpdateCheckDisabled)) return;
            await Task.Delay(3000);
            UpdateInfo u = await RunUpdateCheckAsync();
            if (u == null) return;
            if (_appSettings != null && _appSettings.SkippedUpdateVersion == u.Version.ToString(3)) return;
            if (_appSettings != null && !_appSettings.HasCompletedTour) { _pendingUpdate = u; return; } // after the welcome screens
            ShowUpdate(u);
        }

        private async Task<UpdateInfo> RunUpdateCheckAsync()
        {
            if (_updateChecking) return null;
            _updateChecking = true;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    return await UpdateChecker.CheckAsync(cts.Token);
            }
            finally { _updateChecking = false; }
        }

        private async void CheckForUpdatesNow()
        {
            if (_btnUpdCheckNow != null) _btnUpdCheckNow.IsEnabled = false;
            try
            {
                UpdateInfo latest;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    latest = await UpdateChecker.GetLatestAsync(cts.Token);
                if (latest == null)
                    SetToast(Localizer.S("updates.could_not_reach_github_right_now"), "WarnFg");
                else if (latest.Version > UpdateChecker.CurrentVersion)
                    ShowUpdate(latest); // on demand: shown even if this version was skipped
                else
                    SetToast(Localizer.S("updates.you_already_have_the_latest_version") + UpdateChecker.CurrentVersionText + ").", "SuccessFg");
            }
            finally { if (_btnUpdCheckNow != null) _btnUpdCheckNow.IsEnabled = true; }
        }

        // ==================== Settings row ====================
        private UIElement BuildUpdateSettingsRow()
        {
            _btnUpdToggle = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12);
            _btnUpdToggle.MinWidth = 120;
            _btnUpdToggle.Click += (s, e) =>
            {
                if (_appSettings == null) return;
                _appSettings.UpdateCheckDisabled = !_appSettings.UpdateCheckDisabled;
                Localizer.SaveFullSettings(_appSettings);
                RefreshUpdateSettingsVisual();
            };
            _btnUpdCheckNow = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
            _btnUpdCheckNow.Margin = new Thickness(8, 0, 0, 0);
            _btnUpdCheckNow.Click += (s, e) => CheckForUpdatesNow();
            StackPanel ctl = new StackPanel { Orientation = Orientation.Horizontal };
            ctl.Children.Add(_btnUpdToggle);
            ctl.Children.Add(_btnUpdCheckNow);
            return SettingRow(Glyph.Refresh, out _lblSetUpdTitle, out _lblSetUpdDesc, ctl, false);
        }

        private void RefreshUpdateSettingsVisual()
        {
            if (_btnUpdToggle == null) return;
            bool on = _appSettings == null || !_appSettings.UpdateCheckDisabled;
            _btnUpdToggle.Content = on
                ? Localizer.S("sentinel.on")
                : Localizer.S("sentinel.off");
            _btnUpdToggle.SetResourceReference(Button.BackgroundProperty, on ? "SuccessSoftBg" : "BgCard");
            _btnUpdToggle.SetResourceReference(Button.ForegroundProperty, on ? "SuccessFg" : "TextSecondary");
            _btnUpdToggle.SetResourceReference(Button.BorderBrushProperty, on ? "SuccessBorder" : "BorderCard");
            _btnUpdCheckNow.Content = Localizer.S("updates.check_now");
            _lblSetUpdTitle.Text = Localizer.S("updates.updates");
            _lblSetUpdDesc.Text = Localizer.F("updates.version_at_launch_the_app_checks", UpdateChecker.CurrentVersionText);
        }
    }
}
