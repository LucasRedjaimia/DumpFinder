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
            // Drop-down lists read their row title to screen readers (a button has its own label).
            if (control is System.Windows.Controls.Primitives.Selector) A11y.LabelFor(control, title);
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
            _lblSettingsTitle.Text = Localizer.S("main_window.settings");
            _lblSetGeneral.Text = Localizer.S("settings.general");
            _lblSetMonitoring.Text = Localizer.S("settings.monitoring");
            _lblSetAi.Text = Localizer.S("settings.ai_assistant");
            _lblSetIntegrations.Text = Localizer.S("settings.integrations");
            _lblSetHelp.Text = Localizer.S("settings.help");

            _lblSetLangTitle.Text = Localizer.S("settings.language");
            _lblSetLangDesc.Text = Localizer.S("settings.language_of_the_interface_and_explanatio");
            _lblSetThemeTitle.Text = Localizer.S("settings.appearance");
            _lblSetThemeDesc.Text = Localizer.S("settings.follow_windows_or_force_light_or");
            _lblSetSentinelTitle.Text = Localizer.S("settings.sentinel");
            _lblSetSentinelDesc.Text = Localizer.S("settings.stays_in_the_notification_area_and");
            _lblSetAiDesc.Text = Localizer.S("settings.used_by_ask_the_ai_on");
            _lblSetTourTitle.Text = Localizer.S("settings.app_introduction");
            _lblSetTourDesc.Text = Localizer.S("settings.replay_the_three_welcome_screens_and");
            _lblSetAdminTitle.Text = Localizer.S("settings.administrator_mode");
            _lblSetAdminDesc.Text = Localizer.S("settings.restarts_the_app_with_the_rights");
            _btnSettingsElevate.Content = Localizer.S("settings.restart_as_admin");
            RefreshAutostartVisual();
            RefreshUpdateSettingsVisual();
            _btnTourGuide.Content = Localizer.S("settings.replay_introduction");
        }

        private void RenderSettingsState(ScanResult res)
        {
            if (_setAdminRow == null || res == null) return;
            _setAdminRow.Visibility = res.Hardware.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
