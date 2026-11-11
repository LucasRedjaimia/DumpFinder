using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>Settings: everything that used to crowd the sidebar, plus the AI assistant and MCP integrations.</summary>
    public partial class MainWindow
    {
        private Grid _pageSettings;
        private TextBlock _lblSettingsTitle;
        private TextBlock _lblSetGeneral, _lblSetMonitoring, _lblSetAi, _lblSetIntegrations, _lblSetHelp;
        private TextBlock _lblSetLangTitle, _lblSetLangDesc, _lblSetThemeTitle, _lblSetThemeDesc;
        private TextBlock _lblSetSentinelTitle, _lblSetSentinelDesc;
        private TextBlock _lblSetTourTitle, _lblSetTourDesc, _lblSetAdminTitle, _lblSetAdminDesc;
        private TextBlock _lblSetAiDesc;
        private Button _btnSettingsElevate;
        private Grid _setAdminRow;

        private void BuildSettingsPage()
        {
            _pageSettings = new Grid { Visibility = Visibility.Collapsed };
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
            sv.Content = stack;
            _pageSettings.Children.Add(sv);

            _lblSettingsTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, Margin = new Thickness(0, 0, 0, 18) }, "TextPrimary");
            stack.Children.Add(_lblSettingsTitle);

            // ---- General
            _lblSetGeneral = SectionHeader(stack);
            Border general = MakeCard(new Thickness(0), new Thickness(0, 0, 0, 18));
            StackPanel g = new StackPanel();
            DetachFromParent(_cbLanguage);
            DetachFromParent(_cbTheme);
            _cbLanguage.Width = 220; _cbLanguage.Margin = new Thickness(0);
            _cbTheme.Width = 220; _cbTheme.Margin = new Thickness(0);
            g.Children.Add(SettingRow(Glyph.Globe, out _lblSetLangTitle, out _lblSetLangDesc, _cbLanguage, true));
            g.Children.Add(SettingRow(Glyph.Lightbulb, out _lblSetThemeTitle, out _lblSetThemeDesc, _cbTheme, false));
            g.Children.Add(BuildUpdateSettingsRow());
            general.Child = g;
            stack.Children.Add(general);

            // ---- Monitoring
            _lblSetMonitoring = SectionHeader(stack);
            Border mon = MakeCard(new Thickness(0), new Thickness(0, 0, 0, 18));
            DetachFromParent(_btnSentinelToggle);
            _btnSentinelToggle.Margin = new Thickness(0);
            _btnSentinelToggle.MinWidth = 180;
            StackPanel monStack = new StackPanel();
            monStack.Children.Add(SettingRow(Glyph.Shield, out _lblSetSentinelTitle, out _lblSetSentinelDesc, _btnSentinelToggle, true));
            monStack.Children.Add(BuildAutostartRow());
            mon.Child = monStack;
            stack.Children.Add(mon);

            // ---- AI assistant (provider card from the former AI page)
            _lblSetAi = SectionHeader(stack);
            _lblSetAiDesc = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -4, 0, 10) }, "TextSecondary");
            stack.Children.Add(_lblSetAiDesc);
            _aiProviderCard.Margin = new Thickness(0, 0, 0, 18);
            stack.Children.Add(_aiProviderCard);

            // ---- Integrations (MCP card from the former AI page)
            _lblSetIntegrations = SectionHeader(stack);
            _mcpCardStack.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(_mcpCardStack);

            // ---- Help
            _lblSetHelp = SectionHeader(stack);
            Border help = MakeCard(new Thickness(0), new Thickness(0, 0, 0, 24));
            StackPanel h = new StackPanel();
            DetachFromParent(_btnTourGuide);
            _btnTourGuide.Margin = new Thickness(0);
            _btnTourGuide.HorizontalContentAlignment = HorizontalAlignment.Center;
            _btnTourGuide.MinWidth = 180;
            h.Children.Add(SettingRow(Glyph.Lightbulb, out _lblSetTourTitle, out _lblSetTourDesc, _btnTourGuide, true));
            _btnSettingsElevate = CreateFlatButton("", "WarnSoftBg", "WarnFg", "WarnSoftBg", new Thickness(12, 6, 12, 6), 12);
            _btnSettingsElevate.MinWidth = 180;
            _btnSettingsElevate.Click += OnElevateClick;
            _setAdminRow = SettingRow(Glyph.Shield, out _lblSetAdminTitle, out _lblSetAdminDesc, _btnSettingsElevate, false);
            h.Children.Add(_setAdminRow);
            help.Child = h;
            stack.Children.Add(help);
        }

        private TextBlock SectionHeader(StackPanel host)
        {
            TextBlock t = ResFg(new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) }, "TextSecondary");
            host.Children.Add(t);
            return t;
        }

        private Grid SettingRow(string glyph, out TextBlock title, out TextBlock desc, FrameworkElement control, bool first)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock ic = ResFg(Ds.Icon(glyph, 16), "TextSecondary");
            ic.Margin = new Thickness(0, 0, 16, 0);
            row.Children.Add(ic);

            StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            title = ResFg(new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary");
            desc = ResFg(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary");
            t.Children.Add(title);
            t.Children.Add(desc);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);

            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            row.Children.Add(control);

            Border wrap = ResBorder(new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(18, 14, 18, 14), Child = row }, "BorderCard");
            Grid host = new Grid();
            host.Children.Add(wrap);
            return host;
        }

        private void RefreshSettingsLabels()
        {
            if (_lblSettingsTitle == null) return;
            _lblSettingsTitle.Text = Localizer.L("Paramètres", "Settings", "Ajustes", "Einstellungen", "Impostazioni", "Configurações");
            _lblSetGeneral.Text = Localizer.L("Général", "General", "General", "Allgemein", "Generale", "Geral");
            _lblSetMonitoring.Text = Localizer.L("Surveillance", "Monitoring", "Supervisión", "Überwachung", "Monitoraggio", "Monitoramento");
            _lblSetAi.Text = Localizer.L("Assistant IA", "AI assistant", "Asistente IA", "KI-Assistent", "Assistente IA", "Assistente IA");
            _lblSetIntegrations.Text = Localizer.L("Intégrations", "Integrations", "Integraciones", "Integrationen", "Integrazioni", "Integrações");
            _lblSetHelp.Text = Localizer.L("Aide", "Help", "Ayuda", "Hilfe", "Aiuto", "Ajuda");

            _lblSetLangTitle.Text = Localizer.L("Langue", "Language", "Idioma", "Sprache", "Lingua", "Idioma");
            _lblSetLangDesc.Text = Localizer.L("Langue de l'interface et des explications.", "Language of the interface and explanations.", "Idioma de la interfaz y explicaciones.", "Sprache der Oberfläche und Erklärungen.", "Lingua dell'interfaccia e delle spiegazioni.", "Idioma da interface e das explicações.");
            _lblSetThemeTitle.Text = Localizer.L("Apparence", "Appearance", "Apariencia", "Darstellung", "Aspetto", "Aparência");
            _lblSetThemeDesc.Text = Localizer.L("Suivre Windows, ou forcer le thème clair ou sombre.", "Follow Windows, or force light or dark.", "Seguir Windows o forzar claro u oscuro.", "Windows folgen oder hell/dunkel erzwingen.", "Segui Windows o forza chiaro/scuro.", "Seguir o Windows ou forçar claro/escuro.");
            _lblSetSentinelTitle.Text = Localizer.L("Sentinelle", "Sentinel", "Centinela", "Wächter", "Sentinella", "Sentinela");
            _lblSetSentinelDesc.Text = Localizer.L(
                "Reste dans la zone de notification et vous prévient dès qu'un crash, un timeout GPU ou une erreur matérielle est enregistré.",
                "Stays in the notification area and alerts you as soon as a crash, GPU timeout or hardware error is logged.",
                "Permanece en el área de notificación y avisa en cuanto se registra un fallo.",
                "Bleibt im Infobereich und meldet sofort Abstürze, GPU-Timeouts oder Hardwarefehler.",
                "Resta nell'area di notifica e avvisa appena viene registrato un crash.",
                "Fica na área de notificação e avisa assim que uma falha é registrada.");
            _lblSetAiDesc.Text = Localizer.L(
                "Utilisé par « Demander à l'IA » sur chaque crash. Votre clé reste sur ce PC. Sans clé, « Copier le prompt » prépare un rapport à coller dans n'importe quel assistant.",
                "Used by \"Ask the AI\" on each crash. Your key stays on this PC. Without a key, \"Copy prompt\" prepares a report to paste into any assistant.",
                "Usado por «Preguntar a la IA» en cada fallo. Su clave se queda en este PC.",
                "Wird von „KI fragen“ bei jedem Absturz genutzt. Ihr Schlüssel bleibt auf diesem PC.",
                "Usato da «Chiedi all'IA» su ogni crash. La chiave resta su questo PC.",
                "Usado por «Perguntar à IA» em cada falha. Sua chave fica neste PC.");
            _lblSetTourTitle.Text = Localizer.L("Présentation de l'app", "App introduction", "Presentación de la app", "App-Einführung", "Presentazione dell'app", "Apresentação do app");
            _lblSetTourDesc.Text = Localizer.L("Revoir les trois écrans d'accueil et réafficher les astuces de chaque section.", "Replay the three welcome screens and show each section's tips again.", "Volver a ver la bienvenida y los consejos.", "Willkommensbildschirme und Tipps erneut anzeigen.", "Rivedi il benvenuto e i suggerimenti.", "Rever a apresentação e as dicas.");
            _lblSetAdminTitle.Text = Localizer.L("Mode administrateur", "Administrator mode", "Modo administrador", "Administratormodus", "Modalità amministratore", "Modo administrador");
            _lblSetAdminDesc.Text = Localizer.L("Relance l'app avec les droits nécessaires à la lecture SMART et des dumps d'écran bleu.", "Restarts the app with the rights needed for SMART and blue-screen dumps.", "Reinicia la app con permisos para SMART y volcados.", "Startet die App mit Rechten für SMART und Bluescreen-Dumps neu.", "Riavvia l'app con i permessi per SMART e dump.", "Reinicia o app com permissões para SMART e dumps.");
            _btnSettingsElevate.Content = Localizer.L("🛡 Relancer en admin", "🛡 Restart as admin", "🛡 Reiniciar como admin", "🛡 Als Admin neu starten", "🛡 Riavvia come admin", "🛡 Reiniciar como admin");
            RefreshAutostartVisual();
            RefreshUpdateSettingsVisual();
            _btnTourGuide.Content = Localizer.L("Revoir la présentation", "Replay introduction", "Ver de nuevo", "Erneut ansehen", "Rivedi", "Rever");
        }

        private void RenderSettingsState(ScanResult res)
        {
            if (_setAdminRow == null || res == null) return;
            _setAdminRow.Visibility = res.Hardware.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
