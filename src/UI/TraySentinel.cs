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
        private DateTime _lastAlertTime = DateTime.MinValue;
        /// <summary>What a click on the current notification does (falls back to opening the window).</summary>
        private Action _pendingClick;

        public bool IsEnabled { get; private set; }

        public TraySentinel(Action onOpen, Action onRescan, Action onExit)
        {
            _onOpenRequested = onOpen;
            _onRescanRequested = onRescan;
            _onExitRequested = onExit;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled == IsEnabled) return;
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
            _miOpen.Text = Localizer.L("Ouvrir Dump Finder", "Open Dump Finder", "Abrir Dump Finder", "Dump Finder öffnen", "Apri Dump Finder", "Abrir Dump Finder");
            _miScan.Text = Localizer.L("Relancer l'analyse", "Rescan System", "Analizar sistema", "System prüfen", "Analizza sistema", "Analisar sistema");
            _miExit.Text = Localizer.L("Quitter", "Quit", "Salir", "Beenden", "Esci", "Sair");
            if (_notifyIcon != null)
                _notifyIcon.Text = "Dump Finder — " + Localizer.L("Sentinelle active", "Sentinel active", "Centinela activo", "Wächter aktiv", "Sentinella attiva", "Sentinela ativa");
        }

        /// <summary>
        /// Windows 10/11 shows tray balloon tips as native toast notifications (Action Center).
        /// <paramref name="onClick"/> runs when the user clicks it.
        /// </summary>
        public void Notify(string title, string message, bool warning, Action onClick)
        {
            if (_notifyIcon == null || !_notifyIcon.Visible) return;
            _pendingClick = onClick;
            _notifyIcon.ShowBalloonTip(8000, title, message, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        public void ShowMinimizedHintOnce()
        {
            if (_notifyIcon == null || !_notifyIcon.Visible) return;
            _notifyIcon.ShowBalloonTip(
                2800,
                "Dump Finder",
                Localizer.L(
                    "Sentinelle active en arrière-plan (0 % CPU). Vous serez alerté si un pilote ou composant décroche.",
                    "Sentinel active in the background (0% CPU). You will be notified if any driver or hardware fault occurs.",
                    "Centinela activo en segundo plano. Recibirá una alerta si falla algún controlador.",
                    "Wächter im Hintergrund aktiv. Sie werden bei Treiber- oder Hardwarefehlern benachrichtigt.",
                    "Sentinella attiva in background. Sarai avvisato in caso di errori driver o hardware.",
                    "Sentinela ativa em segundo plano. Você será avisado se ocorrer alguma falha."),
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
            try
            {
                if (e.EventRecord == null || _notifyIcon == null || !_notifyIcon.Visible) return;
                if ((DateTime.Now - _lastAlertTime).TotalSeconds < 6) return;

                string prov = e.EventRecord.ProviderName ?? "";
                int id = e.EventRecord.Id;

                string title = "";
                string msg = "";

                if (prov.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prov.IndexOf("amdwddmg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prov.IndexOf("amdkmdag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (prov.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101))
                {
                    title = Localizer.L("Alerte GPU détectée (" + prov + ")", "GPU Fault Detected (" + prov + ")", "Fallo GPU detectado", "GPU-Fehler erkannt", "Errore GPU rilevato", "Falha GPU detectada");
                    msg = Localizer.L("Le pilote graphique vient de subir un timeout (Event " + id + "). Cliquez pour ouvrir le diagnostic.", "Your graphics driver just experienced a timeout (Event " + id + "). Click to open diagnostics.", "El controlador gráfico sufrió un bloqueo. Haga clic para ver soluciones.", "Der Grafiktreiber hatte einen Timeout. Klicken Sie für Details.", "Il driver grafico ha subito un timeout. Clicca per i dettagli.", "O driver de vídeo sofreu um timeout. Clique para abrir.");
                }
                else if (prov.IndexOf("WHEA", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    title = "WHEA Hardware Error (ID " + id + ")";
                    msg = Localizer.L("Erreur matérielle CPU ou bus PCIe détectée en direct. Cliquez pour inspecter.", "Live CPU or PCIe hardware error detected. Click to inspect.", "Error de hardware CPU/PCIe detectado.", "CPU- oder PCIe-Hardwarefehler erkannt.", "Errore hardware CPU/PCIe rilevato.", "Erro de hardware CPU/PCIe detectado.");
                }
                else if (prov.IndexOf("stornvme", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    title = "NVMe SSD Timeout (stornvme " + id + ")";
                    msg = Localizer.L("Le contrôleur SSD NVMe vient de se réinitialiser. Cliquez pour appliquer le correctif HMB.", "Your NVMe SSD controller just reset. Click to apply the HMB fix.", "El controlador SSD NVMe se reinició.", "Der NVMe-SSD-Controller wurde zurückgesetzt.", "Il controller SSD NVMe si è resettato.", "O controlador SSD NVMe reiniciou.");
                }
                else if (id == 2004)
                {
                    title = Localizer.L("Saturation mémoire virtuelle", "Virtual Memory Exhaustion", "Memoria virtual agotada", "Virtueller Speicher erschöpft", "Memoria virtuale esaurita", "Memória virtual esgotada");
                    msg = Localizer.L("Windows manque de mémoire virtuelle (Pagefile). Cliquez pour vérifier la configuration.", "Windows ran low on commit memory (Pagefile). Click to check settings.", "Windows se quedó sin memoria virtual.", "Windows hat zu wenig Auslagerungsspeicher.", "Memoria virtuale Windows insufficiente.", "Memória virtual do Windows insuficiente.");
                }

                if (!string.IsNullOrEmpty(title))
                {
                    _lastAlertTime = DateTime.Now;
                    _pendingClick = null;
                    _notifyIcon.ShowBalloonTip(6000, title, msg, ToolTipIcon.Warning);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        public void Dispose()
        {
            StopEventWatcher();
            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                catch (Exception ex) { Log.Warn(ex); }
                _notifyIcon = null;
            }
        }
    }
}
