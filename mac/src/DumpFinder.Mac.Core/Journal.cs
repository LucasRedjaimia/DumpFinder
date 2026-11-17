using System.Text.Json.Nodes;

namespace DumpFinder.Mac.Core;

/// <summary>
/// One value Dump Finder replaced, captured just before the write (Windows ChangeRecord):
/// file (a JSON settings file, with a backup copy), defaults (a preference key), pmset (a power setting),
/// move (an item moved to the Trash) or agent (a LaunchAgent moved aside).
/// </summary>
public sealed class ChangeRecord
{
    public string Kind { get; set; } = "";
    /// <summary>file / move / agent: the original path. defaults: the domain (or a plist path).</summary>
    public string Path { get; set; } = "";
    /// <summary>file: the backup copy. move / agent: where the item went.</summary>
    public string Backup { get; set; } = "";
    /// <summary>defaults key or pmset setting.</summary>
    public string Key { get; set; } = "";
    public bool Existed { get; set; }
    /// <summary>defaults: bool | int | string.</summary>
    public string Type { get; set; } = "";
    /// <summary>The previous value (defaults, pmset).</summary>
    public string Value { get; set; } = "";
    /// <summary>Written with administrator rights (macOS asks for the password).</summary>
    public bool Admin { get; set; }

    public string Describe() => Kind switch
    {
        "defaults" => $"{Path} {Key} = {(Existed ? Value : "(absent)")}",
        "pmset" => $"pmset {Key} = {Value}",
        "move" or "agent" => Path,
        _ => Path,
    };
}

/// <summary>One applied change (Windows History.JournalEntry).</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; set; } = DateTime.Now;
    public string FixId { get; set; } = "";
    public string Op { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public bool Reversible { get; set; }
    public DateTime? RevertedAt { get; set; }
    public List<ChangeRecord> Changes { get; set; } = new();
}

/// <summary>history/journal.json, newest first, 500 entries at most (Windows History.Journal).</summary>
public static class Journal
{
    const string File_ = "history/journal.json";
    const int Max = 500;
    static readonly object Gate = new();

    public static List<JournalEntry> Entries()
    {
        lock (Gate) return AppStore.Read<List<JournalEntry>>(File_);
    }

    public static void Add(JournalEntry e)
    {
        lock (Gate)
        {
            var list = AppStore.Read<List<JournalEntry>>(File_);
            list.Insert(0, e);
            if (list.Count > Max) list = list.Take(Max).ToList();
            AppStore.Write(File_, list);
        }
    }

    public static void MarkReverted(string id)
    {
        lock (Gate)
        {
            var list = AppStore.Read<List<JournalEntry>>(File_);
            foreach (var e in list.Where(e => e.Id == id)) e.RevertedAt = DateTime.Now;
            AppStore.Write(File_, list);
        }
    }

    /// <summary>
    /// Puts every record back, newest first (Windows ChangeRecorder.Revert): user records here, admin records in
    /// one administrator prompt. Continues past a failure; returns the last error ("" when all went back).
    /// </summary>
    public static string Revert(JournalEntry e)
    {
        string error = "";
        var admin = new List<string>();
        foreach (var r in Enumerable.Reverse(e.Changes))
        {
            try
            {
                switch (r.Kind)
                {
                    case "file":
                        if (!File.Exists(r.Backup)) { error = "backup missing: " + r.Backup; break; }
                        File.Copy(r.Backup, r.Path, overwrite: true);
                        break;
                    case "move":
                        if (!System.IO.File.Exists(r.Backup) && !Directory.Exists(r.Backup)) { error = Loc.S("mac.fix.no_longer_in_trash") + System.IO.Path.GetFileName(r.Path); break; }
                        if (System.IO.File.Exists(r.Path) || Directory.Exists(r.Path)) { error = "already exists: " + r.Path; break; }
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(r.Path)!);
                        MoveAny(r.Backup, r.Path);
                        break;
                    case "agent":
                        if (!System.IO.File.Exists(r.Backup)) { error = "missing: " + r.Backup; break; }
                        // Undoing a "disable" loads it again; undoing a "re-enable" unloads it.
                        if (r.Path.Contains("/Library/LaunchAgents/", StringComparison.Ordinal)) StartupAgents.MoveBack(r.Backup, r.Path);
                        else StartupAgents.Disable(r.Backup, r.Path);
                        break;
                    case "defaults":
                        var cmd = Defaults.RestoreCommand(r);
                        if (r.Admin) admin.Add(cmd);
                        else if (Proc.Run("/bin/sh", new[] { "-c", cmd }, 10000).ExitCode != 0 && r.Existed) error = "defaults: " + r.Key;
                        break;
                    case "pmset":
                        admin.Add(Pmset.Command(r.Key, r.Value));
                        break;
                }
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { error = x.Message; }
        }
        if (admin.Count > 0 && Admin.Run(admin) is { Length: > 0 } adminError) error = adminError;
        return error;
    }

    internal static void MoveAny(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else System.IO.File.Move(from, to);
    }
}

/// <summary>
/// Commands that need administrator rights (power settings, system update preferences): one AppleScript
/// "do shell script … with administrator privileges" for the whole batch, so macOS asks for the password once
/// (Windows: one elevated child per batch). Returns "" on success, else the reason.
/// </summary>
public static class Admin
{
    /// <summary>Test hook: the self-test records the commands instead of running them.</summary>
    public static Func<IReadOnlyList<string>, string>? Runner { get; set; }

    public static string Run(IReadOnlyList<string> commands)
    {
        if (commands.Count == 0) return "";
        if (Runner is not null) return Runner(commands);
        var script = "do shell script \"" + AppleScriptEscape(string.Join(" && ", commands)) + "\" with administrator privileges";
        var r = Proc.Run("/usr/bin/osascript", new[] { "-e", script }, 120000);
        if (r.TimedOut) return Loc.S("mac.fix.admin_timeout");
        return r.ExitCode == 0 ? "" : Loc.S("mac.fix.admin_cancelled");
    }

    public static string AppleScriptEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Single-quoted for /bin/sh.</summary>
    public static string Sh(string s) => "'" + s.Replace("'", "'\\''") + "'";
}

/// <summary>defaults read / write / delete, with the previous value captured for undo.</summary>
public static class Defaults
{
    /// <summary>(exists, type, value) of a key; type is bool / int / string.</summary>
    public static (bool Exists, string Type, string Value) Read(string domain, string key)
    {
        var r = Proc.Run("/usr/bin/defaults", new[] { "read", domain, key }, 5000);
        if (r.ExitCode != 0) return (false, "", "");
        var t = Proc.Run("/usr/bin/defaults", new[] { "read-type", domain, key }, 5000).Stdout;
        string type = t.Contains("boolean") ? "bool" : t.Contains("integer") ? "int" : "string";
        var v = r.Stdout.Trim();
        if (type == "bool") v = v == "1" ? "true" : "false";
        return (true, type, v);
    }

    public static ChangeRecord Capture(string domain, string key, bool admin)
    {
        var (exists, type, value) = Read(domain, key);
        return new ChangeRecord { Kind = "defaults", Path = domain, Key = key, Existed = exists, Type = type, Value = value, Admin = admin };
    }

    public static string WriteCommand(string domain, string key, string type, string value) =>
        $"/usr/bin/defaults write {Admin.Sh(domain)} {Admin.Sh(key)} -{type} {Admin.Sh(value)}";

    public static string RestoreCommand(ChangeRecord r) => r.Existed
        ? WriteCommand(r.Path, r.Key, r.Type.Length > 0 ? r.Type : "string", r.Value)
        : $"/usr/bin/defaults delete {Admin.Sh(r.Path)} {Admin.Sh(r.Key)} 2>/dev/null; true";
}

public static class Pmset
{
    /// <summary>pmset -g value of a setting ("powernap" → "1"), "" when unknown.</summary>
    public static string Get(string setting, string? pmsetOutput = null)
    {
        pmsetOutput ??= Proc.Run("/usr/bin/pmset", new[] { "-g" }, 5000).Stdout;
        foreach (var line in pmsetOutput.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == setting) return parts[1];
        }
        return "";
    }

    public static string Command(string setting, string value) => $"/usr/bin/pmset -a {Admin.Sh(setting)} {Admin.Sh(value)}";
}

/// <summary>A JSON settings file with a backup copy taken before the change (Windows "file" records).</summary>
public static class JsonSettings
{
    public static string BackupDir => System.IO.Path.Combine(AppStore.Root, "backups");

    /// <summary>Applies <paramref name="edit"/> to the file and returns the record (with its backup) for undo.</summary>
    public static ChangeRecord Edit(string path, Action<JsonObject> edit)
    {
        Directory.CreateDirectory(BackupDir);
        var backup = System.IO.Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{System.IO.Path.GetFileName(path).Replace(' ', '_')}.bak");
        File.Copy(path, backup, overwrite: true);
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("not a JSON object: " + path);
        edit(root);
        var tmp = path + ".dumpfinder.tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, path, overwrite: true);
        return new ChangeRecord { Kind = "file", Path = path, Backup = backup };
    }
}
