using System.Collections.ObjectModel;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>
/// Full-window overlays, same as Windows: the 3-step introduction (MainWindow.Onboarding.cs) and the
/// "new version" card (MainWindow.Updates.cs), plus the one-time tip per section and the Settings rows.
/// </summary>
public sealed partial class MainViewModel
{
    public RelayCommand OnboardingNextCommand { get; private set; } = null!;
    public RelayCommand OnboardingBackCommand { get; private set; } = null!;
    public RelayCommand OnboardingEndCommand { get; private set; } = null!;
    public RelayCommand ReplayIntroCommand { get; private set; } = null!;
    public RelayCommand UpdateLaterCommand { get; private set; } = null!;
    public RelayCommand UpdateSkipCommand { get; private set; } = null!;
    public RelayCommand UpdateDownloadCommand { get; private set; } = null!;
    public RelayCommand UpdateGitHubCommand { get; private set; } = null!;
    public RelayCommand ToggleUpdateCheckCommand { get; private set; } = null!;
    public RelayCommand CheckUpdateNowCommand { get; private set; } = null!;
    /// <summary>Escape: update card, then introduction, then the crash detail panel.</summary>
    public RelayCommand EscapeCommand { get; private set; } = null!;

    /// <summary>The capture harness and the self-test never reach the network.</summary>
    public static bool Offline { get; set; }

    void InitOverlays()
    {
        OnboardingNextCommand = new(() => { if (OnboardingStep >= 2) EndOnboarding(); else OnboardingStep++; return Task.CompletedTask; });
        OnboardingBackCommand = new(() => { if (OnboardingStep > 0) OnboardingStep--; return Task.CompletedTask; });
        OnboardingEndCommand = new(() => { EndOnboarding(); return Task.CompletedTask; });
        ReplayIntroCommand = new(() =>
        {
            _settings.SeenTips = "";
            _settings.Save();
            Page = TabHome;
            StartOnboarding();
            return Task.CompletedTask;
        });
        UpdateLaterCommand = new(() => { Update = null; return Task.CompletedTask; });
        UpdateSkipCommand = new(() =>
        {
            if (Update is null) return Task.CompletedTask;
            _settings.SkippedUpdateVersion = Update.Version.ToString(3);
            _settings.Save();
            Update = null;
            ShowToast("success", Loc.S("updates.this_version_won_t_be_offered"));
            return Task.CompletedTask;
        });
        UpdateDownloadCommand = new(() => { if (Update is { } u) _platform.Open(u.DownloadUrl); Update = null; return Task.CompletedTask; });
        UpdateGitHubCommand = new(() => { if (Update is { } u) _platform.Open(u.PageUrl); return Task.CompletedTask; });
        ToggleUpdateCheckCommand = new(() =>
        {
            _settings.UpdateCheckDisabled = !_settings.UpdateCheckDisabled;
            _settings.Save();
            Raise(nameof(UpdateCheckOn)); Raise(nameof(UpdateToggleText));
            return Task.CompletedTask;
        });
        CheckUpdateNowCommand = new(CheckUpdateNowAsync, () => !_checkingUpdate);
        EscapeCommand = new(() =>
        {
            if (Update is not null) Update = null;
            else if (OnboardingVisible) EndOnboarding();
            else CloseDetailCommand.Execute(null);
            return Task.CompletedTask;
        });
    }

    // ---------- Introduction (3 steps) ----------
    bool _onboardingVisible;
    public bool OnboardingVisible { get => _onboardingVisible; private set => Set(ref _onboardingVisible, value); }

    int _onboardingStep;
    public int OnboardingStep
    {
        get => _onboardingStep;
        set
        {
            Set(ref _onboardingStep, Math.Clamp(value, 0, 2));
            foreach (var n in new[] { nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(OnboardingKicker), nameof(OnboardingTitle),
                                      nameof(OnboardingBody), nameof(OnboardingNextText), nameof(OnboardingCanSkip), nameof(OnboardingCanBack) })
                Raise(n);
        }
    }
    public bool IsStep1 => OnboardingStep == 0;
    public bool IsStep2 => OnboardingStep == 1;
    public bool IsStep3 => OnboardingStep == 2;
    public string OnboardingKicker => Loc.S(OnboardingStep switch { 0 => "onboarding.welcome", 1 => "onboarding.step_2_of_3", _ => "onboarding.step_3_of_3" });
    public string OnboardingTitle => Loc.S(OnboardingStep switch { 0 => "onboarding.understand_why_your_pc_crashes", 1 => "onboarding.the_scan_is_already_running", _ => "onboarding.your_dashboard" });
    public string OnboardingBody => Loc.S(OnboardingStep switch { 0 => "onboarding.when_windows_crashes_it_leaves_traces", 1 => "onboarding.30_days_of_logs_dmp_files", _ => "onboarding.home_shows_a_30_day_stability" });
    public string OnboardingNextText => Loc.S(OnboardingStep >= 2 ? "onboarding.get_started" : "onboarding.next");
    public bool OnboardingCanSkip => OnboardingStep < 2;
    public bool OnboardingCanBack => OnboardingStep > 0;

    bool _tourTriggered;

    /// <summary>Windows: after the first scan, once, unless the tour was completed.</summary>
    void MaybeStartOnboarding()
    {
        if (_tourTriggered || _settings.HasCompletedTour) return;
        _tourTriggered = true;
        StartOnboarding();
    }

    public void StartOnboarding()
    {
        OnboardingStep = 0;
        OnboardingVisible = true;
    }

    void EndOnboarding()
    {
        OnboardingVisible = false;
        _settings.HasCompletedTour = true;
        _settings.Save();
        if (_pendingUpdate is not null) { Update = _pendingUpdate; _pendingUpdate = null; }
    }

    /// <summary>One tip per section, the first time it is opened after the tour (Windows OnUserSwitchedTab).</summary>
    void ShowSectionTip(int page)
    {
        if (!_settings.HasCompletedTour || Offline) return;
        var key = page switch
        {
            TabCrashes => "onboarding.tip_click_a_crash_to_open",
            TabHardware => "onboarding.tip_the_stability_test_tab_checks",
            TabTools => "onboarding.tip_report_export_gathers_the_whole",
            _ => null,
        };
        if (key is null) return;
        var id = "tab" + page;
        var seen = _settings.SeenTips.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (seen.Contains(id)) return;
        seen.Add(id);
        _settings.SeenTips = string.Join(",", seen);
        _settings.Save();
        ShowToast("accent", Loc.S(key));
    }

    // ---------- Updates ----------
    UpdateInfo? _update, _pendingUpdate;
    bool _checkingUpdate;

    /// <summary>The release shown in the card; null hides it.</summary>
    public UpdateInfo? Update
    {
        get => _update;
        private set
        {
            Set(ref _update, value);
            UpdateNotes.Clear();
            if (value is not null) foreach (var l in UpdateChecker.NoteLines(value.Notes)) UpdateNotes.Add(l);
            foreach (var n in new[] { nameof(UpdateVisible), nameof(UpdateTitle), nameof(UpdateSub), nameof(UpdateDownloadText), nameof(UpdateNoNotes) }) Raise(n);
        }
    }
    public bool UpdateVisible => Update is not null;
    /// <summary>Capture harness: shows the card for a sample release.</summary>
    public void PreviewUpdate(UpdateInfo info) => Update = info;
    public string UpdateTitle => Update is null ? "" : "Dump Finder " + Update.Version.ToString(3);
    public string UpdateSub => Update is null ? "" : Loc.S("updates.you_are_using_version") + UpdateChecker.CurrentVersionText
        + (Update.PublishedAt is { } d ? Loc.S("updates.released_on") + d.ToString("d", Loc.Culture) : "");
    public string UpdateDownloadText => Update is null ? "" : Loc.S("updates.download") + Update.Version.ToString(3);
    public ObservableCollection<NoteLine> UpdateNotes { get; } = new();
    public bool UpdateNoNotes => Update is not null && UpdateNotes.Count == 0;

    public bool UpdateCheckOn => !_settings.UpdateCheckDisabled;
    public string UpdateToggleText => Loc.Plain(Loc.S(UpdateCheckOn ? "sentinel.on" : "sentinel.off"));
    public string UpdateSettingText => Loc.F("updates.version_at_launch_the_app_checks", UpdateChecker.CurrentVersionText);

    /// <summary>Windows StartupUpdateCheck: once per launch, 3 s after the window opens, 15 s budget.</summary>
    public async Task StartupUpdateCheckAsync()
    {
        if (Offline || _settings.UpdateCheckDisabled) return;
        await Task.Delay(3000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var found = await UpdateChecker.CheckAsync(cts.Token);
        if (found is null || found.Version.ToString(3) == _settings.SkippedUpdateVersion) return;
        if (!_settings.HasCompletedTour || OnboardingVisible) _pendingUpdate = found;
        else Update = found;
    }

    async Task CheckUpdateNowAsync()
    {
        if (Offline) return;
        _checkingUpdate = true;
        CheckUpdateNowCommand.Refresh();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var latest = await UpdateChecker.GetLatestAsync(cts.Token);
            if (latest is null) ShowToast("warn", Loc.S("updates.could_not_reach_github_right_now"));
            else if (latest.Version > UpdateChecker.CurrentVersion) Update = latest;   // shown even if skipped
            else ShowToast("success", Loc.S("updates.you_already_have_the_latest_version") + UpdateChecker.CurrentVersionText + ").");
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateNowCommand.Refresh();
        }
    }
}
