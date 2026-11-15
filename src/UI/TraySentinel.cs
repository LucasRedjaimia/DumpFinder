using System;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WinCrashDoctor
{
    public class TraySentinel : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private EventLogWatcher _watcher;
        private ToolStripMenuItem _miOpen;
        private ToolStripMenuItem _miScan;
        private ToolStripMenuItem _miExit;
        private readonly Action _onOpenRequested;
        private readonly Action _onRescanRequested;
        private readonly Action _onExitRequested;
        /// <summary>Runs an in-app "nav:" command (brings the window back first); null = just open the window.</summary>
        private readonly Action<string> _onNavigate;
        private ContextMenuStrip _menu;
        private DateTime _lastAlertTime = DateTime.MinValue;
        /// <summary>What a click on the current notification does (falls back to opening the window).</summary>
        private Action _pendingClick;
        private bool _minimizedHintShown;
        private volatile bool _disposed;
        /// <summary>UI thread dispatcher: NotifyIcon must only be touched from the thread that created it.</summary>
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public bool IsEnabled { get; private set; }

        /// <summary>Create on the UI thread (the dispatcher is captured here).</summary>
        public TraySentinel(Action onOpen, Action onRescan, Action onExit, Action<string> onNavigate = null)
        {
            _onOpenRequested = onOpen;
            _onRescanRequested = onRescan;
            _onExitRequested = onExit;
            _onNavigate = onNavigate;
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        }

        public void SetEnabled(bool enabled)
        {
            if (_disposed || enabled == IsEnabled) return;
            IsEnabled = enabled;

            if (enabled)
            {
                EnsureTrayCreated();
                _notifyIcon.Visible = true;
                StartEventWatcher();
            }
            else
            {
                StopEventWatcher();
                if (_notifyIcon != null)
                    _notifyIcon.Visible = false;
            }
        }

        public void RefreshMenuLocalization()
        {
            if (_miOpen == null) return;
            _miOpen.Text = Localizer.S("tray_sentinel.open_dump_finder");
            _miScan.Text = Localizer.S("tray_sentinel.rescan_system");
            _miExit.Text = Localizer.S("tray_sentinel.quit");
            if (_notifyIcon != null)
                _notifyIcon.Text = "Dump Finder — " + Localizer.S("tray_sentinel.sentinel_active");
        }

        /// <summary>
        /// Windows 10/11 shows tray balloon tips as native toast notifications (Action Center).
        /// <paramref name="onClick"/> runs when the user clicks it.
        /// </summary>
        public void Notify(string title, string message, bool warning, Action onClick)
        {
            if (_disposed || _notifyIcon == null || !_notifyIcon.Visible) return;
            _pendingClick = onClick;
            _notifyIcon.ShowBalloonTip(8000, title, message, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        public void ShowMinimizedHintOnce()
        {
            if (_disposed || _minimizedHintShown || _notifyIcon == null || !_notifyIcon.Visible) return;
            _minimizedHintShown = true;
            _notifyIcon.ShowBalloonTip(
                2800,
                "Dump Finder",
                Localizer.S("tray_sentinel.sentinel_active_in_the_background_0"),
                ToolTipIcon.Info);
        }

        private void EnsureTrayCreated()
        {
            if (_notifyIcon != null) return;

            Icon appIcon = SystemIcons.Shield;
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                Icon extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null) appIcon = extracted;
            }
            catch (Exception ex) { Log.Warn(ex); }

            ContextMenuStrip menu = new ContextMenuStrip();
            _menu = menu;
            _miOpen = new ToolStripMenuItem();
            _miOpen.Font = new Font(_miOpen.Font, FontStyle.Bold);
            _miOpen.Click += (s, e) => { if (_onOpenRequested != null) _onOpenRequested(); };

            _miScan = new ToolStripMenuItem();
            _miScan.Click += (s, e) => { if (_onRescanRequested != null) _onRescanRequested(); };

            _miExit = new ToolStripMenuItem();
            _miExit.Click += (s, e) => { if (_onExitRequested != null) _onExitRequested(); };

            menu.Items.Add(_miOpen);
            menu.Items.Add(_miScan);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miExit);

            _notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                ContextMenuStrip = menu,
                Visible = false
            };
            _notifyIcon.DoubleClick += (s, e) => { if (_onOpenRequested != null) _onOpenRequested(); };
            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                var a = _pendingClick ?? _onOpenRequested;
                _pendingClick = null;
                if (a != null) a();
            };
            _notifyIcon.BalloonTipClosed += (s, e) => _pendingClick = null;

            RefreshMenuLocalization();
        }

        /// <summary>Live System-log events the sentinel alerts on.</summary>
        public enum LiveAlert { None, Gpu, Whea, Nvme, Commit }

        /// <summary>Which alert a watched System event raises (None = no alert).</summary>
        internal static LiveAlert Classify(string provider, int id)
        {
            string prov = provider ?? "";
            if (prov.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prov.IndexOf("amdwddmg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prov.IndexOf("amdkmdag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (prov.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101))
                return LiveAlert.Gpu;
            if (prov.IndexOf("WHEA", StringComparison.OrdinalIgnoreCase) >= 0) return LiveAlert.Whea;
            if (prov.IndexOf("stornvme", StringComparison.OrdinalIgnoreCase) >= 0) return LiveAlert.Nvme;
            if (id == 2004) return LiveAlert.Commit;
            return LiveAlert.None;
        }

        /// <summary>Where a click on the alert leads; the alert texts promise exactly this.</summary>
        internal static string NavCommandFor(LiveAlert kind)
        {
            switch (kind)
            {
                case LiveAlert.Gpu: return "nav:fixes:tdr";              // display driver timeout -> TDR fix
                case LiveAlert.Nvme: return "nav:fixes:nvme_hmb";        // controller reset -> HMB fix
                case LiveAlert.Commit: return "nav:fixes:pagefile_auto"; // commit exhaustion -> pagefile fix
                case LiveAlert.Whea: return "nav:hardware";              // hardware error -> Hardware page
                default: return null;
            }
        }

        /// <summary>Click action of a live alert (null: the click just opens the window).</summary>
        internal Action ClickActionFor(LiveAlert kind)
        {
            string cmd = NavCommandFor(kind);
            if (cmd == null || _onNavigate == null) return null;
            return () => _onNavigate(cmd);
        }

        private void StartEventWatcher()
        {
            StopEventWatcher();
            try
            {
                string xpath = "*[System[(EventID=4101 or EventID=153 or EventID=14 or EventID=13 or EventID=17 or EventID=18 or EventID=19 or EventID=129 or EventID=2004)]]";
                EventLogQuery q = new EventLogQuery("System", PathType.LogName, xpath);
                _watcher = new EventLogWatcher(q);
                _watcher.EventRecordWritten += OnSystemEventWritten;
                _watcher.Enabled = true;
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private void StopEventWatcher()
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.Enabled = false;
                    _watcher.Dispose();
                }
                catch (Exception ex) { Log.Warn(ex); }
                _watcher = null;
            }
        }

        private void OnSystemEventWritten(object sender, EventRecordWrittenEventArgs e)
        {
            // Runs on an EventLogWatcher thread-pool thread: only read the record here, then
            // marshal the NotifyIcon work to the UI dispatcher.
            try
            {
                if (_disposed || e.EventRecord == null) return;

                string prov = e.EventRecord.ProviderName ?? "";
                int id = e.EventRecord.Id;

                string title = "";
                string msg = "";
                LiveAlert kind = Classify(prov, id);

                switch (kind)
                {
                    case LiveAlert.Gpu:
                        title = Localizer.F("tray_sentinel.gpu_fault_detected", prov);
                        msg = Localizer.F("tray_sentinel.your_graphics_driver_just_experienced_a", id);
                        break;
                    case LiveAlert.Whea:
                        title = Localizer.F("tray_sentinel.whea_hardware_error_id", id);
                        msg = Localizer.S("tray_sentinel.live_cpu_or_pcie_hardware_error");
                        break;
                    case LiveAlert.Nvme:
                        title = Localizer.F("tray_sentinel.nvme_ssd_timeout_stornvme", id);
                        msg = Localizer.S("tray_sentinel.your_nvme_ssd_controller_just_reset");
                        break;
                    case LiveAlert.Commit:
                        title = Localizer.S("tray_sentinel.virtual_memory_exhaustion");
                        msg = Localizer.S("tray_sentinel.windows_ran_low_on_commit_memory");
                        break;
                }

                if (!string.IsNullOrEmpty(title))
                {
                    string t = title, m = msg;
                    _dispatcher.BeginInvoke(new Action(() => ShowAlert(t, m, kind)));
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        /// <summary>UI thread: throttled live alert balloon; a click runs that alert's navigation.</summary>
        private void ShowAlert(string title, string msg, LiveAlert kind)
        {
            try
            {
                if (_disposed || _notifyIcon == null || !_notifyIcon.Visible) return;
                if ((DateTime.Now - _lastAlertTime).TotalSeconds < 6) return;
                _lastAlertTime = DateTime.Now;
                _pendingClick = ClickActionFor(kind);
                _notifyIcon.ShowBalloonTip(6000, title, msg, ToolTipIcon.Warning);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopEventWatcher();
            _pendingClick = null;
            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.ContextMenuStrip = null;
                    _notifyIcon.Dispose();
                }
                catch (Exception ex) { Log.Warn(ex); }
                _notifyIcon = null;
            }
            if (_menu != null)
            {
                try { _menu.Dispose(); }
                catch (Exception ex) { Log.Warn(ex); }
                _menu = null;
            }
        }
    }
}
