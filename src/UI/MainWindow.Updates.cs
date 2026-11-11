using System;
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
        private bool _updateChecking;

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

            Grid foot = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _btnUpdPage = CreateFlatButton("", "BgCard", "AccentText", "BgCard", new Thickness(0, 7, 12, 7), 12.5);
            _btnUpdPage.BorderThickness = new Thickness(0);
            _btnUpdPage.Click += (s, e) => { if (_shownUpdate != null) OpenRepoUrl(_shownUpdate.PageUrl); };
            foot.Children.Add(_btnUpdPage);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal };
            _btnUpdSkip = CreateFlatButton("", "BgCard", "TextSecondary", "BgCard", new Thickness(12, 7, 12, 7), 13);
            _btnUpdSkip.BorderThickness = new Thickness(0);
            _btnUpdSkip.Click += (s, e) => SkipShownUpdate();
            _btnUpdLater = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnUpdLater.Margin = new Thickness(6, 0, 0, 0);
            _btnUpdLater.Click += (s, e) => HideUpdate();
            _btnUpdDownload = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(18, 7, 18, 7), 13);
            _btnUpdDownload.Margin = new Thickness(8, 0, 0, 0);
            _btnUpdDownload.Click += (s, e) => { if (_shownUpdate != null) OpenRepoUrl(_shownUpdate.DownloadUrl); HideUpdate(); };
            btns.Children.Add(_btnUpdSkip);
            btns.Children.Add(_btnUpdLater);
            btns.Children.Add(_btnUpdDownload);
            Grid.SetColumn(btns, 2);
            foot.Children.Add(btns);
            sp.Children.Add(foot);

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

            _lblUpdKicker.Text = Localizer.L("NOUVELLE VERSION DISPONIBLE", "NEW VERSION AVAILABLE", "NUEVA VERSIÓN DISPONIBLE", "NEUE VERSION VERFÜGBAR", "NUOVA VERSIONE DISPONIBILE", "NOVA VERSÃO DISPONÍVEL");
            _lblUpdTitle.Text = "Dump Finder " + ver;
            string date = u.PublishedAt.HasValue ? u.PublishedAt.Value.ToLocalTime().ToString("d", Localizer.Culture) : null;
            _lblUpdSub.Text = Localizer.L("Tu utilises la version ", "You are using version ", "Usas la versión ", "Du nutzt Version ", "Stai usando la versione ", "Você usa a versão ") + UpdateChecker.CurrentVersionText +
                (date != null ? Localizer.L(" · publiée le ", " · released on ", " · publicada el ", " · veröffentlicht am ", " · pubblicata il ", " · publicada em ") + date : "");

            RenderReleaseNotes(u.Notes);

            _btnUpdDownload.Content = Localizer.L("Télécharger ", "Download ", "Descargar ", "Herunterladen ", "Scarica ", "Baixar ") + ver;
            _btnUpdLater.Content = Localizer.L("Plus tard", "Later", "Más tarde", "Später", "Più tardi", "Mais tarde");
            _btnUpdSkip.Content = Localizer.L("Ignorer cette version", "Skip this version", "Omitir esta versión", "Diese Version überspringen", "Salta questa versione", "Ignorar esta versão");
            _btnUpdPage.Content = Localizer.L("Voir sur GitHub", "View on GitHub", "Ver en GitHub", "Auf GitHub ansehen", "Vedi su GitHub", "Ver no GitHub");

            _updateOverlay.Visibility = Visibility.Visible;
            _btnUpdDownload.Focus();
        }

        private void HideUpdate()
        {
            if (_updateOverlay != null) _updateOverlay.Visibility = Visibility.Collapsed;
            _shownUpdate = null;
        }

        private void SkipShownUpdate()
        {
            if (_shownUpdate != null && _appSettings != null)
            {
                _appSettings.SkippedUpdateVersion = _shownUpdate.Version.ToString(3);
                Localizer.SaveFullSettings(_appSettings);
                SetToast(Localizer.L("Cette version ne sera plus proposée. La suivante le sera.", "This version won't be offered again. The next one will.", "Esta versión no se volverá a ofrecer.", "Diese Version wird nicht mehr angeboten.", "Questa versione non verrà più proposta.", "Esta versão não será mais oferecida."), "TextSecondary");
            }
            HideUpdate();
        }

        /// <summary>Release notes are Markdown: headings, bullets, bold and links are turned into plain readable text.</summary>
        private void RenderReleaseNotes(string md)
        {
            _updNotes.Children.Clear();
            if (string.IsNullOrWhiteSpace(md))
            {
                _updNotes.Children.Add(ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = Localizer.L("Pas de description pour cette version.", "No description for this release.", "Sin descripción.", "Keine Beschreibung.", "Nessuna descrizione.", "Sem descrição.") }, "TextSecondary"));
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
                Notes = "## Nouveautés\n- Onglet **Optimiser** : réglages, applications, démarrage et nettoyage\n- Température CPU pendant le stress test\n\n## Corrections\n- Le conseil HMB ne s'affiche plus hors Windows 11 24H2\n- Bouton « C'est déjà la dernière version » sur l'alerte BIOS",
                DownloadUrl = UpdateChecker.RepoUrl + "/releases/latest",
                PageUrl = UpdateChecker.RepoUrl + "/releases/latest"
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
                    SetToast(Localizer.L("Impossible de joindre GitHub pour le moment.", "Could not reach GitHub right now.", "No se pudo contactar con GitHub.", "GitHub ist gerade nicht erreichbar.", "Impossibile contattare GitHub.", "Não foi possível acessar o GitHub."), "WarnFg");
                else if (latest.Version > UpdateChecker.CurrentVersion)
                    ShowUpdate(latest); // on demand: shown even if this version was skipped
                else
                    SetToast(Localizer.L("Tu as déjà la dernière version (", "You already have the latest version (", "Ya tienes la última versión (", "Du hast bereits die neueste Version (", "Hai già l'ultima versione (", "Você já tem a versão mais recente (") + UpdateChecker.CurrentVersionText + ").", "SuccessFg");
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
                ? Localizer.L("● Activé", "● On", "● Activado", "● An", "● Attivo", "● Ativado")
                : Localizer.L("○ Désactivé", "○ Off", "○ Desactivado", "○ Aus", "○ Disattivo", "○ Desativado");
            _btnUpdToggle.SetResourceReference(Button.BackgroundProperty, on ? "SuccessSoftBg" : "BgCard");
            _btnUpdToggle.SetResourceReference(Button.ForegroundProperty, on ? "SuccessFg" : "TextSecondary");
            _btnUpdToggle.SetResourceReference(Button.BorderBrushProperty, on ? "SuccessBorder" : "BorderCard");
            _btnUpdCheckNow.Content = Localizer.L("Vérifier maintenant", "Check now", "Comprobar ahora", "Jetzt prüfen", "Controlla ora", "Verificar agora");
            _lblSetUpdTitle.Text = Localizer.L("Mises à jour", "Updates", "Actualizaciones", "Updates", "Aggiornamenti", "Atualizações");
            _lblSetUpdDesc.Text = Localizer.L(
                "Version " + UpdateChecker.CurrentVersionText + ". Au lancement, l'app regarde sur GitHub si une nouvelle version est sortie et te montre ses nouveautés.",
                "Version " + UpdateChecker.CurrentVersionText + ". At launch, the app checks GitHub for a new version and shows you what's new.",
                "Versión " + UpdateChecker.CurrentVersionText + ". Al iniciar, la app busca en GitHub una versión nueva.",
                "Version " + UpdateChecker.CurrentVersionText + ". Beim Start prüft die App auf GitHub, ob es eine neue Version gibt.",
                "Versione " + UpdateChecker.CurrentVersionText + ". All'avvio, l'app controlla su GitHub se esiste una nuova versione.",
                "Versão " + UpdateChecker.CurrentVersionText + ". Ao iniciar, o app verifica no GitHub se há uma nova versão.");
        }
    }
}
