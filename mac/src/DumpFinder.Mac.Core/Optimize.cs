using System.Globalization;
using System.Xml.Linq;

namespace DumpFinder.Mac.Core;

/// <summary>An Optimize › Settings card: a user preference documented by Apple (Windows OptTweaks).</summary>
public sealed record Tweak(string Id, string Domain, string Key, string Type, string Value, bool Recommended)
{
    public string Title => Loc.S($"mac.opt.{Id}.title");
    public string Explanation => Loc.S($"mac.opt.{Id}.explain");
}

/// <summary>A cleanup item (Windows OptCleanup): its size and what moving it to the Trash covers.</summary>
public sealed record CleanupItem(string Id, string Folder, long Bytes, int Count)
{
    public string Title => Loc.S($"mac.opt.clean_{Id}.title");
    public string Explanation => Loc.S($"mac.opt.clean_{Id}.explain");
}

/// <summary>A per-user LaunchAgent (Windows OptStartup: Run keys and Startup folders).</summary>
public sealed record StartupAgent(string Label, string Program, string PlistPath, bool Enabled);

/// <summary>Optimize section: settings, startup agents and cleanup. Every change is journaled and can be undone.</summary>
public static class Optimizer
{
    // ---------- Settings ----------
    public static readonly Tweak[] Tweaks =
    {
        new("ds_network", "com.apple.desktopservices", "DSDontWriteNetworkStores", "bool", "true", true),
        new("ds_usb", "com.apple.desktopservices", "DSDontWriteUSBStores", "bool", "true", true),
        new("show_extensions", "NSGlobalDomain", "AppleShowAllExtensions", "bool", "true", false),
        new("personalized_ads", "com.apple.AdLib", "allowApplePersonalizedAdvertising", "bool", "false", false),
    };

    public static bool IsApplied(Tweak t) => Defaults.Read(t.Domain, t.Key) is { Exists: true } v && v.Value == t.Value;

    /// <summary>Applies or restores (the previous value; the key is removed when there was none) and journals it.</summary>
    public static OpResult RunTweak(Tweak t, bool restore)
    {
        var op = "opt_" + t.Id + (restore ? "_restore" : "_apply");
        var rec = Defaults.Capture(t.Domain, t.Key, false);
        var cmd = restore ? $"/usr/bin/defaults delete {Admin.Sh(t.Domain)} {Admin.Sh(t.Key)} 2>/dev/null; true" : Defaults.WriteCommand(t.Domain, t.Key, t.Type, t.Value);
        bool ok = Proc.Run("/bin/sh", new[] { "-c", cmd }, 10000).ExitCode == 0;
        var res = new OpResult(op, ok, ok ? Loc.F(restore ? "mac.opt.restored" : "mac.opt.applied", t.Title) : "defaults: " + t.Key, ok ? new() { rec } : new());
        Record(res, (restore ? Loc.S("main_window.reset_to_default") : "") + t.Title);
        return res;
    }

    // ---------- Startup (per-user LaunchAgents) ----------
    static string AgentsDir => Path.Combine(MacPaths.Home, "Library", "LaunchAgents");
    static string DisabledDir => Path.Combine(AppStore.Root, "disabled-agents");

    public static List<StartupAgent> Agents()
    {
        var list = new List<StartupAgent>();
        foreach (var (dir, enabled) in new[] { (AgentsDir, true), (DisabledDir, false) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var plist in Directory.EnumerateFiles(dir, "*.plist"))
            {
                var label = Path.GetFileNameWithoutExtension(plist);
                if (label == LoginItem.Label) continue;   // Dump Finder's own login item: Settings › Monitoring
                var (l, program) = ReadAgent(plist);
                list.Add(new StartupAgent(l.Length > 0 ? l : label, program, plist, enabled));
            }
        }
        return list.OrderByDescending(a => a.Enabled).ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Label and program of a launchd plist (XML; binary plists are converted with plutil first).</summary>
    public static (string Label, string Program) ReadAgent(string plist)
    {
        try
        {
            var text = File.ReadAllText(plist);
            if (!text.TrimStart().StartsWith('<'))
                text = Proc.Run("/usr/bin/plutil", new[] { "-convert", "xml1", "-o", "-", plist }, 5000).Stdout;
            var dict = XDocument.Parse(text).Root?.Element("dict");
            if (dict is null) return ("", "");
            string label = "", program = "";
            var items = dict.Elements().ToList();
            for (int i = 0; i + 1 < items.Count; i++)
            {
                if (items[i].Name != "key") continue;
                if (items[i].Value == "Label") label = items[i + 1].Value;
                else if (items[i].Value == "Program") program = items[i + 1].Value;
                else if (items[i].Value == "ProgramArguments" && program.Length == 0)
                    program = string.Join(" ", items[i + 1].Elements("string").Select(e => e.Value));
            }
            return (label, program);
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException) { return ("", ""); }
    }

    /// <summary>
    /// Disabling unloads the agent and moves its plist aside (nothing is deleted, like Task Manager's switch on
    /// Windows); re-enabling moves it back and loads it.
    /// </summary>
    public static OpResult SetAgent(StartupAgent a, bool enable)
    {
        var op = "opt_startup_" + (enable ? "enable" : "disable");
        try
        {
            string from = a.PlistPath, to;
            if (enable)
            {
                to = Path.Combine(AgentsDir, Path.GetFileName(from));
                StartupAgents.MoveBack(from, to);
            }
            else
            {
                Directory.CreateDirectory(DisabledDir);
                to = Path.Combine(DisabledDir, Path.GetFileName(from));
                StartupAgents.Disable(from, to);
            }
            var res = new OpResult(op, true, Loc.F(enable ? "mac.opt.agent_enabled" : "mac.opt.agent_disabled", a.Label),
                new() { new ChangeRecord { Kind = "agent", Path = from, Backup = to } });
            Record(res, Loc.S(enable ? "opt_startup.startup_re_enabled" : "opt_startup.startup_disabled") + a.Label);
            return res;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var res = new OpResult(op, false, e.Message, new());
            Record(res, Loc.S(enable ? "opt_startup.startup_re_enabled" : "opt_startup.startup_disabled") + a.Label);
            return res;
        }
    }

    // ---------- Cleanup (moved to the Trash: the journal can put it back) ----------
    static string Trash => Path.Combine(MacPaths.Home, ".Trash");

    /// <summary>Folders whose content macOS and apps rebuild on their own. Crash reports are never touched.</summary>
    static IEnumerable<(string Id, string Folder, Func<FileSystemInfo, bool> Eligible)> CleanupSources()
    {
        var day = DateTime.Now.AddHours(-24);
        var month = DateTime.Now.AddDays(-30);
        yield return ("caches", Path.Combine(MacPaths.Home, "Library", "Caches"),
            f => f.LastWriteTime < day && !f.Name.StartsWith("com.apple.", StringComparison.Ordinal) && !f.Name.Contains("dumpfinder", StringComparison.OrdinalIgnoreCase));
        yield return ("logs", Path.Combine(MacPaths.Home, "Library", "Logs"),
            f => f.LastWriteTime < month && f.Name != "DiagnosticReports" && f.Name != "DumpFinder");
        yield return ("derived_data", Path.Combine(MacPaths.Home, "Library", "Developer", "Xcode", "DerivedData"), f => f.LastWriteTime < day);
    }

    public static List<CleanupItem> MeasureCleanup()
    {
        var list = new List<CleanupItem>();
        foreach (var (id, folder, eligible) in CleanupSources())
        {
            if (!Directory.Exists(folder)) continue;
            long bytes = 0;
            int count = 0;
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().Where(eligible))
            {
                count++;
                bytes += SizeOf(entry);
            }
            list.Add(new CleanupItem(id, folder, bytes, count));
        }
        return list;
    }

    static long SizeOf(FileSystemInfo f)
    {
        try
        {
            if (f is FileInfo fi) return (f.Attributes & FileAttributes.ReparsePoint) != 0 ? 0 : fi.Length;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return ((DirectoryInfo)f).EnumerateFiles("*", opts).Sum(x => x.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Moves the eligible entries of each chosen item to the Trash; one journal entry per item.</summary>
    public static List<OpResult> Clean(IEnumerable<string> ids)
    {
        var results = new List<OpResult>();
        Directory.CreateDirectory(Trash);
        foreach (var (id, folder, eligible) in CleanupSources().Where(s => ids.Contains(s.Id)))
        {
            var recs = new List<ChangeRecord>();
            long bytes = 0;
            int skipped = 0;
            if (Directory.Exists(folder))
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().Where(eligible).ToList())
                {
                    try
                    {
                        long size = SizeOf(entry);
                        var to = UniqueTrashPath(entry.Name);
                        Journal.MoveAny(entry.FullName, to);
                        recs.Add(new ChangeRecord { Kind = "move", Path = entry.FullName, Backup = to });
                        bytes += size;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                }
            var msg = Loc.F("mac.opt.moved_to_trash", bytes / 1073741824.0) + (skipped > 0 ? Loc.F("engine.opt_cleanup.files_in_use_left", skipped) : "") + Loc.S("common.sentence_end");
            var res = new OpResult("opt_clean_" + id + "_apply", true, msg, recs);
            Record(res, Loc.S("opt_cleanup.cleanup") + Loc.S($"mac.opt.clean_{id}.title"));
            results.Add(res);
        }
        return results;
    }

    static string UniqueTrashPath(string name)
    {
        var p = Path.Combine(Trash, name);
        if (!File.Exists(p) && !Directory.Exists(p)) return p;
        return Path.Combine(Trash, $"{name} {DateTime.Now.ToString("HH.mm.ss", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..4]}");
    }

    static void Record(OpResult r, string title) => Journal.Add(new JournalEntry
    {
        Op = r.Op, FixId = r.Op, Title = title, Ok = r.Ok, Message = r.Message, Changes = r.Changes, Reversible = r.Ok && r.Changes.Count > 0,
    });

    // ---------- Myths ----------
    public static readonly string[] Myths = { "purge", "sip", "cleaners", "spotlight", "smc_nvram", "swap", "permissions" };
}

/// <summary>launchctl around the moves of a LaunchAgent plist (per-user domain gui/&lt;uid&gt;).</summary>
public static class StartupAgents
{
    /// <summary>Test hook: false skips launchctl (the self-test moves files in a temporary home).</summary>
    public static bool UseLaunchctl { get; set; } = true;
    static string Domain => "gui/" + Proc.Run("/usr/bin/id", new[] { "-u" }, 3000).Stdout.Trim();

    public static void Disable(string plist, string to)
    {
        if (UseLaunchctl) Proc.Run("/bin/launchctl", new[] { "bootout", Domain, plist }, 10000);   // not loaded: fine
        Journal.MoveAny(plist, to);
    }

    public static void MoveBack(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Journal.MoveAny(from, to);
        if (UseLaunchctl) Proc.Run("/bin/launchctl", new[] { "bootstrap", Domain, to }, 10000);
    }
}
