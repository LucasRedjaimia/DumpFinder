using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WinCrashDoctor
{
    /// <summary>Sentinel 2.0: start with Windows in the tray, and a "you crashed while away" alert.</summary>
    public partial class MainWindow
    {
        private Button _btnAutostart;
        private TextBlock _lblSetAutostartTitle, _lblSetAutostartDesc;

        /// <summary>Logon start: no window, tray only, then a background check for crashes since last time.</summary>
        public async void StartInTray()
        {
            await Task.Delay(TimeSpan.FromSeconds(40)); // let the desktop settle after logon
            // The window was opened meanwhile and is scanning: it shows the results itself.
            if (_scanInProgress) return;
            _scanInProgress = true;
            try
            {
                ScanResult res;
                try
                {
                    res = await Task.Run(() => DiagnosticsEngine.RunScan(DateTime.Now.AddDays(-Stability.WindowDays)));
                }
                finally
                {
                    _scanInProgress = false;
                }
                _lastResult = res;
                RenderScanResult(res);

                DateTime now = DateTime.Now;
                var fresh = History.PeekNewCrashes(_crashEntries.Where(c => (now - c.Time).TotalDays <= Stability.WindowDays).Select(c => c.Key));
                var severe = _crashEntries.Where(c => fresh.Contains(c.Key) && Stability.IsSevere(c.Category)).OrderByDescending(c => c.Time).ToList();
                if (severe.Count == 0 || _traySentinel == null) return;

                CrashEntry last = severe[0];
                string when = last.Time.Date == now.Date
                    ? Localizer.S("sentinel.today_at") + last.Time.ToString("HH:mm")
                    : last.Time.Date == now.Date.AddDays(-1)
                        ? Localizer.S("sentinel.yesterday_at") + last.Time.ToString("HH:mm")
                        : last.Time.ToString("g");
                string key = last.Key;
                _traySentinel.Notify(
                    Localizer.F("sentinel.your_pc_crashed", when),
                    last.Title + (severe.Count > 1 ? Localizer.F("sentinel.more", (severe.Count - 1)) : "")
                        + "\n" + Localizer.S("sentinel.click_to_see_the_analysis"),
                    true,
                    () => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Show();
                        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                        Activate();
                        OpenCrash(key);
                    })));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Tray startup check");
            }
            finally
            {
                OnScanFinished(); // a scan / relocalization requested while this one ran
            }
        }

        // ==================== Settings row ====================
        private UIElement BuildAutostartRow()
        {
            _btnAutostart = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12);
            _btnAutostart.MinWidth = 180;
            _btnAutostart.Click += (s, e) => ToggleAutostart();
            return SettingRow(Glyph.Play, out _lblSetAutostartTitle, out _lblSetAutostartDesc, _btnAutostart, false);
        }

        private void ToggleAutostart()
        {
            bool target = !Autostart.IsEnabled;
            if (target && !_sentinelEnabled) ToggleSentinel(); // starting with Windows only makes sense with the Sentinel on
            bool ok = Autostart.Set(target);
            RefreshAutostartVisual();
            SetToast(!ok
                    ? Localizer.S("sentinel.could_not_change_automatic_start")
                    : target
                        ? Localizer.S("sentinel.dump_finder_will_start_with_windows")
                        : Localizer.S("sentinel.automatic_start_disabled"),
                ok ? "SuccessFg" : "WarnFg");
        }

        private void RefreshAutostartVisual()
        {
            if (_btnAutostart == null) return;
            bool on = Autostart.IsEnabled;
            _btnAutostart.Content = on
                ? Localizer.S("sentinel.on")
                : Localizer.S("sentinel.off");
            _btnAutostart.SetResourceReference(Button.BackgroundProperty, on ? "SuccessSoftBg" : "BgCard");
            _btnAutostart.SetResourceReference(Button.ForegroundProperty, on ? "SuccessFg" : "TextSecondary");
            _btnAutostart.SetResourceReference(Button.BorderBrushProperty, on ? "SuccessBorder" : "BorderCard");
            _lblSetAutostartTitle.Text = Localizer.S("sentinel.start_with_windows");
            _lblSetAutostartDesc.Text = Localizer.S("sentinel.starts_the_sentinel_at_sign_in");
        }
    }
}
