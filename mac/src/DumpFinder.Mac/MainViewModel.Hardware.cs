using System.Collections.ObjectModel;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>
/// One Hardware card (Windows BuildHealthInfoCard): tag chip, title, badge, value, detail,
/// "Learn more" drawer and quick action.
/// </summary>
public sealed class HealthCardRow(HealthCard card, RelayCommand action) : Observable
{
    public string Tone { get; } = card.Level switch { HealthLevel.Critical => "danger", HealthLevel.Warning => "warn", HealthLevel.Good => "success", _ => "accent" };
    public bool IsDanger => Tone == "danger";
    public bool IsWarn => Tone == "warn";
    public bool IsSuccess => Tone == "success";
    public bool IsAccent => Tone == "accent";
    /// <summary>Windows Localizer.HealthBadgeText; "Info" is not translated on Windows either.</summary>
    public string Badge { get; } = card.Level switch
    {
        HealthLevel.Good => Loc.S("localizer.healthy"), HealthLevel.Warning => Loc.S("localizer.check"),
        HealthLevel.Critical => Loc.S("localizer.critical"), _ => "Info",
    };
    public string Tag => card.Tag;
    public string Title => card.Title;
    public string Value => card.Value;
    public bool HasValue => card.Value.Length > 0;
    public string Detail => card.Detail;
    public string GuideTitle => card.GuideTitle;
    public string GuideSteps => card.GuideSteps;
    public bool HasGuide => card.GuideSteps.Length > 0;
    public string ActionLabel => card.ActionLabel;
    public bool HasAction => card.ActionTarget.Length > 0;
    public bool HasActions => HasGuide || HasAction;
    public string ActionTarget => card.ActionTarget;
    public RelayCommand ActionCommand => action;

    bool _guideOpen;
    public bool GuideOpen { get => _guideOpen; set { Set(ref _guideOpen, value); Raise(nameof(GuideLabel)); } }
    public string GuideLabel => Loc.Plain(Loc.S(GuideOpen ? "health.hide_guide" : "health.learn_more"));
    public RelayCommand ToggleGuideCommand => new(() => { GuideOpen = !GuideOpen; return Task.CompletedTask; });
}

/// <summary>A row of Hardware › Extensions (Windows Drivers row).</summary>
public sealed record ExtensionRow(string Icon, string Tone, string Name, string Meta, string Blamed, bool NotLoaded, bool First, string Query) : Toned(Tone)
{
    public bool HasBlamed => Blamed.Length > 0;
}

/// <summary>A launcher tile of Tools › macOS tools (Windows WinToolsPage item).</summary>
public sealed record ToolItem(string Title, string Description, string Target);

public sealed record ToolCategory(string Icon, string Title, List<ToolItem> Items);

/// <summary>A day of Tools › History (Windows HistoryPage: date, score pill, crashes).</summary>
public sealed record HistoryRow(string Date, string Score, string Tone, string Sub) : Toned(Tone);

/// <summary>Hardware (Hardware · Conflicts · Extensions) and Tools (macOS tools · Report &amp; export · History).</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<HealthCardRow> HardwareCards { get; } = new();
    public ObservableCollection<HealthCardRow> ConflictCards { get; } = new();
    public ObservableCollection<ExtensionRow> ExtensionRows { get; } = new();
    public ObservableCollection<ToolCategory> ToolCategories { get; } = new();
    public ObservableCollection<HistoryRow> HistoryRows { get; } = new();

    public RelayCommand HardwareTabCommand { get; private set; } = null!;
    public RelayCommand ToolsTabCommand { get; private set; } = null!;
    public RelayCommand CardActionCommand { get; private set; } = null!;
    public RelayCommand SearchUpdateCommand { get; private set; } = null!;
    public RelayCommand LaunchToolCommand { get; private set; } = null!;
    public RelayCommand ExportHtmlCommand { get; private set; } = null!;
    public RelayCommand ExportJsonCommand { get; private set; } = null!;
    public RelayCommand CopyMarkdownCommand { get; private set; } = null!;
    public RelayCommand CopyTextCommand { get; private set; } = null!;
    public RelayCommand SaveTextCommand { get; private set; } = null!;

    void InitHardwareTools()
    {
        HardwareTabCommand = new(p => { HardwareTab = Convert.ToInt32(p); return Task.CompletedTask; });
        ToolsTabCommand = new(p => { ToolsTab = Convert.ToInt32(p); return Task.CompletedTask; });
        CardActionCommand = new(p => { if (p is string target) RunAction(target); return Task.CompletedTask; });
        SearchUpdateCommand = new(p =>
        {
            if (p is string q) _platform.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
            return Task.CompletedTask;
        });
        LaunchToolCommand = new(p =>
        {
            if (p is not ToolItem tool) return Task.CompletedTask;
            _platform.Open(tool.Target);
            Status = Loc.S("win_tools.launched_tool") + tool.Title;
            return Task.CompletedTask;
        });
        bool HasReport() => _result is not null;
        ExportHtmlCommand = new(SaveHtmlReportAsync, HasReport);
        ExportJsonCommand = new(async () =>
        {
            if (_result is null || _summary is null) return;
            var path = await _platform.SaveTextAsync($"DumpFinder_{DateTime.Now:yyyyMMdd_HHmm}", "json", ReportBuilder.Json(_result, _summary));
            if (path is not null) ShowToast("success", Loc.S("reports.json_export_saved") + Path.GetFileName(path));
        }, HasReport);
        CopyMarkdownCommand = new(async () =>
        {
            if (_result is null || _summary is null) return;
            await _platform.CopyTextAsync(ReportBuilder.Markdown(_result, _summary));
            ShowToast("success", Loc.S("reports.markdown_summary_copied_ready_to_paste"));
        }, HasReport);
        CopyTextCommand = new(async () =>
        {
            if (ReportText.Length == 0) return;
            await _platform.CopyTextAsync(ReportText);
            ShowToast("success", Loc.S("main_window.report_copied_to_clipboard"));
        }, HasReport);
        SaveTextCommand = new(async () =>
        {
            if (ReportText.Length == 0) return;
            var path = await _platform.SaveTextAsync($"DumpFinder_Report_{DateTime.Now:yyyyMMdd_HHmm}", "txt", ReportText);
            if (path is not null) ShowToast("success", Loc.S("mac.toast.saved") + Path.GetFileName(path));
        }, HasReport);
        RenderTools();
        RenderHistory();
    }

    void RefreshReportCommands()
    {
        foreach (var c in new[] { ExportHtmlCommand, ExportJsonCommand, CopyMarkdownCommand, CopyTextCommand, SaveTextCommand }) c.Refresh();
    }

    // ---------- In-app navigation (Windows nav: commands) ----------

    /// <summary>"nav:drivers", "nav:crashes"… inside the app; anything else is opened by macOS (app, settings pane, link).</summary>
    void RunAction(string target)
    {
        if (!target.StartsWith("nav:", StringComparison.Ordinal)) { _platform.Open(target); return; }
        switch (target[4..])
        {
            case "home": Page = TabHome; break;
            case "crashes": CrashTab = 0; Page = TabCrashes; break;
            case "hardware": HardwareTab = 0; Page = TabHardware; break;
            case "conflicts": HardwareTab = 1; Page = TabHardware; break;
            case "drivers": HardwareTab = 2; Page = TabHardware; break;
            case "tools": Page = TabTools; break;
            case "history": ToolsTab = 2; Page = TabTools; break;
            default: ShowToast("warn", Loc.S("health.unable_to_open_action") + target); break;
        }
    }

    // ---------- Hardware sub-tabs: 0 Hardware · 1 Conflicts · 2 Extensions (Windows sub 0, 1, 3) ----------
    int _hardwareTab;
    public int HardwareTab
    {
        get => _hardwareTab;
        set { Set(ref _hardwareTab, Math.Clamp(value, 0, 2)); Raise(nameof(IsHwMain)); Raise(nameof(IsHwConflicts)); Raise(nameof(IsHwExtensions)); }
    }
    public bool IsHwMain => HardwareTab == 0;
    public bool IsHwConflicts => HardwareTab == 1;
    public bool IsHwExtensions => HardwareTab == 2;

    string _extensionSummary = "";
    public string ExtensionSummary { get => _extensionSummary; private set => Set(ref _extensionSummary, value); }
    bool _noExtensionRows = true;
    public bool NoExtensionRows { get => _noExtensionRows; private set => Set(ref _noExtensionRows, value); }

    void RenderHardware(ScanResult r)
    {
        var cards = HardwareChecks.Build(r, DetectionData.Embedded, DateTimeOffset.Now);
        HardwareCards.Clear();
        ConflictCards.Clear();
        foreach (var c in cards)
            (c.Tab == HardwareChecks.TabHardware ? HardwareCards : ConflictCards).Add(new HealthCardRow(c, CardActionCommand));
        // Windows: sidebar badge = Warning + Critical cards of Hardware and Conflicts.
        int bad = cards.Count(c => c.Level >= HealthLevel.Warning);
        HardwareBadge = bad > 0 ? bad.ToString(Loc.Culture) : "";

        var ext = HardwareChecks.Extensions(r);
        ExtensionRows.Clear();
        int i = 0;
        foreach (var e in ext)
        {
            var kind = e.Kernel ? "kext" : "system extension";
            var meta = string.Join(" · ", new[] { kind, e.Version, e.Kernel && e.Loaded ? "loaded" : "", e.Detail }.Where(s => s.Length > 0));
            ExtensionRows.Add(new ExtensionRow("DevTools", e.Crashes > 0 ? "danger" : "", e.BundleId, meta,
                e.Crashes > 0 ? Loc.P("hosting.blamed_for_crashes", e.Crashes) : "", !e.Loaded, i++ == 0, e.BundleId + " macOS update"));
        }
        NoExtensionRows = ExtensionRows.Count == 0;
        ExtensionSummary = Loc.F("mac.ext.summary", ext.Count, ext.Count(e => !e.Loaded), ext.Count(e => e.Crashes > 0));
    }

    // ---------- Tools sub-tabs: 0 macOS tools · 1 Report & export · 2 History ----------
    int _toolsTab;
    public int ToolsTab
    {
        get => _toolsTab;
        set
        {
            Set(ref _toolsTab, Math.Clamp(value, 0, 2));
            Raise(nameof(IsToolsApps)); Raise(nameof(IsToolsReport)); Raise(nameof(IsToolsHistory));
            if (value == 2) RenderHistory();
        }
    }
    public bool IsToolsApps => ToolsTab == 0;
    public bool IsToolsReport => ToolsTab == 1;
    public bool IsToolsHistory => ToolsTab == 2;

    string _reportText = "";
    public string ReportText { get => _reportText; private set => Set(ref _reportText, value); }

    /// <summary>The four Windows categories, with the macOS tool that does the same job.</summary>
    void RenderTools()
    {
        const string utilities = "/System/Applications/Utilities/";
        ToolItem T(string key, string target) => new(Loc.S($"mac.tools.{key}"), Loc.S($"mac.tools.{key}_desc"), target);
        ToolCategories.Clear();
        ToolCategories.Add(new("Diagnostic", Loc.S("mac.tools.cat_logs"), new()
        {
            T("console", utilities + "Console.app"),
            T("reports_folder", DiagnosticReports.UserDir),
        }));
        ToolCategories.Add(new("Component", Loc.S("mac.tools.cat_hardware"), new()
        {
            T("system_information", utilities + "System Information.app"),
            T("login_items", HardwareChecks.LoginItemsPane),
        }));
        ToolCategories.Add(new("Lightning", Loc.S("mac.tools.cat_resources"), new()
        {
            T("activity_monitor", HardwareChecks.ActivityMonitor),
            T("disk_utility", HardwareChecks.DiskUtility),
        }));
        ToolCategories.Add(new("Shield", Loc.S("mac.tools.cat_integrity"), new()
        {
            T("software_update", HardwareChecks.SoftwareUpdatePane),
            T("privacy_security", HardwareChecks.PrivacyPane),
        }));
    }

    // ---------- History (Windows HistoryPage: trend card, last 8 days) ----------
    string _trendSummary = "";
    public string TrendSummary { get => _trendSummary; private set => Set(ref _trendSummary, value); }
    List<double> _trend = new();
    /// <summary>Score of the last scan of each day, oldest first (sparkline, hidden under 2 days).</summary>
    public List<double> Trend { get => _trend; private set { Set(ref _trend, value); Raise(nameof(HasTrend)); } }
    public bool HasTrend => Trend.Count >= 2;

    void RenderHistory()
    {
        var days = History.Scans().GroupBy(s => s.Time.Date).OrderBy(g => g.Key)
                          .Select(g => g.OrderBy(s => s.Time).Last()).ToList();
        Trend = days.Select(d => (double)d.Score).ToList();
        if (days.Count == 0) TrendSummary = Loc.S("history.history_builds_up_with_every_scan");
        else if (days.Count == 1) TrendSummary = Loc.F("history.first_scan_recorded_100_come_back", days[0].Score);
        else
        {
            var first = days[0];
            var last = days[^1];
            int delta = last.Score - first.Score;
            TrendSummary = Loc.F("history.trend_summary", last.Score, first.Score, Loc.DayMonth(first.Time),
                delta >= 0 ? "+" : "−", Math.Abs(delta), (int)(last.Time.Date - first.Time.Date).TotalDays + 1);
        }
        HistoryRows.Clear();
        foreach (var d in Enumerable.Reverse(days).Take(8))
        {
            var tone = d.Score >= 85 ? "success" : d.Score >= 60 ? "warn" : "danger";   // Windows Stability.LevelOf
            HistoryRows.Add(new HistoryRow(d.Time.ToString("ddd d MMM", Loc.Culture), $"{d.Score}/100", tone, Loc.F("history.crash_es_in_30_d", d.Crashes30)));
        }
    }
}
