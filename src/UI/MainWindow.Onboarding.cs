using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinCrashDoctor
{
    /// <summary>
    /// First-run welcome (3 screens) + one contextual tip the first time each section is opened.
    /// Replaces the former 23-step spotlight tour.
    /// </summary>
    public partial class MainWindow : Window
    {
        private Grid _rootGrid;
        private Grid _onboardingOverlay;
        private Button _btnTourGuide;
        private bool _initialTourTriggered = false;
        private AppSettings _appSettings;

        private bool _onboardingActive;
        private int _onboardingStepIndex;
        private StackPanel _onbDots;
        private TextBlock _lblOnbKicker;
        private TextBlock _lblOnbTitle;
        private TextBlock _lblOnbBody;
        private Button _btnOnbSkip;
        private Button _btnOnbBack;
        private Button _btnOnbNext;
        private Border _onbIllustration;

        private const int OnboardingSteps = 3;

        private void BuildOnboardingOverlay()
        {
            _onboardingOverlay = new Grid { Visibility = Visibility.Collapsed };
            Border scrim = new Border();
            scrim.SetResourceReference(Border.BackgroundProperty, "ScrimBg");
            _onboardingOverlay.Children.Add(scrim);

            Border card = ResBorder(ResBg(new Border
            {
                Width = 520,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(32, 30, 32, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 14, Direction = 270, Opacity = 0.4, Color = Colors.Black }
            }, "BgCard"), "BorderStrong");

            StackPanel sp = new StackPanel();
            _onbIllustration = new Border { Height = 88, Margin = new Thickness(0, 0, 0, 22) };
            sp.Children.Add(_onbIllustration);

            _lblOnbKicker = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold }, "AccentText");
            _lblOnbTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) }, "TextPrimary");
            _lblOnbBody = ResFg(new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21, MinHeight = 84 }, "TextSecondary");
            sp.Children.Add(_lblOnbKicker);
            sp.Children.Add(_lblOnbTitle);
            sp.Children.Add(_lblOnbBody);

            Grid foot = new Grid { Margin = new Thickness(0, 26, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _onbDots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foot.Children.Add(_onbDots);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal };
            _btnOnbSkip = CreateFlatButton("", "BgCard", "TextSecondary", "BgCard", new Thickness(12, 7, 12, 7), 13);
            _btnOnbSkip.BorderThickness = new Thickness(0);
            _btnOnbSkip.Click += (s, e) => EndOnboarding();
            _btnOnbBack = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnOnbBack.Margin = new Thickness(6, 0, 0, 0);
            _btnOnbBack.Click += (s, e) => PrevOnboardingStep();
            _btnOnbNext = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(18, 7, 18, 7), 13);
            _btnOnbNext.Margin = new Thickness(8, 0, 0, 0);
            _btnOnbNext.Click += (s, e) => NextOnboardingStep();
            btns.Children.Add(_btnOnbSkip);
            btns.Children.Add(_btnOnbBack);
            btns.Children.Add(_btnOnbNext);
            Grid.SetColumn(btns, 2);
            foot.Children.Add(btns);
            sp.Children.Add(foot);

            card.Child = sp;
            _onboardingOverlay.Children.Add(card);

            _onboardingOverlay.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { EndOnboarding(); e.Handled = true; }
                else if (e.Key == Key.Right || e.Key == Key.Enter) { NextOnboardingStep(); e.Handled = true; }
                else if (e.Key == Key.Left) { PrevOnboardingStep(); e.Handled = true; }
            };
        }

        public void StartOnboarding()
        {
            _onboardingActive = true;
            _onboardingStepIndex = 0;
            _onboardingOverlay.Visibility = Visibility.Visible;
            ShowCurrentOnboardingStep();
            _btnOnbNext.Focus();
        }

        public void EndOnboarding()
        {
            _onboardingActive = false;
            _onboardingOverlay.Visibility = Visibility.Collapsed;
            if (_appSettings != null)
            {
                _appSettings.HasCompletedTour = true;
                Localizer.SaveFullSettings(_appSettings);
            }
            if (_pendingUpdate != null) { var u = _pendingUpdate; _pendingUpdate = null; ShowUpdate(u); }
        }

        public void NextOnboardingStep()
        {
            if (!_onboardingActive) return;
            if (_onboardingStepIndex >= OnboardingSteps - 1) { EndOnboarding(); return; }
            _onboardingStepIndex++;
            ShowCurrentOnboardingStep();
        }

        public void PrevOnboardingStep()
        {
            if (!_onboardingActive || _onboardingStepIndex == 0) return;
            _onboardingStepIndex--;
            ShowCurrentOnboardingStep();
        }

        /// <summary>Replays the welcome and re-arms the per-section tips (Settings › Help).</summary>
        private void ReplayIntroduction()
        {
            if (_appSettings != null)
            {
                _appSettings.SeenTips = "";
                Localizer.SaveFullSettings(_appSettings);
            }
            SwitchTab(TabHome);
            StartOnboarding();
        }

        private void ShowCurrentOnboardingStep()
        {
            int i = _onboardingStepIndex;
            switch (i)
            {
                case 0:
                    _lblOnbKicker.Text = Localizer.S("onboarding.welcome");
                    _lblOnbTitle.Text = Localizer.S("onboarding.understand_why_your_pc_crashes");
                    _lblOnbBody.Text = Localizer.S("onboarding.when_windows_crashes_it_leaves_traces");
                    _onbIllustration.Child = IllustrationLogo();
                    break;
                case 1:
                    _lblOnbKicker.Text = Localizer.S("onboarding.step_2_of_3");
                    _lblOnbTitle.Text = Localizer.S("onboarding.the_scan_is_already_running");
                    _lblOnbBody.Text = Localizer.S("onboarding.30_days_of_logs_dmp_files");
                    _onbIllustration.Child = IllustrationIcons(new[] { Glyph.Document, Glyph.Diagnostic, Glyph.Component, Glyph.Shield });
                    break;
                default:
                    _lblOnbKicker.Text = Localizer.S("onboarding.step_3_of_3");
                    _lblOnbTitle.Text = Localizer.S("onboarding.your_dashboard");
                    _lblOnbBody.Text = Localizer.S("onboarding.home_shows_a_30_day_stability");
                    _onbIllustration.Child = IllustrationScore();
                    break;
            }

            _btnOnbSkip.Content = Localizer.S("onboarding.skip");
            _btnOnbBack.Content = Localizer.S("onboarding.back");
            _btnOnbBack.Visibility = i == 0 ? Visibility.Collapsed : Visibility.Visible;
            _btnOnbSkip.Visibility = i == OnboardingSteps - 1 ? Visibility.Collapsed : Visibility.Visible;
            _btnOnbNext.Content = i == OnboardingSteps - 1
                ? Localizer.S("onboarding.get_started")
                : Localizer.S("onboarding.next");

            _onbDots.Children.Clear();
            for (int d = 0; d < OnboardingSteps; d++)
            {
                Border dot = ResBg(new Border { Height = 6, Width = d == i ? 20 : 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) }, d == i ? "Accent" : "BorderStrong");
                _onbDots.Children.Add(dot);
            }
            AnimatePageIn(_onbIllustration);
        }

        private UIElement IllustrationLogo()
        {
            Image logo = new Image { Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Left };
            logo.SetResourceReference(Image.SourceProperty, "BrandLogo");
            return logo;
        }

        private UIElement IllustrationIcons(string[] glyphs)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var g in glyphs)
            {
                Border tile = MakeIconTile(g, "AccentSoftBg", "AccentText", 56);
                tile.CornerRadius = new CornerRadius(16);
                ((TextBlock)tile.Child).FontSize = 22;
                tile.Margin = new Thickness(0, 0, 12, 0);
                sp.Children.Add(tile);
            }
            return sp;
        }

        private UIElement IllustrationScore()
        {
            Grid g = new Grid { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Left };
            ScoreRing ring = new ScoreRing { Value = 86, StrokeWidth = 8 };
            ring.SetResourceReference(ScoreRing.TrackBrushProperty, "BgSubtle");
            ring.SetResourceReference(ScoreRing.FillBrushProperty, "SuccessFg");
            g.Children.Add(ring);
            g.Children.Add(ResFg(new TextBlock { Text = "86", FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "TextPrimary"));
            return g;
        }

        // ==================== CONTEXTUAL TIPS (once per section) ====================
        private void OnUserSwitchedTab(int tabIdx)
        {
            if (_onboardingActive || _appSettings == null || !_appSettings.HasCompletedTour) return;
            string id = "tab" + tabIdx;
            var seen = new HashSet<string>((_appSettings.SeenTips ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            if (seen.Contains(id)) return;

            string tip = SectionTip(tabIdx);
            if (tip == null) return;
            seen.Add(id);
            _appSettings.SeenTips = string.Join(",", seen);
            Localizer.SaveFullSettings(_appSettings);
            ShowToast(tip, ToastKind.Info, true);
        }

        private static string SectionTip(int tabIdx)
        {
            switch (tabIdx)
            {
                case TabCrashes:
                    return Localizer.S("onboarding.tip_click_a_crash_to_open");
                case TabHardware:
                    return Localizer.S("onboarding.tip_the_stability_test_tab_checks");
                case TabFixes:
                    return Localizer.S("onboarding.tip_the_registry_is_backed_up");
                case TabTools:
                    return Localizer.S("onboarding.tip_report_export_gathers_the_whole");
                default:
                    return null;
            }
        }
    }
}
