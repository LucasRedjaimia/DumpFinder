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
            try
            {
                ScanResult res = await Task.Run(() => DiagnosticsEngine.RunScan(DateTime.Now.AddDays(-Stability.WindowDays)));
                _lastResult = res;
                RenderScanResult(res);

                DateTime now = DateTime.Now;
                var fresh = History.PeekNewCrashes(_crashEntries.Where(c => (now - c.Time).TotalDays <= Stability.WindowDays).Select(c => c.Key));
                var severe = _crashEntries.Where(c => fresh.Contains(c.Key) && Stability.IsSevere(c.Category)).OrderByDescending(c => c.Time).ToList();
                if (severe.Count == 0 || _traySentinel == null) return;

                CrashEntry last = severe[0];
                string when = last.Time.Date == now.Date
                    ? Localizer.L("aujourd'hui à ", "today at ", "hoy a las ", "heute um ", "oggi alle ", "hoje às ") + last.Time.ToString("HH:mm")
                    : last.Time.Date == now.Date.AddDays(-1)
                        ? Localizer.L("hier à ", "yesterday at ", "ayer a las ", "gestern um ", "ieri alle ", "ontem às ") + last.Time.ToString("HH:mm")
                        : last.Time.ToString("g");
                string key = last.Key;
                _traySentinel.Notify(
                    Localizer.L("Votre PC a planté " + when, "Your PC crashed " + when, "Su PC falló " + when, "Ihr PC ist abgestürzt: " + when, "Il PC è andato in crash " + when, "Seu PC travou " + when),
                    last.Title + (severe.Count > 1 ? Localizer.L(" (+" + (severe.Count - 1) + " autre(s))", " (+" + (severe.Count - 1) + " more)", " (+" + (severe.Count - 1) + ")", " (+" + (severe.Count - 1) + ")", " (+" + (severe.Count - 1) + ")", " (+" + (severe.Count - 1) + ")") : "")
                        + "\n" + Localizer.L("Cliquez pour voir l'analyse.", "Click to see the analysis.", "Haga clic para ver el análisis.", "Klicken Sie für die Analyse.", "Clicca per vedere l'analisi.", "Clique para ver a análise."),
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
                    ? Localizer.L("Impossible de modifier le démarrage automatique.", "Could not change automatic start.", "No se pudo cambiar el inicio automático.", "Autostart konnte nicht geändert werden.", "Impossibile modificare l'avvio automatico.", "Não foi possível alterar o início automático.")
                    : target
                        ? Localizer.L("Dump Finder démarrera avec Windows, discrètement dans la zone de notification.", "Dump Finder will start with Windows, quietly in the notification area.", "Dump Finder se iniciará con Windows.", "Dump Finder startet mit Windows.", "Dump Finder si avvierà con Windows.", "O Dump Finder iniciará com o Windows.")
                        : Localizer.L("Démarrage automatique désactivé.", "Automatic start disabled.", "Inicio automático desactivado.", "Autostart deaktiviert.", "Avvio automatico disattivato.", "Início automático desativado."),
                ok ? "SuccessFg" : "WarnFg");
        }

        private void RefreshAutostartVisual()
        {
            if (_btnAutostart == null) return;
            bool on = Autostart.IsEnabled;
            _btnAutostart.Content = on
                ? Localizer.L("● Activé", "● On", "● Activado", "● An", "● Attivo", "● Ativado")
                : Localizer.L("○ Désactivé", "○ Off", "○ Desactivado", "○ Aus", "○ Disattivo", "○ Desativado");
            _btnAutostart.SetResourceReference(Button.BackgroundProperty, on ? "SuccessSoftBg" : "BgCard");
            _btnAutostart.SetResourceReference(Button.ForegroundProperty, on ? "SuccessFg" : "TextSecondary");
            _btnAutostart.SetResourceReference(Button.BorderBrushProperty, on ? "SuccessBorder" : "BorderCard");
            _lblSetAutostartTitle.Text = Localizer.L("Démarrer avec Windows", "Start with Windows", "Iniciar con Windows", "Mit Windows starten", "Avvia con Windows", "Iniciar com o Windows");
            _lblSetAutostartDesc.Text = Localizer.L(
                "Lance la Sentinelle à l'ouverture de session, sans fenêtre. Si un crash grave a eu lieu, une notification vous le signale.",
                "Starts the Sentinel at sign-in, without a window. If a severe crash happened, a notification tells you.",
                "Inicia el Centinela al iniciar sesión, sin ventana.",
                "Startet den Wächter bei der Anmeldung, ohne Fenster.",
                "Avvia la Sentinella all'accesso, senza finestra.",
                "Inicia a Sentinela ao entrar, sem janela.");
        }
    }
}
