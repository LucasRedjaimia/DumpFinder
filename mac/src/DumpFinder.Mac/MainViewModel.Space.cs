using System.Collections.ObjectModel;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>A row of Optimize › Storage: an app, a folder or a file, its size against the largest one, and its actions.</summary>
public sealed record SpaceRow(string Name, string Path, string Size, double Share, string Sub, bool IsFolder, bool CanTrash, bool First)
{
    public string Icon => IsFolder ? "Document" : "Save";
}

/// <summary>
/// Optimize › Storage (Mac addition): what takes space, as apps, a drill-down of the home folder and the largest files,
/// with Show in Finder and Move to Trash (journaled, so Undo puts it back).
/// </summary>
public sealed partial class MainViewModel
{
    DiskSpaceScan? _space;
    List<AppEntry> _apps = new();
    CancellationTokenSource? _spaceCts;
    string _spaceFolder = "";

    public ObservableCollection<SpaceRow> SpaceRows { get; } = new();
    public RelayCommand SpaceScanCommand => _spaceScan ??= new(ScanSpaceAsync, () => !SpaceScanning);
    public RelayCommand SpaceModeCommand => _spaceModeCmd ??= new(p => { SpaceMode = Convert.ToInt32(p); return Task.CompletedTask; });
    public RelayCommand SpaceOpenCommand => _spaceOpen ??= new(p => { if (p is SpaceRow { IsFolder: true } r && SpaceMode == 1) { _spaceFolder = r.Path; RenderSpace(); } return Task.CompletedTask; });
    public RelayCommand SpaceUpCommand => _spaceUp ??= new(() =>
    {
        if (_space is not null && _spaceFolder != _space.Root) { _spaceFolder = Path.GetDirectoryName(_spaceFolder) ?? _space.Root; RenderSpace(); }
        return Task.CompletedTask;
    });
    public RelayCommand SpaceRevealCommand => _spaceReveal ??= new(p => { if (p is SpaceRow r) DiskSpaceScan.RevealInFinder(r.Path); return Task.CompletedTask; });
    public RelayCommand SpaceTrashCommand => _spaceTrash ??= new(p => p is SpaceRow r ? TrashAsync(r) : Task.CompletedTask);
    RelayCommand? _spaceScan, _spaceModeCmd, _spaceOpen, _spaceUp, _spaceReveal, _spaceTrash;

    /// <summary>0 Apps · 1 Folders · 2 Largest files.</summary>
    int _spaceMode;
    public int SpaceMode
    {
        get => _spaceMode;
        set { Set(ref _spaceMode, Math.Clamp(value, 0, 2)); foreach (var n in new[] { nameof(IsSpaceApps), nameof(IsSpaceFolders), nameof(IsSpaceFiles) }) Raise(n); RenderSpace(); }
    }
    public bool IsSpaceApps => SpaceMode == 0;
    public bool IsSpaceFolders => SpaceMode == 1;
    public bool IsSpaceFiles => SpaceMode == 2;

    bool _spaceScanning, _spaceReady;
    string _spaceStatus = "", _spaceDisk = "", _spaceCrumb = "";
    double _spaceUsedShare;
    public bool SpaceScanning { get => _spaceScanning; private set { Set(ref _spaceScanning, value); SpaceScanCommand.Refresh(); } }
    public bool SpaceReady { get => _spaceReady; private set { Set(ref _spaceReady, value); Raise(nameof(SpaceNotReady)); } }
    public bool SpaceNotReady => !SpaceReady;
    public string SpaceStatus { get => _spaceStatus; private set => Set(ref _spaceStatus, value); }
    public string SpaceDisk { get => _spaceDisk; private set => Set(ref _spaceDisk, value); }
    public double SpaceUsedShare { get => _spaceUsedShare; private set => Set(ref _spaceUsedShare, value); }
    public string SpaceCrumb { get => _spaceCrumb; private set { Set(ref _spaceCrumb, value); Raise(nameof(SpaceCanGoUp)); } }
    public bool SpaceCanGoUp => _space is not null && _spaceFolder != _space.Root && IsSpaceFolders;
    public string SpaceScanText => Loc.S(SpaceReady ? "mac.space.rescan" : "mac.space.analyze");

    void RenderSpaceDisk()
    {
        try
        {
            var d = new System.IO.DriveInfo("/");
            long used = d.TotalSize - d.AvailableFreeSpace;
            SpaceUsedShare = 100.0 * used / d.TotalSize;
            SpaceDisk = Loc.F("mac.space.disk_usage", DiskSpaceScan.Human(used), DiskSpaceScan.Human(d.TotalSize), DiskSpaceScan.Human(d.AvailableFreeSpace));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SpaceDisk = ""; }
    }

    /// <summary>Measures the apps and the whole home folder (about a minute for a few hundred GB), then shows the current mode.</summary>
    async Task ScanSpaceAsync()
    {
        if (SpaceScanning) return;
        SpaceScanning = true;
        var cts = _spaceCts = new CancellationTokenSource();
        try
        {
            SpaceStatus = Loc.S("mac.space.measuring_apps");
            _apps = await Task.Run(() => DiskSpaceScan.Apps(cts.Token));
            SpaceStatus = Loc.F("mac.space.measuring", "0 KB", 0);
            var scan = await Task.Run(() => DiskSpaceScan.Scan(MacPaths.Home, cts.Token,
                (bytes, files) => Avalonia.Threading.Dispatcher.UIThread.Post(() => SpaceStatus = Loc.F("mac.space.measuring", DiskSpaceScan.Human(bytes), files))));
            _space = scan;
            _spaceFolder = scan.Root;
            SpaceReady = true;
            SpaceStatus = Loc.F("mac.space.scanned", DiskSpaceScan.Human(scan.ScannedBytes), scan.ScannedFiles);
            RenderSpace();
        }
        finally
        {
            SpaceScanning = false;
            Raise(nameof(SpaceScanText));
        }
    }

    void RenderSpace()
    {
        RenderSpaceDisk();
        SpaceRows.Clear();
        Raise(nameof(SpaceCanGoUp));
        if (!SpaceReady || _space is null) return;
        string home = MacPaths.Home;
        string Short(string p) => p.StartsWith(home, StringComparison.Ordinal) ? "~" + p[home.Length..] : p;
        IEnumerable<SpaceRow> rows;
        if (IsSpaceApps)
        {
            long max = Math.Max(1, _apps.FirstOrDefault()?.Bytes ?? 1);
            rows = _apps.Select((a, i) => new SpaceRow(a.Name, a.Path, DiskSpaceScan.Human(a.Bytes), 100.0 * a.Bytes / max,
                a.LastUsed is { } d ? Loc.F("mac.space.last_used", d) : Loc.S("mac.space.never_used"), false, !a.Protected, i == 0));
            SpaceCrumb = "/Applications";
        }
        else if (IsSpaceFolders)
        {
            var kids = _space.ChildrenOf(_spaceFolder).Where(k => k.Bytes > 0).Take(200).ToList();
            long max = Math.Max(1, kids.FirstOrDefault()?.Bytes ?? 1);
            rows = kids.Select((k, i) => new SpaceRow(k.Name, k.Path, DiskSpaceScan.Human(k.Bytes), 100.0 * k.Bytes / max,
                k.Protected ? Loc.S("mac.space.protected_short") : "", k.IsFolder, !k.Protected, i == 0));
            SpaceCrumb = Short(_spaceFolder) + "  ·  " + DiskSpaceScan.Human(_space.SizeOf(_spaceFolder));
        }
        else
        {
            long max = Math.Max(1, _space.LargestFiles.FirstOrDefault()?.Bytes ?? 1);
            rows = _space.LargestFiles.Select((f, i) => new SpaceRow(f.Name, f.Path, DiskSpaceScan.Human(f.Bytes), 100.0 * f.Bytes / max,
                Short(Path.GetDirectoryName(f.Path) ?? ""), false, !f.Protected, i == 0));
            SpaceCrumb = Loc.S("mac.space.largest_files_note");
        }
        foreach (var r in rows) SpaceRows.Add(r);
    }

    async Task TrashAsync(SpaceRow r)
    {
        if (_changing) { ShowToast("warn", Loc.S("main_window.another_operation_is_already_running_wai")); return; }
        if (r.Path.EndsWith(".app", StringComparison.Ordinal) && IsRunning(Path.GetFileNameWithoutExtension(r.Path)))
        {
            ShowToast("warn", Loc.F("mac.space.quit_first", r.Name));
            return;
        }
        if (!await AskAsync(Loc.F("mac.space.confirm_trash", r.Name, r.Size))) return;
        _changing = true;
        try
        {
            var (ok, message, _) = await Task.Run(() => DiskSpaceScan.MoveToTrash(r.Path, r.Name));
            ShowToast(ok ? "success" : "warn", message);
            if (ok)
            {
                _space?.Forget(r.Path);
                _apps.RemoveAll(a => a.Path == r.Path);
            }
        }
        finally
        {
            _changing = false;
            RenderSpace();
            RenderJournal();
        }
    }
}
