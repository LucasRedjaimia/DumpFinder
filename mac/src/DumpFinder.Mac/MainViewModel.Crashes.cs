using System.Collections.ObjectModel;
using DumpFinder.Mac.Core;
using WinCrashDoctor;

namespace DumpFinder.Mac;

/// <summary>One "Common crashes" article; the card expands in place (first one open, like Windows).</summary>
public sealed class KbRow(KnowledgeEntry entry, bool expanded) : Observable
{
    public string Tag => entry.Tag;
    public Avalonia.Media.IBrush Color { get; } = Avalonia.Media.Brush.Parse(entry.BadgeColorHex);
    public string Title => entry.Title;
    public string Symptoms => entry.Symptoms;
    public string Cause => entry.Cause;
    public string Fix => entry.Solution;
    bool _expanded = expanded;
    public bool Expanded { get => _expanded; set { Set(ref _expanded, value); Raise(nameof(Chevron)); } }
    public string Chevron => Expanded ? "▴" : "▾";
    public RelayCommand ToggleCommand => new(() => { Expanded = !Expanded; return Task.CompletedTask; });
}

/// <summary>Crashes section (Windows: MainWindow.CrashesPage.cs): History | Common crashes, filters, list, detail panel.</summary>
public sealed partial class MainViewModel
{
    string? _selectedKey, _focusKey;
    /// <summary>Reports opened by hand ("Open a .panic / .ips…"), kept on top of the scanned ones.</summary>
    readonly List<CrashEntry> _opened = new();

    public RelayCommand SelectCrashCommand { get; private set; } = null!;
    public RelayCommand OpenCrashCommand { get; private set; } = null!;
    public RelayCommand CloseDetailCommand { get; private set; } = null!;
    public RelayCommand CopyForumCommand { get; private set; } = null!;
    public RelayCommand OpenReportFileCommand { get; private set; } = null!;
    public RelayCommand SearchExtensionCommand { get; private set; } = null!;
    public RelayCommand RevealFileCommand { get; private set; } = null!;
    public RelayCommand CrashTabCommand { get; private set; } = null!;
    public RelayCommand ToggleKbFilterCommand { get; private set; } = null!;

    void InitCrashes()
    {
        SelectCrashCommand = new(p => { _selectedKey = p as string; RenderCrashes(); return Task.CompletedTask; });
        OpenCrashCommand = new(p => { if (p is string k) OpenCrash(k); return Task.CompletedTask; });
        CloseDetailCommand = new(() => { _selectedKey = null; DetailVisible = false; RenderCrashes(keepClosed: true); return Task.CompletedTask; });
        CopyForumCommand = new(CopyForumAsync);
        OpenReportFileCommand = new(OpenReportFileAsync);
        SearchExtensionCommand = new(() =>
        {
            if (DetailExtension.Length > 0)
                _platform.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(DetailExtension + " macOS update"));
            return Task.CompletedTask;
        });
        RevealFileCommand = new(() => { if (_detailFile.Length > 0) _platform.Open(Path.GetDirectoryName(_detailFile)!); return Task.CompletedTask; });
        CrashTab = 0;
        CrashTabCommand = new(p => { CrashTab = Convert.ToInt32(p); return Task.CompletedTask; });
        ToggleKbFilterCommand = new(() => { KbOnlyMine = !KbOnlyMine; RenderKb(); return Task.CompletedTask; });
        RenderKb();
    }

    /// <summary>From Home (recent crash, "View crash"): go to Crashes › History with that crash open.</summary>
    public void OpenCrash(string key)
    {
        Page = TabCrashes;
        CrashTab = 0;
        if (_summary is not null && !AllCrashes().Where(c => c.Key == key).Any(c => InPeriod(c) && InCategory(c)))
        {
            // Make sure the crash is visible: widen the filters rather than hiding it.
            _periodIndex = 3; Raise(nameof(PeriodIndex));
            _categoryIndex = 0; Raise(nameof(CategoryIndex));
        }
        _selectedKey = key;
        RenderCrashes();
    }

    // ---------- Tabs ----------
    int _crashTab;
    public int CrashTab
    {
        get => _crashTab;
        set { Set(ref _crashTab, value); Raise(nameof(IsHistoryTab)); Raise(nameof(IsKbTab)); }
    }
    public bool IsHistoryTab => CrashTab == 0;
    public bool IsKbTab => CrashTab == 1;
    string _historyTabLabel = "";
    public string HistoryTabLabel { get => _historyTabLabel; private set => Set(ref _historyTabLabel, value); }

    // ---------- Filters (same choices as Windows; Mac wording in mac/Locales) ----------
    public string[] PeriodOptions => new[] { "main_window.since_last_boot", "main_window.last_3_days", "main_window.last_30_days", "main_window.all_history" }.Select(k => Loc.Plain(Loc.S(k))).ToArray();
    public string[] CategoryOptions => new[] { "main_window.all_categories", "main_window.bsod_reboots", "main_window.graphics_driver_gpu", "main_window.nvme_ssd_storage", "main_window.whea_hardware_ram", "main_window.browsers_applications" }.Select(k => Loc.Plain(Loc.S(k))).ToArray();

    int _periodIndex = 2, _categoryIndex;
    public int PeriodIndex
    {
        get => _periodIndex;
        set
        {
            if (value < 0 || value == _periodIndex) return;
            int previous = _periodIndex;
            _periodIndex = value;
            Raise();
            // The scan reads 90 days; only "All history" needs a deeper read (Windows: same rule).
            if (value == 3 && previous != 3) _ = ScanAsync(3650);
            else RenderCrashes();
        }
    }
    public int CategoryIndex
    {
        get => _categoryIndex;
        set { if (value < 0 || value == _categoryIndex) return; _categoryIndex = value; Raise(); RenderCrashes(); }
    }

    IEnumerable<CrashEntry> AllCrashes() => _opened.Concat(_summary?.Crashes ?? new List<CrashEntry>());

    bool InPeriod(CrashEntry c)
    {
        if (_opened.Contains(c)) return true;
        var now = DateTime.Now;
        return _periodIndex switch
        {
            0 => _result?.Boot.BootTime is not { } bt || c.Time >= bt.LocalDateTime,
            1 => (now - c.Time).TotalDays <= 3,
            2 => (now - c.Time).TotalDays <= 30,
            _ => true,
        };
    }

    bool InCategory(CrashEntry c) => _categoryIndex switch
    {
        1 => c.Category is CrashCategory.BSOD or CrashCategory.UnexpectedReboot,
        2 => c.Category == CrashCategory.GpuDriver,
        3 => c.Category == CrashCategory.StorageNvme,
        4 => c.Category is CrashCategory.HardwareWhea or CrashCategory.MemoryExhaustion,
        5 => c.Category is CrashCategory.BrowserGpu or CrashCategory.AppCrash,
        _ => true,
    };

    // ---------- List ----------
    public ObservableCollection<CrashRow> CrashList { get; } = new();
    bool _noCrash;
    public bool NoCrash { get => _noCrash; private set => Set(ref _noCrash, value); }

    void RenderCrashes(bool keepClosed = false)
    {
        var now = DateTime.Now;
        var list = AllCrashes().Where(c => InPeriod(c) && InCategory(c)).ToList();
        HistoryTabLabel = Loc.Plain(Loc.S("crashes.history")) + (list.Count > 0 ? $" ({list.Count})" : "");
        if (!keepClosed && list.Count > 0 && (_selectedKey is null || list.All(c => c.Key != _selectedKey)))
            _selectedKey = list[0].Key;
        CrashList.Clear();
        int i = 0;
        foreach (var c in list.Take(150)) CrashList.Add(ToRow(c, now, i++ == 0));
        NoCrash = list.Count == 0;
        ShowDetail(list.FirstOrDefault(c => c.Key == _selectedKey));
    }

    // ---------- Detail panel (Windows: 400 px drawer) ----------
    bool _detailVisible;
    string _detailTitle = "", _detailMeta = "", _detailCategory = "", _detailTone = "", _detailPlain = "", _detailEvidence = "";
    string _detailExtension = "", _detailExtensionInfo = "", _detailFull = "", _detailFile = "";
    public bool DetailVisible { get => _detailVisible; private set => Set(ref _detailVisible, value); }
    public string DetailTitle { get => _detailTitle; private set => Set(ref _detailTitle, value); }
    public string DetailMeta { get => _detailMeta; private set => Set(ref _detailMeta, value); }
    public string DetailCategory { get => _detailCategory; private set => Set(ref _detailCategory, value); }
    public string DetailTone { get => _detailTone; private set { Set(ref _detailTone, value); Raise(nameof(DetailDanger)); Raise(nameof(DetailWarn)); } }
    public bool DetailDanger => DetailTone == "danger";
    public bool DetailWarn => DetailTone == "warn";
    public string DetailPlain { get => _detailPlain; private set { Set(ref _detailPlain, value); Raise(nameof(HasDetailPlain)); } }
    public bool HasDetailPlain => DetailPlain.Length > 0;
    public ObservableCollection<KeyValue> DetailAnalysis { get; } = new();
    public string DetailEvidence { get => _detailEvidence; private set { Set(ref _detailEvidence, value); Raise(nameof(HasDetailEvidence)); } }
    public bool HasDetailEvidence => DetailEvidence.Length > 0;
    public string DetailExtension { get => _detailExtension; private set { Set(ref _detailExtension, value); Raise(nameof(HasDetailExtension)); } }
    public bool HasDetailExtension => DetailExtension.Length > 0;
    public string DetailExtensionInfo { get => _detailExtensionInfo; private set => Set(ref _detailExtensionInfo, value); }
    public string DetailFull { get => _detailFull; private set { Set(ref _detailFull, value); Raise(nameof(HasDetailFull)); } }
    public bool HasDetailFull => DetailFull.Length > 0;
    public bool HasDetailFile => _detailFile.Length > 0;
    CrashEntry? _detail;

    void ShowDetail(CrashEntry? c)
    {
        _detail = c;
        DetailVisible = c is not null;
        DetailAnalysis.Clear();
        if (c is null) return;
        DetailTitle = c.Title;
        DetailMeta = (c.TechLine.Length > 0 ? c.TechLine + "  ·  " : "") + c.Time.ToString("f", Loc.Culture);
        DetailCategory = CrashTimeline.CategoryName(c.Category);
        DetailTone = c.Category switch
        {
            CrashCategory.BSOD or CrashCategory.UnexpectedReboot or CrashCategory.HardwareWhea => "danger",
            CrashCategory.AppCrash => "accent",
            _ => "warn",
        };
        DetailPlain = CrashDetails.Plain(c, DetectionData.Embedded);
        foreach (var kv in CrashDetails.Analysis(c)) DetailAnalysis.Add(kv);
        DetailEvidence = CrashDetails.Evidence(c);
        DetailExtension = CrashDetails.SuspectExtension(c) ?? "";
        if (DetailExtension.Length > 0)
        {
            var k = _result?.KernelExtensions.FirstOrDefault(x => x.BundleId == DetailExtension);
            DetailExtensionInfo = k is null ? Loc.S("mac.detail.extension_absent")
                : k.Loaded ? Loc.F("mac.detail.extension_loaded", k.Version) : Loc.F("mac.detail.extension_installed", k.Path);
        }
        DetailFull = CrashDetails.FullText(c);
        _detailFile = c.Panic?.File ?? c.Report?.File ?? "";
        Raise(nameof(HasDetailFile));
    }

    async Task CopyForumAsync()
    {
        if (_detail is null) return;
        await _platform.CopyTextAsync(CrashDetails.ForumMarkdown(_detail, _result));
        ShowToast("success", Loc.S("crashes.summary_copied_ready_to_paste_on"));
    }

    async Task OpenReportFileAsync()
    {
        var path = await _platform.PickFileAsync(new[] { "*.panic", "*.ips" });
        if (path is null) return;
        var errors = new List<string>();
        var (p, r) = DiagnosticReports.ParseFile(path, DetectionData.Embedded, errors);
        var fake = new ScanResult();
        if (p is not null) fake.Panics.Add(p);
        if (r is not null) fake.Reports.Add(r);
        var entry = CrashTimeline.Build(fake, DetectionData.Embedded, DateTime.Now).Crashes.FirstOrDefault();
        if (entry is null)
        {
            ShowToast("warn", errors.FirstOrDefault() ?? Loc.S("mac.toast.file_not_report"));
            return;
        }
        _opened.RemoveAll(e => e.Key == entry.Key);
        _opened.Insert(0, entry);
        _selectedKey = entry.Key;
        RenderCrashes();
    }

    // ---------- Common crashes ----------
    public ObservableCollection<KbRow> KbRows { get; } = new();
    bool _kbOnlyMine = true;
    public bool KbOnlyMine { get => _kbOnlyMine; private set => Set(ref _kbOnlyMine, value); }
    string _kbStatus = "", _kbToggleLabel = "";
    public string KbStatus { get => _kbStatus; private set => Set(ref _kbStatus, value); }
    public string KbToggleLabel { get => _kbToggleLabel; private set => Set(ref _kbToggleLabel, value); }

    void RenderKb()
    {
        var all = KnowledgeBase.Entries;
        var shown = KbOnlyMine ? all.Where(k => k.IsApplicableTo(_result)).ToList() : all.ToList();
        KbRows.Clear();
        for (int i = 0; i < shown.Count; i++) KbRows.Add(new KbRow(shown[i], i == 0));
        KbStatus = KbOnlyMine ? Loc.P("mac.kb.status_mine", shown.Count, all.Count - shown.Count) : Loc.P("mac.kb.status_all", shown.Count);
        KbToggleLabel = KbOnlyMine ? Loc.F("mac.kb.show_all", all.Count) : Loc.S("mac.kb.only_mine");
    }

    void RefreshCrashTexts()
    {
        Raise(nameof(PeriodOptions));
        Raise(nameof(CategoryOptions));
        // The ComboBoxes drop their selection when their items are replaced: put it back.
        int p = _periodIndex, c = _categoryIndex;
        _periodIndex = -1; _categoryIndex = -1; Raise(nameof(PeriodIndex)); Raise(nameof(CategoryIndex));
        _periodIndex = p; _categoryIndex = c; Raise(nameof(PeriodIndex)); Raise(nameof(CategoryIndex));
        RenderKb();
        RenderCrashes(keepClosed: !DetailVisible);
    }
}
