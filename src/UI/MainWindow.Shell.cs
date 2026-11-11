using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;

namespace WinCrashDoctor
{
    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Window shell: custom title bar, collapsible sidebar, toast notifications,
    /// quiet status line and page transitions.
    /// </summary>
    public partial class MainWindow
    {
        private Button _btnCaptionMax;
        private Button _navSettingsButton;
        private StackPanel _toastHost;
        private Border _statusDot;
        private const int MaxToasts = 4;

        private static bool AnimationsEnabled
        {
            get { return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; }
        }

        // ==================== TITLE BAR ====================
        private UIElement BuildTitleBar()
        {
            Grid bar = new Grid { Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            Button toggle = CreateNavButton(ResFg(Ds.Icon(Glyph.GlobalNav, 14), "TextSecondary"));
            toggle.Width = 40;
            toggle.Height = 32;
            toggle.Padding = new Thickness(0);
            toggle.Margin = new Thickness(0, 0, 8, 0);
            toggle.ToolTip = Localizer.L("Réduire / développer le menu", "Collapse / expand menu", "Contraer / expandir menú", "Menü ein-/ausklappen", "Comprimi / espandi menu", "Recolher / expandir menu");
            System.Windows.Automation.AutomationProperties.SetName(toggle, "Menu");
            toggle.Click += (s, e) => ApplySidebarCollapsed(!_sidebarCollapsed, true);
            WindowChrome.SetIsHitTestVisibleInChrome(toggle, true);
            left.Children.Add(toggle);

            Image logo = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            logo.SetResourceReference(Image.SourceProperty, "BrandLogo");
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            left.Children.Add(logo);

            TextBlock word = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            word.Inlines.Add(new System.Windows.Documents.Run("Dump") { FontWeight = FontWeights.Bold });
            var finder = new System.Windows.Documents.Run("Finder") { FontWeight = FontWeights.Medium };
            finder.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondary");
            word.Inlines.Add(finder);
            ResFg(word, "TextPrimary");
            left.Children.Add(word);

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            Border ver = ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(9, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = ResFg(new TextBlock { Text = v.Major + "." + v.Minor, FontSize = 11, FontWeight = FontWeights.SemiBold }, "TextMuted")
            }, "BgSubtle");
            left.Children.Add(ver);

            Grid.SetColumn(left, 0);
            bar.Children.Add(left);

            StackPanel caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            caps.Children.Add(CaptionButton(Glyph.Minimize, "DfCaptionButton", Localizer.L("Réduire", "Minimize", "Minimizar", "Minimieren", "Riduci", "Minimizar"),
                () => WindowState = WindowState.Minimized));
            _btnCaptionMax = CaptionButton(Glyph.Maximize, "DfCaptionButton", Localizer.L("Agrandir", "Maximize", "Maximizar", "Maximieren", "Ingrandisci", "Maximizar"),
                () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            caps.Children.Add(_btnCaptionMax);
            caps.Children.Add(CaptionButton(Glyph.Close, "DfCaptionCloseButton", Localizer.L("Fermer", "Close", "Cerrar", "Schließen", "Chiudi", "Fechar"),
                () => Close()));
            if (WindowShell.NativeCaptionButtons)
            {
                // DWM paints the real buttons here; keep the space free (3 x 46 px).
                caps.Children.Clear();
                caps.Width = 138;
            }
            Grid.SetColumn(caps, 2);
            bar.Children.Add(caps);

            StateChanged += (s, e) =>
            {
                if (_btnCaptionMax != null)
                    _btnCaptionMax.Content = WindowState == WindowState.Maximized ? Glyph.Restore : Glyph.Maximize;
            };

            return bar;
        }

        private Button CaptionButton(string glyph, string styleKey, string name, Action onClick)
        {
            Button b = new Button { Content = glyph, Height = WindowShell.TitleBarHeight };
            b.SetResourceReference(StyleProperty, styleKey);
            b.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (s, e) => onClick();
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        // ==================== SIDEBAR COLLAPSE ====================
        /// <summary>Used by the --screenshot harness; does not persist the choice.</summary>
        public void SetSidebarCollapsedForCapture(bool collapsed)
        {
            ApplySidebarCollapsed(collapsed, false);
        }

        private void ApplySidebarCollapsed(bool collapsed, bool persist)
        {
            _sidebarCollapsed = collapsed;
            if (_sidebarColumn == null) return;

            _sidebarColumn.Width = new GridLength(collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth);
            _sidebar.Padding = collapsed ? new Thickness(10, 6, 10, 14) : new Thickness(12, 6, 12, 14);
            _sidebarBottomStack.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

            for (int i = 0; i < _navLabels.Length; i++)
            {
                if (_navLabels[i] == null) continue;
                _navLabels[i].Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                _navButtons[i].ToolTip = collapsed ? _navLabels[i].Text : null;
                _navButtons[i].Padding = collapsed ? new Thickness(6, 9, 6, 9) : new Thickness(8, 9, 10, 9);
            }
            RefreshNavBadgesForCollapse();

            if (persist && Localizer.CachedSettings != null)
            {
                Localizer.CachedSettings.SidebarCollapsed = collapsed;
                Localizer.SaveFullSettings(Localizer.CachedSettings);
            }
        }

        /// <summary>In collapsed mode the count badges would overflow: hide them (the icon tooltip remains).</summary>
        private void RefreshNavBadgesForCollapse()
        {
            if (!_sidebarCollapsed) return;
            if (_navHealthBadge != null) _navHealthBadge.Visibility = Visibility.Collapsed;
            if (_navFixesBadge != null) _navFixesBadge.Visibility = Visibility.Collapsed;
        }

        // ==================== PAGE TRANSITIONS ====================
        private void AnimatePageIn(FrameworkElement page)
        {
            if (page == null || !AnimationsEnabled) return;
            var tt = page.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                page.RenderTransform = tt;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(180);
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, dur) { EasingFunction = ease });
        }

        // ==================== STATUS LINE ====================
        private void SetStatus(string text, bool accent)
        {
            if (_lblToast == null) return;
            _lblToast.Text = text;
            _lblToast.SetResourceReference(TextBlock.ForegroundProperty, accent ? "AccentText" : "TextMuted");
            if (_statusDot != null) _statusDot.SetResourceReference(Border.BackgroundProperty, accent ? "Accent" : "TextMuted");
        }

        // ==================== TOASTS ====================
        /// <summary>Used by the --screenshot harness to capture the toast styles.</summary>
        public void ShowToastPreviewForCapture()
        {
            ShowToast(Localizer.L("✓ Multiplane Overlay (MPO) désactivé (OverlayTestMode = 5).", "✓ Multiplane Overlay (MPO) disabled (OverlayTestMode = 5).", "✓ MPO desactivado.", "✓ MPO deaktiviert.", "✓ MPO disattivato.", "✓ MPO desativado."), ToastKind.Success);
            ShowToast(Localizer.L("Opération annulée au contrôle Administrateur (UAC).", "Operation cancelled at the UAC Administrator prompt.", "Operación cancelada (UAC).", "Vorgang abgebrochen (UAC).", "Operazione annullata (UAC).", "Operação cancelada (UAC)."), ToastKind.Warning);
        }

        private UIElement BuildToastHost()
        {
            _toastHost = new StackPanel
            {
                Width = 380,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 40)
            };
            return _toastHost;
        }

        private FrameworkElement _activeTipToast;

        private void ShowToast(string text, ToastKind kind, bool isTip = false)
        {
            if (_toastHost == null) { SetStatus(text, false); return; }
            text = text.TrimStart('✓', '✔', ' '); // the toast icon already says "success"

            // Collapse an identical toast that is still on screen instead of stacking duplicates.
            foreach (UIElement child in _toastHost.Children)
            {
                var existing = child as FrameworkElement;
                if (existing != null && (existing.Tag as string) == text) return;
            }
            while (_toastHost.Children.Count >= MaxToasts)
                _toastHost.Children.RemoveAt(0);

            string fg, soft, glyph;
            switch (kind)
            {
                case ToastKind.Success: fg = "SuccessFg"; soft = "SuccessSoftBg"; glyph = Glyph.Completed; break;
                case ToastKind.Warning: fg = "WarnFg"; soft = "WarnSoftBg"; glyph = Glyph.Warning; break;
                case ToastKind.Error: fg = "DangerFg"; soft = "DangerSoftBg"; glyph = Glyph.ErrorBadge; break;
                default: fg = "AccentText"; soft = "AccentSoftBg"; glyph = Glyph.Lightbulb; break;
            }

            Border card = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 11, 8, 11),
                Margin = new Thickness(0, 8, 0, 0),
                Tag = text,
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = ThemeManager.IsDark ? 0.45 : 0.14, Color = Colors.Black }
            }, "ToastBg"), "BorderStrong");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(card, System.Windows.Automation.AutomationLiveSetting.Polite);
            System.Windows.Automation.AutomationProperties.SetName(card, text);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border ic = ResBg(new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Top,
                Child = ResFg(Ds.Icon(glyph, 14), fg)
            }, soft);
            Grid.SetColumn(ic, 0);
            g.Children.Add(ic);

            TextBlock msg = ResFg(new TextBlock
            {
                Text = text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(11, 4, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            }, "TextPrimary");
            Grid.SetColumn(msg, 1);
            g.Children.Add(msg);

            Button close = CreateNavButton(ResFg(Ds.Icon(Glyph.Cancel, 10), "TextMuted"));
            close.Width = 26;
            close.Height = 26;
            close.Padding = new Thickness(0);
            close.Margin = new Thickness(0);
            close.VerticalAlignment = VerticalAlignment.Top;
            System.Windows.Automation.AutomationProperties.SetName(close, Localizer.L("Fermer", "Dismiss", "Cerrar", "Schließen", "Chiudi", "Fechar"));
            Grid.SetColumn(close, 2);
            g.Children.Add(close);

            card.Child = g;
            if (isTip)
            {
                // Only one section tip at a time: a new section replaces the previous tip.
                if (_activeTipToast != null) _toastHost.Children.Remove(_activeTipToast);
                _activeTipToast = card;
            }
            _toastHost.Children.Add(card);

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : kind == ToastKind.Warning ? 6 : kind == ToastKind.Info ? 8 : 4.5)
            };
            timer.Tick += (s, e) => { timer.Stop(); DismissToast(card); };
            card.MouseEnter += (s, e) => timer.Stop();
            card.MouseLeave += (s, e) => timer.Start();
            close.Click += (s, e) => { timer.Stop(); DismissToast(card); };
            timer.Start();

            if (AnimationsEnabled)
            {
                var tt = new TranslateTransform(0, 14);
                card.RenderTransform = tt;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(220);
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, dur) { EasingFunction = ease });
            }
        }

        private void DismissToast(FrameworkElement card)
        {
            if (card == null || !_toastHost.Children.Contains(card)) return;
            if (!AnimationsEnabled)
            {
                _toastHost.Children.Remove(card);
                return;
            }
            var fade = new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => _toastHost.Children.Remove(card);
            card.BeginAnimation(OpacityProperty, fade);
        }
    }
}
