using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DumpFinder.Mac.Core;
using WinCrashDoctor;

namespace DumpFinder.Mac;

// ---------- Rows shown in lists (one record per visual row) ----------

/// <summary>Tone of an icon tile / pill: Windows CategoryColors (danger = severe, warn = other faults, accent = info).</summary>
public abstract record Toned(string Tone)
{
    public bool IsDanger => Tone == "danger";
    public bool IsWarn => Tone == "warn";
    public bool IsSuccess => Tone == "success";
    public bool IsAccent => Tone == "accent";
}

public sealed record CrashRow(string Key, string Icon, string Tone, string Title, string TechLine, string When, string Category, bool IsNew, bool First,
                             bool IsSelected = false, string FileTag = "") : Toned(Tone)
{
    public bool HasFileTag => FileTag.Length > 0;
}

/// <summary>What the view model asks of the window: clipboard, file dialogs, opening links and files.</summary>
public interface IPlatform
{
    Task CopyTextAsync(string text);
    /// <summary>Save dialog; returns the chosen path (content already written) or null when cancelled.</summary>
    Task<string?> SaveTextAsync(string suggestedName, string extension, string content);
    Task<string?> PickFileAsync(string[] patterns);
    void Open(string pathOrUrl);
}

public sealed record ActionRow(string Icon, string Tone, string Title, string Sub, bool First) : Toned(Tone);

public sealed record CheckCard(string Tone, string Badge, string Category, string Title, string Detail, string Advice) : Toned(Tone)
{
    public bool HasAdvice => Advice.Length > 0;
}

public sealed record InfoRow(string Icon, string Primary, string Secondary, bool First)
{
    public bool HasSecondary => Secondary.Length > 0;
}

public sealed record ToastItem(string Tone, string Icon, string Text) : Toned(Tone);

public sealed class RelayCommand(Func<object?, Task> run, Func<bool>? canRun = null) : ICommand
{
    public RelayCommand(Func<Task> run, Func<bool>? canRun = null) : this(_ => run(), canRun) { }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await run(parameter); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>
/// Window state: navigation (same 7 sections and order as Windows), scan, Home, Crashes, Hardware, Settings.
/// Texts come from Loc (shared Windows Locales + Mac overrides) and are rebuilt when the language changes.
/// </summary>
public sealed partial class MainViewModel : Observable
{
    public const int TabHome = 0, TabCrashes = 1, TabHardware = 2, TabFixes = 3, TabOptimize = 4, TabTools = 5, TabSettings = 6;

    readonly AppSettings _settings;
    readonly IPlatform _platform;
    ScanResult? _result;
    StabilitySummary? _summary;
    HashSet<string> _newKeys = new();

    public MainViewModel(AppSettings settings, IPlatform platform)
    {
        _settings = settings;
        _platform = platform;
        _sidebarCollapsed = settings.SidebarCollapsed;
        _languageIndex = Math.Max(0, Array.IndexOf(Loc.Codes, Loc.Current));
        _themeIndex = Math.Clamp(settings.Theme, 0, 2);

        NavigateCommand = new RelayCommand(p => { Page = Convert.ToInt32(p); return Task.CompletedTask; });
        ScanCommand = new RelayCommand(() => ScanAsync(), () => !IsScanning);
        ReportCommand = new RelayCommand(SaveHtmlReportAsync, () => _result is not null);
        ToggleSidebarCommand = new RelayCommand(() => { SidebarCollapsed = !SidebarCollapsed; return Task.CompletedTask; });
        HeroPrimaryCommand = new RelayCommand(() => { Page = TabHardware; return Task.CompletedTask; });
        HeroSecondaryCommand = new RelayCommand(() => { if (_focusKey is not null) OpenCrash(_focusKey); return Task.CompletedTask; });
        InitCrashes();

        Loc.LanguageChanged += () => { RefreshTexts(); if (_result is not null) Fill(_result); RefreshCrashTexts(); };
        RefreshTexts();
    }

    public Loc L => Loc.Instance;
    public RelayCommand NavigateCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand ReportCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand HeroPrimaryCommand { get; }
    public RelayCommand HeroSecondaryCommand { get; }

    // ---------- Navigation ----------
    int _page;
    public int Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, Math.Clamp(value, 0, 6))) return;
            foreach (var n in new[] { nameof(IsHome), nameof(IsCrashes), nameof(IsHardware), nameof(IsFixes), nameof(IsOptimize), nameof(IsTools), nameof(IsSettings), nameof(IsSoon), nameof(SoonTitle) })
                Raise(n);
        }
    }
    public bool IsHome => Page == TabHome;
    public bool IsCrashes => Page == TabCrashes;
    public bool IsHardware => Page == TabHardware;
    public bool IsFixes => Page == TabFixes;
    public bool IsOptimize => Page == TabOptimize;
    public bool IsTools => Page == TabTools;
    public bool IsSettings => Page == TabSettings;
    /// <summary>Sections not adapted to macOS yet (Fixes, Optimize, Tools): shared "coming soon" page.</summary>
    public bool IsSoon => Page is TabFixes or TabOptimize or TabTools;
    public string SoonTitle => Loc.Plain(Loc.S(Page switch { TabFixes => "main_window.fixes", TabOptimize => "main_window.optimize", _ => "main_window.tools" }));

    bool _sidebarCollapsed;
    public bool SidebarCollapsed
    {
        get => _sidebarCollapsed;
        set
        {
            if (!Set(ref _sidebarCollapsed, value)) return;
            Raise(nameof(SidebarExpanded));
            Raise(nameof(SidebarWidth));
            Raise(nameof(ShowHardwareBadge));
            _settings.SidebarCollapsed = value;
            _settings.Save();
        }
    }
    public bool SidebarExpanded => !SidebarCollapsed;
    /// <summary>Windows: 236 px expanded, 68 px collapsed.</summary>
    public double SidebarWidth => SidebarCollapsed ? 68 : 236;

    // ---------- Scan + status line ----------
    bool _isScanning;
    public bool IsScanning { get => _isScanning; private set { Set(ref _isScanning, value); ScanCommand.Refresh(); } }

    string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <param name="maxAgeDays">90 days by default; Crashes › "All history" asks for more.</param>
    public async Task ScanAsync(int maxAgeDays = 90)
    {
        IsScanning = true;
        Status = Loc.S("mac.status.scanning");
        try
        {
            var r = await DiagnosticsEngine.ScanAsync(maxAgeDays: maxAgeDays);
            _result = r;
            var summary = CrashTimeline.Build(r, DetectionData.Embedded, DateTime.Now);
            _newKeys = History.MarkSeen(summary.Crashes.Select(c => c.Key));
            Fill(r, summary);
            History.AddScan(new ScanRecord
            {
                Time = r.ScannedAt.LocalDateTime, Score = summary.Score, Crashes30 = summary.Crashes30,
                Severe30 = summary.Severe30, Diagnosis = summary.VerdictTitle,
            });
        }
        catch (Exception e)
        {
            Status = e.Message;
            ShowToast("danger", e.Message);
        }
        finally
        {
            IsScanning = false;
            ReportCommand.Refresh();
        }
    }

    async Task SaveHtmlReportAsync()
    {
        if (_result is null || _summary is null) return;
        var html = ReportBuilder.Html(_result, _summary);
        var path = await _platform.SaveTextAsync($"DumpFinder-report-{DateTime.Now:yyyy-MM-dd}", "html", html);
        if (path is null) return;
        ShowToast("success", Loc.S("mac.toast.report_saved"));
        _platform.Open(path);
    }

    // ---------- Sidebar badge (Windows: count of Warning + Critical checks on Hardware, hidden when collapsed) ----------
    string _hardwareBadge = "";
    public string HardwareBadge { get => _hardwareBadge; private set { Set(ref _hardwareBadge, value); Raise(nameof(ShowHardwareBadge)); } }
    public bool ShowHardwareBadge => HardwareBadge.Length > 0 && SidebarExpanded;

    // ---------- Sidebar hardware box ----------
    string _hwBadge = "Apple Silicon", _hwLine1 = "", _hwLine2 = "";
    public string HwBadge { get => _hwBadge; private set => Set(ref _hwBadge, value); }
    public string HwLine1 { get => _hwLine1; private set => Set(ref _hwLine1, value); }
    public string HwLine2 { get => _hwLine2; private set => Set(ref _hwLine2, value); }

    // ---------- Home ----------
    string _greeting = "", _homeSub = "", _scoreText = "—", _levelText = "", _levelTone = "", _deltaText = "", _deltaTone = "";
    string _verdictTitle = "", _verdictBody = "", _heroPrimary = "", _heroSecondary = "";
    double _score;
    List<StabilityDay> _days = new();
    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }
    public string HomeSubtitle { get => _homeSub; private set => Set(ref _homeSub, value); }
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    public double Score { get => _score; private set => Set(ref _score, value); }
    public string LevelText { get => _levelText; private set => Set(ref _levelText, value); }
    public string LevelTone { get => _levelTone; private set { Set(ref _levelTone, value); Raise(nameof(LevelSuccess)); Raise(nameof(LevelWarn)); Raise(nameof(LevelDanger)); } }
    public bool LevelSuccess => LevelTone == "success";
    public bool LevelWarn => LevelTone == "warn";
    public bool LevelDanger => LevelTone == "danger";
    public bool HasLevel => LevelTone.Length > 0;
    public string DeltaText { get => _deltaText; private set { Set(ref _deltaText, value); Raise(nameof(HasDelta)); } }
    public bool HasDelta => DeltaText.Length > 0;
    public string DeltaTone { get => _deltaTone; private set { Set(ref _deltaTone, value); Raise(nameof(DeltaUp)); Raise(nameof(DeltaDown)); } }
    public bool DeltaUp => DeltaTone == "success";
    public bool DeltaDown => DeltaTone == "danger";
    public string VerdictTitle { get => _verdictTitle; private set => Set(ref _verdictTitle, value); }
    public string VerdictBody { get => _verdictBody; private set => Set(ref _verdictBody, value); }
    public string HeroPrimary { get => _heroPrimary; private set { Set(ref _heroPrimary, value); Raise(nameof(HasHeroPrimary)); } }
    public bool HasHeroPrimary => HeroPrimary.Length > 0;
    public string HeroSecondary { get => _heroSecondary; private set { Set(ref _heroSecondary, value); Raise(nameof(HasHeroSecondary)); } }
    public bool HasHeroSecondary => HeroSecondary.Length > 0;
    public List<StabilityDay> Days { get => _days; private set => Set(ref _days, value); }

    public ObservableCollection<ActionRow> Actions { get; } = new();
    public ObservableCollection<CrashRow> Recent { get; } = new();
    bool _noActions, _noRecent;
    public bool NoActions { get => _noActions; private set => Set(ref _noActions, value); }
    public bool NoRecent { get => _noRecent; private set => Set(ref _noRecent, value); }

    // ---------- Crashes / Hardware ----------
    public ObservableCollection<CheckCard> Checks { get; } = new();
    public ObservableCollection<InfoRow> Machine { get; } = new();
    public ObservableCollection<InfoRow> Extensions { get; } = new();
    bool _noExtension;
    public bool NoExtension { get => _noExtension; private set => Set(ref _noExtension, value); }

    // ---------- Settings ----------
    public string[] Languages => Loc.Natives;
    public string[] ThemeOptions { get; private set; } = Array.Empty<string>();

    int _languageIndex;
    public int LanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (value < 0 || !Set(ref _languageIndex, value)) return;
            _settings.Language = Loc.Codes[value];
            _settings.Save();
            Loc.SetLanguage(Loc.Codes[value]);
        }
    }

    int _themeIndex;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (value < 0 || !Set(ref _themeIndex, value)) return;
            _settings.Theme = value;
            _settings.Save();
            App.ApplyTheme(value);
        }
    }

    public string VersionText => Loc.F("mac.settings.version",
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?");
    public string VersionShort => Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "";

    // ---------- Toasts (Windows: bottom-right, 4.5 / 6 / 8 s by kind) ----------
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void ShowToast(string tone, string text)
    {
        if (Toasts.Any(t => t.Text == text)) return;
        var item = new ToastItem(tone, tone switch { "success" => "Completed", "danger" => "ErrorBadge", "warn" => "Warning", _ => "Lightbulb" }, text);
        Toasts.Add(item);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);
        int ms = tone switch { "success" => 4500, "warn" => 6000, _ => 8000 };
        _ = Task.Delay(ms).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Remove(item)));
    }

    // ---------- Filling ----------
    void RefreshTexts()
    {
        int h = DateTime.Now.Hour;
        Greeting = Loc.S(h >= 18 || h < 5 ? "home.good_evening" : "home.hello");
        ThemeOptions = new[] { Loc.S("main_window.system_windows"), Loc.S("main_window.dark_mode"), Loc.S("main_window.light_mode") };
        Raise(nameof(ThemeOptions));
        // The ComboBox drops its selection when its items are replaced: put it back.
        var t = _themeIndex; _themeIndex = -1; Raise(nameof(ThemeIndex)); _themeIndex = t; Raise(nameof(ThemeIndex));
        Raise(nameof(VersionText));
        Raise(nameof(SoonTitle));
    }

    void Fill(ScanResult r) => Fill(r, CrashTimeline.Build(r, DetectionData.Embedded, DateTime.Now));

    void Fill(ScanResult r, StabilitySummary s)
    {
        _summary = s;
        var now = DateTime.Now;
        var hw = r.Hardware;
        HwBadge = hw.Chip.Length > 0 ? hw.Chip : "Apple Silicon";
        HwLine1 = hw.MachineName.Length > 0 ? $"{hw.MachineName} · {hw.Memory}" : hw.Memory;
        HwLine2 = hw.OsVersion;

        Status = Loc.F("mac.status.last_scan", r.ScannedAt.LocalDateTime, s.Crashes30);
        HomeSubtitle = Loc.F("home.last_scan_0_hh_mm_last", r.ScannedAt.LocalDateTime, hw.Chip.Length > 0 ? hw.Chip : "Mac");

        // Score ring, level and delta (Windows HomePage.RenderHome)
        Score = s.Score;
        ScoreText = s.Score.ToString(Loc.Culture);
        (LevelText, LevelTone) = s.Level switch
        {
            StabilityLevel.Stable => (Loc.Plain(Loc.S("home.stable")), "success"),
            StabilityLevel.Fragile => (Loc.Plain(Loc.S("home.fragile")), "warn"),
            _ => (Loc.Plain(Loc.S("home.unstable")), "danger"),
        };
        Raise(nameof(HasLevel));
        var prev = History.PreviousDay(now);
        if (prev is not null && prev.Score != s.Score)
        {
            int d = s.Score - prev.Score;
            DeltaText = (d > 0 ? "↑ +" : "↓ ") + d + Loc.S("home.since") + Loc.DayMonth(prev.Time);
            DeltaTone = d > 0 ? "success" : "danger";
        }
        else DeltaText = "";

        VerdictTitle = s.VerdictTitle;
        VerdictBody = s.VerdictBody;
        Days = s.Days;

        var warnings = r.Findings.Where(f => f.Severity >= Severity.Warning).ToList();
        HardwareBadge = warnings.Count > 0 ? warnings.Count.ToString(Loc.Culture) : "";
        HeroPrimary = warnings.Count > 0 ? Loc.P("mac.home.see_findings", warnings.Count) : "";
        var focus = s.Crashes.FirstOrDefault(c => (now - c.Time).TotalDays <= Stability.WindowDays && Stability.IsSevere(c.Category))
                    ?? s.Crashes.FirstOrDefault(c => (now - c.Time).TotalDays <= Stability.WindowDays);
        HeroSecondary = focus is not null ? Loc.S("home.view_crash") : "";

        // Recommended actions: the 3 most serious checks (Windows: top 3 recommended fixes)
        Actions.Clear();
        int i = 0;
        foreach (var f in warnings.Take(3))
            Actions.Add(new ActionRow(CategoryIcon(f.Category), f.Severity == Severity.Critical ? "danger" : "warn", f.Title, Loc.S("mac.check_cat." + f.Category), i++ == 0));
        NoActions = Actions.Count == 0;

        Recent.Clear();
        i = 0;
        foreach (var c in s.Crashes.Take(4)) Recent.Add(ToRow(c, now, i++ == 0));
        NoRecent = Recent.Count == 0;
        _focusKey = focus?.Key;
        RenderCrashes();
        RenderKb();

        Checks.Clear();
        foreach (var f in r.Findings)
            Checks.Add(new CheckCard(
                f.Severity switch { Severity.Critical => "danger", Severity.Warning => "warn", Severity.Ok => "success", _ => "accent" },
                ReportBuilder.LevelName(f.Severity), Loc.S("mac.check_cat." + f.Category), f.Title, f.Detail, f.Advice));

        Machine.Clear();
        i = 0;
        void M(string icon, string a, string b) => Machine.Add(new InfoRow(icon, a, b, i++ == 0));
        M("Component", $"{hw.MachineName} ({hw.ModelId})", $"{hw.Chip} · {hw.Memory}");
        M("Settings", hw.OsVersion, hw.Kernel);
        if (r.Boot.BootTime is { } bt)
            M("Reboot", bt.LocalDateTime.ToString("g", Loc.Culture), r.Boot.ShutdownReason);
        foreach (var d in hw.Drives) M("Save", d.Name, "SMART " + d.SmartStatus);
        if (hw.BootVolumeSize is long size && hw.BootVolumeFree is long free)
            M("Save", $"{free / 1e9:0.#} GB / {size / 1e9:0.#} GB", $"{100.0 * free / size:0.#} %");
        if (hw.HasBattery) M("Lightning", $"{hw.BatteryMaxCapacityPct} %", $"{hw.BatteryCycles} cycles · {hw.BatteryCondition}");
        M("Shield", "SIP " + OnOff(hw.SipEnabled), "Gatekeeper " + OnOff(hw.GatekeeperEnabled));
        foreach (var e in r.Errors) M("Warning", Loc.S("mac.hardware.not_read"), e);

        Extensions.Clear();
        i = 0;
        foreach (var k in r.KernelExtensions)
            Extensions.Add(new InfoRow("DevTools", $"{k.BundleId} {k.Version}".Trim(), k.Loaded ? "kext · loaded" : "kext · " + k.Path, i++ == 0));
        foreach (var x in r.SystemExtensions)
            Extensions.Add(new InfoRow("DevTools", $"{x.BundleId} {x.Version}", $"system extension · {x.State}", i++ == 0));
        NoExtension = Extensions.Count == 0;
    }

    CrashRow ToRow(CrashEntry c, DateTime now, bool first)
    {
        var (tone, icon) = c.Category switch
        {
            CrashCategory.BSOD => ("danger", "Warning"),
            CrashCategory.UnexpectedReboot => ("danger", "Reboot"),
            CrashCategory.HardwareWhea => ("danger", "Component"),
            CrashCategory.GpuDriver => ("warn", "Video"),
            CrashCategory.StorageNvme => ("warn", "Save"),
            CrashCategory.MemoryExhaustion => ("warn", "Diagnostic"),
            CrashCategory.BrowserGpu => ("warn", "Globe"),
            _ => ("accent", "ErrorBadge"),
        };
        string fileTag = c.Panic is not null ? ".panic" : c.Report is not null ? ".ips" : "";
        return new CrashRow(c.Key, icon, tone, c.Title, c.TechLine, RelativeTime(c.Time, now), CrashTimeline.CategoryName(c.Category), _newKeys.Contains(c.Key), first,
                            c.Key == _selectedKey, fileTag);
    }

    /// <summary>Windows HomePage.RelativeTime.</summary>
    static string RelativeTime(DateTime t, DateTime now)
    {
        var d = now - t;
        if (d.TotalMinutes < 60) return Loc.F("home.min_ago", Math.Max(1, (int)d.TotalMinutes));
        if (t.Date == now.Date) return t.ToString("HH:mm");
        if (t.Date == now.Date.AddDays(-1)) return Loc.S("home.yesterday") + t.ToString("HH:mm");
        return t.ToString("d MMM HH:mm", Loc.Culture);
    }

    static string CategoryIcon(string category) => category switch
    {
        "crashes" => "Warning",
        "memory" => "Diagnostic",
        "drivers" => "DevTools",
        "hardware" => "Component",
        "system" => "Shield",
        _ => "Lightbulb",
    };

    static string OnOff(bool? b) => b is null ? "?" : b.Value ? "on" : "off";
}
