using System.Globalization;

namespace DumpFinder.Mac.Core;

/// <summary>An installed app (or an app suite folder) with its size and the last time macOS saw it opened.</summary>
public sealed record AppEntry(string Name, string Path, long Bytes, DateTime? LastUsed, bool Protected);

/// <summary>A file or folder of the home folder with its total size.</summary>
public sealed record SpaceEntry(string Name, string Path, long Bytes, bool IsFolder, bool Protected);

/// <summary>
/// What takes space on the Mac (Optimize › Storage): installed apps, the home folder as a size tree to drill into, and
/// the largest files. Read-only; moving to the Trash goes through <see cref="MoveToTrash"/> and the change journal.
/// </summary>
public sealed class DiskSpaceScan
{
    readonly Dictionary<string, long> _dirSizes = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _fileSizes = new(StringComparer.Ordinal);
    public string Root { get; }
    public List<SpaceEntry> LargestFiles { get; } = new();
    public long ScannedBytes { get; private set; }
    public int ScannedFiles { get; private set; }

    DiskSpaceScan(string root) => Root = root;

    /// <summary>
    /// One walk of <paramref name="root"/> (links not followed, unreadable folders skipped) that sums every folder and
    /// keeps the largest files. <paramref name="progress"/> gets the bytes seen so far every 20 000 files.
    /// </summary>
    public static DiskSpaceScan Scan(string root, CancellationToken ct, Action<long, int>? progress = null, long bigFile = 100L << 20)
    {
        var scan = new DiskSpaceScan(root);
        var big = new List<(string Path, long Bytes)>();
        scan.Walk(root, ct, progress, bigFile, big);
        foreach (var (p, b) in big.OrderByDescending(x => x.Bytes).Take(60))
            scan.LargestFiles.Add(new SpaceEntry(System.IO.Path.GetFileName(p), p, b, false, IsProtected(p)));
        return scan;
    }

    long Walk(string dir, CancellationToken ct, Action<long, int>? progress, long bigFile, List<(string, long)> big)
    {
        long total = 0;
        var kids = new List<string>();
        _children[dir] = kids;
        IEnumerable<FileSystemInfo> entries;
        try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false }).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { _dirSizes[dir] = 0; return 0; }
        foreach (var e in entries)
        {
            if (ct.IsCancellationRequested) break;
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // symlinks: counted where they point, not twice
            kids.Add(e.FullName);
            if (e is DirectoryInfo)
            {
                total += Walk(e.FullName, ct, progress, bigFile, big);
                continue;
            }
            long len;
            try { len = ((FileInfo)e).Length; } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { continue; }
            // Large files may be sparse (virtual machine disks): count the blocks really used on the SSD, as the Finder does.
            if (len >= bigFile) len = Math.Min(len, AllocatedBytes(e.FullName) ?? len);
            _fileSizes[e.FullName] = len;
            total += len;
            ScannedBytes += len;
            if (++ScannedFiles % 20000 == 0) progress?.Invoke(ScannedBytes, ScannedFiles);
            if (len >= bigFile) big.Add((e.FullName, len));
        }
        _dirSizes[dir] = total;
        return total;
    }

    /// <summary>Space a file really takes (stat blocks × 512), null when unknown.</summary>
    public static long? AllocatedBytes(string path)
    {
        var r = Proc.Run("/usr/bin/stat", new[] { "-f", "%b", path }, 5000);
        return r.ExitCode == 0 && long.TryParse(r.Stdout.Trim(), out var blocks) ? blocks * 512 : null;
    }

    public long SizeOf(string path) => _dirSizes.TryGetValue(path, out var d) ? d : _fileSizes.GetValueOrDefault(path);

    /// <summary>The children of a scanned folder, largest first (the drill-down list).</summary>
    public List<SpaceEntry> ChildrenOf(string dir) =>
        _children.TryGetValue(dir, out var kids)
            ? kids.Select(k => new SpaceEntry(System.IO.Path.GetFileName(k), k, SizeOf(k), _dirSizes.ContainsKey(k), IsProtected(k)))
                  .OrderByDescending(e => e.Bytes).ToList()
            : new List<SpaceEntry>();

    /// <summary>After a move to the Trash: the item leaves the tree and its size leaves every parent.</summary>
    public void Forget(string path)
    {
        long size = SizeOf(path);
        _dirSizes.Remove(path);
        _fileSizes.Remove(path);
        LargestFiles.RemoveAll(f => f.Path == path || f.Path.StartsWith(path + "/", StringComparison.Ordinal));
        var parent = System.IO.Path.GetDirectoryName(path);
        if (parent is not null && _children.TryGetValue(parent, out var kids)) kids.Remove(path);
        for (var p = parent; p is not null && p.Length >= Root.Length; p = System.IO.Path.GetDirectoryName(p))
            if (_dirSizes.ContainsKey(p)) _dirSizes[p] -= size;
    }

    // ---------- Protected items: only "Show in Finder" ----------
    static readonly string[] LibraryKeep =
    {
        "Keychains", "Preferences", "Application Support", "Containers", "Group Containers", "Mail", "Messages", "Mobile Documents",
        "Accounts", "Calendars", "Safari", "Cookies", "CloudStorage", "Photos", "Daemon Containers", "LaunchAgents", "Biome", "Trial",
    };

    /// <summary>Home items kept out of the Trash: the standard folders, keys and secrets, and shell / tool configuration.</summary>
    static readonly string[] HomeKeep =
    {
        "Desktop", "Documents", "Downloads", "Library", "Movies", "Music", "Pictures", "Public", "Applications", ".Trash",
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".config", ".local", ".docker", ".gitconfig", ".git-credentials", ".netrc",
        ".zshrc", ".zprofile", ".zshenv", ".zsh_history", ".bashrc", ".bash_profile", ".profile", ".CFUserTextEncoding",
    };

    /// <summary>
    /// The home folder itself, its standard folders and secrets (<see cref="HomeKeep"/>), the parts of the Library that
    /// hold accounts, keychains, mail and app data, iCloud Drive, and Dump Finder's own data are never offered for the Trash.
    /// </summary>
    public static bool IsProtected(string path)
    {
        var home = MacPaths.Home.TrimEnd('/');
        if (!path.StartsWith(home + "/", StringComparison.Ordinal)) return !path.StartsWith("/Applications/", StringComparison.Ordinal);
        var rel = path[(home.Length + 1)..].Split('/');
        if (rel.Length == 1) return HomeKeep.Contains(rel[0]);                     // ~/Documents, ~/Library, ~/.ssh…
        if (rel[0] == "Library" && rel.Length == 2) return LibraryKeep.Contains(rel[1]) || rel[1] == "DumpFinder";
        if (rel[0] == "Library" && rel[1] == "Application Support" && rel.Length >= 3 && rel[2] == "DumpFinder") return true;
        if (rel[0] == ".Trash") return true;
        return false;
    }

    // ---------- Applications ----------
    /// <summary>
    /// /Applications and ~/Applications: every .app, and app suite folders (a folder holding apps, e.g. Adobe) as one
    /// entry. Safari and anything that lives on the system volume are protected.
    /// </summary>
    public static List<AppEntry> Apps(CancellationToken ct)
    {
        var list = new List<AppEntry>();
        foreach (var dir in new[] { "/Applications", System.IO.Path.Combine(MacPaths.Home, "Applications") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var entry in new DirectoryInfo(dir).EnumerateDirectories())
            {
                if (ct.IsCancellationRequested) break;
                if (entry.Name == "Utilities" && dir == "/Applications") continue;   // Apple's own tools
                bool isApp = entry.Name.EndsWith(".app", StringComparison.Ordinal);
                bool suite = !isApp && entry.EnumerateDirectories("*.app", SearchOption.TopDirectoryOnly).Any();
                if (!isApp && !suite) continue;
                bool system = entry.Name == "Safari.app" || (entry.LinkTarget?.StartsWith("/System", StringComparison.Ordinal) ?? false);
                list.Add(new AppEntry(isApp ? entry.Name[..^4] : entry.Name, entry.FullName, FolderSize(entry.FullName, ct), LastUsed(entry.FullName), system));
            }
        }
        return list.OrderByDescending(a => a.Bytes).ToList();
    }

    public static long FolderSize(string path, CancellationToken ct = default)
    {
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            long total = 0;
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opts))
            {
                if (ct.IsCancellationRequested) break;
                total += f.Length;
            }
            return total;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Spotlight's "last opened" date (kMDItemLastUsedDate), null when macOS never recorded one.</summary>
    static DateTime? LastUsed(string path)
    {
        var r = Proc.Run("/usr/bin/mdls", new[] { "-name", "kMDItemLastUsedDate", "-raw", path }, 5000);
        return DateTime.TryParse(r.Stdout.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : null;
    }

    // ---------- Actions ----------
    /// <summary>
    /// Moves a file, folder or app to the Trash and journals it (Undo in Tools › History puts it back). Items the
    /// account cannot move itself (apps installed by an administrator) go through the Finder, which asks for the
    /// password and supports Put Back in the Trash; those are not journaled. Returns (ok, message, by Finder).
    /// </summary>
    public static (bool Ok, string Message, bool ByFinder) MoveToTrash(string path, string title)
    {
        if (IsProtected(path)) return (false, Loc.S("mac.space.protected"), false);
        if (!File.Exists(path) && !Directory.Exists(path)) return (false, Loc.S("mac.space.gone"), false);
        long size = Directory.Exists(path) ? FolderSize(path) : new FileInfo(path).Length;
        var trash = System.IO.Path.Combine(MacPaths.Home, ".Trash");
        try
        {
            Directory.CreateDirectory(trash);
            var name = System.IO.Path.GetFileName(path);
            var to = System.IO.Path.Combine(trash, name);
            if (File.Exists(to) || Directory.Exists(to))
                to = System.IO.Path.Combine(trash, $"{System.IO.Path.GetFileNameWithoutExtension(name)} {DateTime.Now:HH.mm.ss}{System.IO.Path.GetExtension(name)}");
            Journal.MoveAny(path, to);
            Journal.Add(new JournalEntry
            {
                Op = "space_trash", FixId = "space_trash", Title = Loc.S("mac.space.trashed_title") + title, Ok = true, Reversible = true,
                Message = Loc.F("mac.space.trashed", title, Human(size)),
                Changes = new() { new ChangeRecord { Kind = "move", Path = path, Backup = to } },
            });
            return (true, Loc.F("mac.space.trashed", title, Human(size)), false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // Root-owned (App Store or admin-installed): the Finder asks for the password itself.
            var script = $"tell application \"Finder\" to delete (POSIX file \"{Admin.AppleScriptEscape(path)}\")";
            var r = Proc.Run("/usr/bin/osascript", new[] { "-e", script }, 120000);
            bool gone = !File.Exists(path) && !Directory.Exists(path);
            return gone ? (true, Loc.F("mac.space.trashed_by_finder", title, Human(size)), true)
                        : (false, Loc.S("mac.space.not_moved") + (r.ExitCode != 0 ? "" : e.Message), false);
        }
    }

    public static void RevealInFinder(string path) => Proc.Run("/usr/bin/open", new[] { "-R", path }, 5000);

    public static string Human(long bytes) => bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.0} {Loc.S("stress_text.gb")}"
        : bytes >= 1L << 20 ? $"{bytes / 1048576.0:0} {Loc.S("stress_text.mb")}" : $"{Math.Max(1, bytes / 1024)} KB";
}
