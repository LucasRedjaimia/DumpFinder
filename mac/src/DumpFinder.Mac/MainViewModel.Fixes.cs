using System.Collections.ObjectModel;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>A fix card (Windows BuildFixCard): title, status pill, tag, explanation, Apply, details drawer with Restore.</summary>
public sealed class FixCardRow(FixItem fix) : Observable
{
    public FixItem Fix => fix;
    public string Id => fix.Id;
    public string Title => fix.Title;
    public string Explanation => fix.Explanation;
    public string Tag => fix.Tag;
    public bool HasTag => fix.Tag.Length > 0;
    public string Status => Loc.S(fix.Status switch
    {
        FixStatus.Applied => "system_health_scanner.active", FixStatus.Recommended => "localizer.recommended",
        FixStatus.Optional => "localizer.optional", _ => "localizer.tool",
    });
    public bool IsApplied => fix.Status == FixStatus.Applied;
    public bool IsRecommended => fix.Status == FixStatus.Recommended;
    public bool IsNotInstalled => fix.Status == FixStatus.Info && !fix.IsTool;
    /// <summary>Windows: an applied fix shows "✓ Active" (still clickable); a tool keeps its own label.</summary>
    public string ApplyText => IsApplied && !fix.IsTool ? "✓ " + Status : fix.ApplyLabel;
    public bool CanApply => !IsNotInstalled;
    public string Current => Loc.S("main_window.current_state") + fix.CurrentValue;
    public string Target => fix.Target;
    public bool HasRestore => fix.CanRestore && fix.RestoreLabel.Length > 0;
    public string RestoreText => fix.RestoreLabel;
    public string AdminText => fix.Admin ? Loc.S("opt_tweaks.admin") : "";
    public bool IsAdmin => fix.Admin;
    bool _expanded;
    public bool Expanded { get => _expanded; set { Set(ref _expanded, value); Raise(nameof(Chevron)); } }
    public string Chevron => Expanded ? "▴" : "▾";
    public RelayCommand ToggleCommand => new(() => { Expanded = !Expanded; return Task.CompletedTask; });
}

/// <summary>A row of the change journal (Windows HistoryPage RenderJournal).</summary>
public sealed record JournalRow(string Id, string Icon, string Tone, string Title, string Sub, string Tip, bool CanUndo, bool NotUndoable, bool First) : Toned(Tone);

public sealed class TweakRow(Tweak t, bool applied) : Observable
{
    public Tweak Tweak => t;
    public string Title => t.Title;
    public string Explanation => t.Explanation;
    public bool Applied => applied;
    public bool Recommended => !applied && t.Recommended;
    public string Status => Loc.S(applied ? "system_health_scanner.active" : t.Recommended ? "localizer.recommended" : "localizer.optional");
    public string ButtonText => applied ? Loc.S("opt_tweaks.restore") : Loc.S("opt_tweaks.apply");
    public string Detail => $"{t.Domain}  •  {t.Key} = {t.Value}";
}

public sealed record AgentRow(StartupAgent Agent, string Name, string Program, bool Enabled, string Pill, string ButtonText);

public sealed class CleanupRow(CleanupItem item) : Observable
{
    public CleanupItem Item => item;
    public string Title => item.Title;
    public string Explanation => item.Explanation;
    public string Size => item.Bytes >= 1L << 30 ? $"{item.Bytes / 1073741824.0:0.00} {Loc.S("stress_text.gb")}" : $"{item.Bytes / 1048576.0:0} {Loc.S("stress_text.mb")}";
    public bool HasSize => item.Bytes > 0;
    bool _checked = item.Bytes > 0;
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>
/// Fixes, Optimize and the change journal, same flows as Windows: one change at a time, "Applying…" in the
/// status line, a toast with the result, the journal written for every change and Undo from Tools › History.
/// </summary>
public sealed partial class MainViewModel
{
    List<FixItem> _fixes = new();
    bool _changing;
    public ObservableCollection<FixCardRow> FixCards { get; } = new();
    public ObservableCollection<JournalRow> JournalRows { get; } = new();
    public ObservableCollection<TweakRow> TweakRows { get; } = new();
    public ObservableCollection<AgentRow> AgentRows { get; } = new();
    public ObservableCollection<CleanupRow> CleanupRows { get; } = new();

    public RelayCommand ApplyFixCommand => _applyFix ??= new(p => p is string id ? ApplyFixAsync(id, restore: false) : Task.CompletedTask);
    public RelayCommand RestoreFixCommand => _restoreFix ??= new(p => p is string id ? ApplyFixAsync(id, restore: true) : Task.CompletedTask);
    public RelayCommand ApplyAllCommand => _applyAll ??= new(ApplyAllAsync);
    public RelayCommand FixGroupCommand => _fixGroup ??= new(p => { FixGroupIndex = Convert.ToInt32(p); return Task.CompletedTask; });
    public RelayCommand UndoCommand => _undo ??= new(p => p is string id ? UndoAsync(id) : Task.CompletedTask);
    public RelayCommand JournalCommand => _journal ??= new(() => { RunAction("nav:history"); return Task.CompletedTask; });
    public RelayCommand OptimizeTabCommand => _optTab ??= new(p => { OptimizeTab = Convert.ToInt32(p); return Task.CompletedTask; });
    public RelayCommand TweakCommand => _tweak ??= new(p => p is TweakRow t ? RunChangeAsync(() => Optimizer.RunTweak(t.Tweak, t.Applied), RenderTweaks) : Task.CompletedTask);
    public RelayCommand ApplyTweaksCommand => _tweaksAll ??= new(ApplyRecommendedTweaksAsync);
    public RelayCommand AgentCommand => _agent ??= new(p => p is AgentRow a ? RunChangeAsync(() => Optimizer.SetAgent(a.Agent, !a.Enabled), RenderAgents) : Task.CompletedTask);
    public RelayCommand CleanCommand => _clean ??= new(CleanAsync);
    RelayCommand? _applyFix, _restoreFix, _applyAll, _fixGroup, _undo, _journal, _optTab, _tweak, _tweaksAll, _agent, _clean;

    // ---------- Fixes ----------
    int _fixGroupIndex;
    public int FixGroupIndex
    {
        get => _fixGroupIndex;
        set { Set(ref _fixGroupIndex, value); foreach (var n in new[] { nameof(IsGroupAll), nameof(IsGroupApps), nameof(IsGroupSystem) }) Raise(n); RenderFixCards(); }
    }
    public bool IsGroupAll => FixGroupIndex == 0;
    public bool IsGroupApps => FixGroupIndex == 1;
    public bool IsGroupSystem => FixGroupIndex == 2;

    string _applyAllText = "", _fixesBadge = "";
    public string ApplyAllText { get => _applyAllText; private set => Set(ref _applyAllText, value); }
    bool _hasRecommended;
    public bool HasRecommended { get => _hasRecommended; private set => Set(ref _hasRecommended, value); }
    /// <summary>Sidebar badge: every Recommended fix (Windows: same, whatever the filter).</summary>
    public string FixesBadge { get => _fixesBadge; private set { Set(ref _fixesBadge, value); Raise(nameof(ShowFixesBadge)); } }
    public bool ShowFixesBadge => FixesBadge.Length > 0 && SidebarExpanded;

    List<FixItem> Recommended => _fixes.Where(f => f.Status == FixStatus.Recommended).ToList();

    async Task RefreshFixesAsync()
    {
        var r = _result;
        var s = _summary;
        _fixes = await Task.Run(() => FixCatalog.Inspect(r, s));
        RenderFixCards();
        RenderHomeActions();
    }

    void RenderFixCards()
    {
        FixCards.Clear();
        var group = FixGroupIndex switch { 1 => FixGroup.BrowserApps, 2 => FixGroup.SystemMaint, _ => FixGroup.All };
        foreach (var f in _fixes.Where(f => group == FixGroup.All || f.Group == group)) FixCards.Add(new FixCardRow(f));
        int shown = FixCards.Count(c => c.IsRecommended);
        HasRecommended = shown > 0;
        ApplyAllText = Loc.Plain(shown > 0 ? Loc.F("main_window.apply_recommended_2", shown) : Loc.S("main_window.no_fix_required"));
        int all = Recommended.Count;
        FixesBadge = all > 0 ? all.ToString(Loc.Culture) : "";
    }

    /// <summary>Home: the first 3 recommended fixes with an Apply button; without any, the checks to look at.</summary>
    void RenderHomeActions()
    {
        var rec = Recommended;
        if (rec.Count == 0 || _result is null) return;
        Actions.Clear();
        int i = 0;
        foreach (var f in rec.Take(3))
            Actions.Add(new ActionRow(f.Group == FixGroup.BrowserApps ? "Globe" : "Repair", "accent", f.Title,
                f.Tag + (f.Admin ? "" : Loc.S("home.reversible")), i++ == 0, f.Id));
        NoActions = false;
        HeroPrimary = Loc.Plain(Loc.P("home.apply_fixes_button", rec.Count));
    }

    /// <summary>Hero primary: apply the recommended fixes when there are some (Windows), else see the checks.</summary>
    bool HeroAppliesFixes => Recommended.Count > 0;

    /// <summary>Windows ExecuteSingleFix: one change at a time; a running browser or Discord is closed first, on approval.</summary>
    async Task ApplyFixAsync(string id, bool restore)
    {
        var fix = _fixes.FirstOrDefault(f => f.Id == id);
        if (fix is null) return;
        if (fix.IsTool) { _platform.Open(FixCatalog.DiskUtility); return; }
        if (!await CloseAppIfRunningAsync(fix)) return;
        await RunFixOpsAsync(new[] { id + (restore ? "_restore" : "_apply") }, single: true);
    }

    async Task ApplyAllAsync()
    {
        var pending = Recommended;
        if (pending.Count == 0) { ShowToast("success", Loc.S("main_window.all_recommended_fixes_are_already_active")); return; }
        var ops = new List<string>();
        foreach (var f in pending)
            if (await CloseAppIfRunningAsync(f)) ops.Add(f.Id + "_apply");   // a declined one is skipped, the others go on
        if (ops.Count > 0) await RunFixOpsAsync(ops, single: false);
    }

    async Task RunFixOpsAsync(IReadOnlyList<string> ops, bool single)
    {
        if (_changing) { ShowToast("warn", Loc.S("main_window.another_operation_is_already_running_wai")); return; }
        _changing = true;
        var before = Status;
        Status = Loc.S("main_window.applying");
        try
        {
            var titles = _fixes.ToDictionary(f => f.Id, f => f.Title);
            string TitleOf(string op) => (op.EndsWith("_restore", StringComparison.Ordinal) ? Loc.S("main_window.reset_to_default") : "")
                                         + titles.GetValueOrDefault(op[..op.LastIndexOf('_')], op);
            var results = await Task.Run(() => FixCatalog.RunAndRecord(ops, TitleOf));
            if (single) ShowToast(results[0].Ok ? "success" : "danger", results[0].Message);
            else
            {
                int ok = results.Count(r => r.Ok), failed = results.Count - ok;
                ShowToast(failed == 0 ? "success" : "warn", failed == 0 ? Loc.F("main_window.fix_es_applied_each_one_can", ok) : Loc.F("main_window.fix_es_applied_failed", ok, failed));
            }
        }
        finally
        {
            Status = before;
            _changing = false;
            await RefreshFixesAsync();
            RenderJournal();
        }
    }

    /// <summary>Windows: "Close Google Chrome gracefully to apply this setting?" The app rewrites its settings when it quits.</summary>
    async Task<bool> CloseAppIfRunningAsync(FixItem f)
    {
        if (f.Process.Length == 0 || !IsRunning(f.Process)) return true;
        if (!await AskAsync(Loc.F("main_window.close_gracefully_to_apply_this_setting", f.Process))) return false;
        await Task.Run(() =>
        {
            Proc.Run("/usr/bin/osascript", new[] { "-e", $"tell application \"{Admin.AppleScriptEscape(f.Process)}\" to quit" }, 15000);
            for (int i = 0; i < 30 && IsRunning(f.Process); i++) Thread.Sleep(500);
        });
        return !IsRunning(f.Process);
    }

    static bool IsRunning(string app) => Proc.Run("/usr/bin/pgrep", new[] { "-x", app }, 3000).ExitCode == 0;

    // ---------- Journal (Tools › History) ----------
    bool _noJournal = true;
    public bool NoJournal { get => _noJournal; private set => Set(ref _noJournal, value); }

    void RenderJournal()
    {
        JournalRows.Clear();
        int i = 0;
        foreach (var e in Journal.Entries().Take(60))
        {
            var (icon, tone) = !e.Ok ? ("ErrorBadge", "danger") : e.RevertedAt is not null ? ("Refresh", "") : ("Completed", "success");
            var sub = e.Time.ToString("g", Loc.Culture) + "  ·  " + (e.Ok ? Loc.F("history.value_s_changed", e.Changes.Count) : e.Message)
                      + (e.RevertedAt is { } at ? "  ·  " + Loc.S("history.undone_on") + at.ToString("g", Loc.Culture) : "");
            JournalRows.Add(new JournalRow(e.Id, icon, tone, e.Title, sub, string.Join("\n", e.Changes.Select(c => "• " + c.Describe())),
                e.Ok && e.Reversible && e.RevertedAt is null, e.Ok && !e.Reversible, i++ == 0));
        }
        NoJournal = JournalRows.Count == 0;
    }

    async Task UndoAsync(string id)
    {
        if (_changing) { ShowToast("warn", Loc.S("main_window.another_operation_is_already_running_wai")); return; }
        var entry = Journal.Entries().FirstOrDefault(e => e.Id == id);
        if (entry is null) return;
        _changing = true;
        var before = Status;
        Status = Loc.S("history.undoing");
        try
        {
            var error = await Task.Run(() => Journal.Revert(entry));
            if (error.Length == 0)
            {
                Journal.MarkReverted(id);
                ShowToast("success", Loc.S("history.change_undone") + entry.Title);
            }
            else ShowToast("warn", Loc.S("history.could_not_undo") + error);
        }
        finally
        {
            Status = before;
            _changing = false;
            RenderJournal();
            await RefreshFixesAsync();
            RenderOptimize();
        }
    }

    // ---------- Optimize ----------
    int _optimizeTab;
    public int OptimizeTab
    {
        get => _optimizeTab;
        set
        {
            Set(ref _optimizeTab, Math.Clamp(value, 0, 3));
            foreach (var n in new[] { nameof(IsOptSettings), nameof(IsOptStartup), nameof(IsOptCleanup), nameof(IsOptMyths) }) Raise(n);
            RenderOptimize();
        }
    }
    public bool IsOptSettings => OptimizeTab == 0;
    public bool IsOptStartup => OptimizeTab == 1;
    public bool IsOptCleanup => OptimizeTab == 2;
    public bool IsOptMyths => OptimizeTab == 3;

    string _tweaksButton = "", _agentsNote = "", _cleanButton = "";
    bool _hasTweaksToApply, _noAgents, _canClean;
    public string TweaksButton { get => _tweaksButton; private set => Set(ref _tweaksButton, value); }
    public bool HasTweaksToApply { get => _hasTweaksToApply; private set => Set(ref _hasTweaksToApply, value); }
    public string AgentsNote { get => _agentsNote; private set => Set(ref _agentsNote, value); }
    public bool NoAgents { get => _noAgents; private set => Set(ref _noAgents, value); }
    public string CleanButton { get => _cleanButton; private set => Set(ref _cleanButton, value); }
    public bool CanClean { get => _canClean; private set => Set(ref _canClean, value); }
    public IEnumerable<KeyValue> Myths => Optimizer.Myths.Select(m => new KeyValue(Loc.S($"mac.myth.{m}.title"), Loc.S($"mac.myth.{m}.body"), false));

    void RenderOptimize()
    {
        if (!IsOptimize && !Offline) return;
        if (IsOptSettings) RenderTweaks();
        else if (IsOptStartup) RenderAgents();
        else if (IsOptCleanup) _ = RenderCleanupAsync();
        Raise(nameof(Myths));
    }

    void RenderTweaks()
    {
        TweakRows.Clear();
        foreach (var t in Optimizer.Tweaks) TweakRows.Add(new TweakRow(t, !Offline && Optimizer.IsApplied(t)));
        int pending = TweakRows.Count(t => t.Recommended);
        HasTweaksToApply = pending > 0;
        TweaksButton = Loc.Plain(pending > 0 ? Loc.F("optimize.apply_recommended", pending) : Loc.S("optimize.recommended_already_applied"));
    }

    void RenderAgents()
    {
        AgentRows.Clear();
        var agents = Optimizer.Agents();
        foreach (var a in agents)
            AgentRows.Add(new AgentRow(a, a.Label, a.Program, a.Enabled, Loc.S(a.Enabled ? "optimize.at_startup" : "risky_drivers.disabled"),
                Loc.S(a.Enabled ? "risky_drivers.disable" : "risky_drivers.re_enable")));
        NoAgents = AgentRows.Count == 0;
        AgentsNote = Loc.F("mac.opt.agents_note", agents.Count(a => a.Enabled));
    }

    async Task RenderCleanupAsync()
    {
        CleanButton = Loc.S("optimize.measuring_reclaimable_space");
        CanClean = false;
        var items = await Task.Run(Optimizer.MeasureCleanup);
        CleanupRows.Clear();
        foreach (var i in items)
        {
            var row = new CleanupRow(i);
            row.PropertyChanged += (_, _) => UpdateCleanButton();
            CleanupRows.Add(row);
        }
        UpdateCleanButton();
    }

    void UpdateCleanButton()
    {
        long bytes = CleanupRows.Where(r => r.Checked).Sum(r => r.Item.Bytes);
        CanClean = CleanupRows.Any(r => r.Checked && r.HasSize);
        CleanButton = Loc.F("optimize.clean_selected", bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.00} {Loc.S("stress_text.gb")}" : $"{bytes / 1048576.0:0} {Loc.S("stress_text.mb")}");
    }

    async Task ApplyRecommendedTweaksAsync()
    {
        var pending = TweakRows.Where(t => t.Recommended).Select(t => t.Tweak).ToList();
        if (pending.Count == 0) return;
        await RunBatchAsync(() => pending.Select(t => Optimizer.RunTweak(t, false)).ToList(), RenderTweaks,
            results => Loc.F("mac.opt.settings_applied", results.Count(r => r.Ok)));
    }

    async Task CleanAsync()
    {
        var ids = CleanupRows.Where(r => r.Checked && r.HasSize).Select(r => r.Item.Id).ToList();
        if (ids.Count == 0) return;
        await RunBatchAsync(() => Optimizer.Clean(ids), () => _ = RenderCleanupAsync(), results => string.Join("  ", results.Select(r => r.Message)), Loc.S("optimize.cleaning"));
    }

    /// <summary>Windows ExecuteOptimizeOp: one change at a time, "Applying…", the result as a toast, then the tab again.</summary>
    Task RunChangeAsync(Func<OpResult> run, Action render) =>
        RunBatchAsync(() => new List<OpResult> { run() }, render, results => results[0].Message);

    async Task RunBatchAsync(Func<List<OpResult>> run, Action render, Func<List<OpResult>, string> message, string? status = null)
    {
        if (_changing) { ShowToast("warn", Loc.S("main_window.another_operation_is_already_running_wai")); return; }
        _changing = true;
        var before = Status;
        Status = status ?? Loc.S("main_window.applying");
        try
        {
            var results = await Task.Run(run);
            ShowToast(results.All(r => r.Ok) ? "success" : "warn", message(results));
        }
        finally
        {
            Status = before;
            _changing = false;
            render();
            RenderJournal();
        }
    }

    // ---------- Confirmation (Windows Yes / No MessageBox) ----------
    TaskCompletionSource<bool>? _confirm;
    string _confirmText = "";
    public string ConfirmText { get => _confirmText; private set => Set(ref _confirmText, value); }
    public bool ConfirmVisible => _confirm is not null;
    public RelayCommand ConfirmYesCommand => _yes ??= new(() => { CloseConfirm(true); return Task.CompletedTask; });
    public RelayCommand ConfirmNoCommand => _no ??= new(() => { CloseConfirm(false); return Task.CompletedTask; });
    RelayCommand? _yes, _no;

    Task<bool> AskAsync(string text)
    {
        CloseConfirm(false);
        _confirm = new TaskCompletionSource<bool>();
        ConfirmText = text;
        Raise(nameof(ConfirmVisible));
        return _confirm.Task;
    }

    void CloseConfirm(bool answer)
    {
        var c = _confirm;
        _confirm = null;
        Raise(nameof(ConfirmVisible));
        c?.TrySetResult(answer);
    }
}
