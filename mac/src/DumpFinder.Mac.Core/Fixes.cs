using System.Text.Json.Nodes;

namespace DumpFinder.Mac.Core;

/// <summary>Windows FixStatus: Applied ("Active"), Recommended, Optional, Info ("Tool" / not installed).</summary>
public enum FixStatus { Applied, Recommended, Optional, Info }

/// <summary>Windows FixGroup, with the Mac groups that have fixes.</summary>
public enum FixGroup { All, Gpu, BrowserApps, SystemMaint }

/// <summary>A fix card (Windows RegistryFixItem).</summary>
public sealed record FixItem(string Id, FixGroup Group, string Tag, string Title, string Explanation, string CurrentValue, string Target,
                             FixStatus Status, bool Admin, bool CanRestore, string ApplyLabel, string RestoreLabel, string Process = "")
{
    /// <summary>Tool fixes (Info) only open something: nothing to journal.</summary>
    public bool IsTool => Id == "first_aid";
}

/// <summary>What an apply / restore did (Windows FixRunner result).</summary>
public sealed record OpResult(string Op, bool Ok, string Message, List<ChangeRecord> Changes);

/// <summary>Where user-level files live; the self-test points it at a temporary home.</summary>
public static class MacPaths
{
    public static string Home { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AppSupport => Path.Combine(Home, "Library", "Application Support");
}

/// <summary>
/// The Mac fixes (Windows DiagnosticsEngine.InspectAllFixes + ExecuteFixOperation). Mac equivalents of the Windows
/// browser and Discord GPU fixes, power and update settings, and a repair tool. Every change is captured for undo;
/// power and system update settings need the administrator password.
/// </summary>
public static class FixCatalog
{
    /// <summary>Chromium browsers: id, app name (as macOS knows it), tag, Local State path under Application Support.</summary>
    public static readonly (string Id, string App, string Tag, string LocalState)[] Browsers =
    {
        ("chrome_gpu", "Google Chrome", "CHROME", "Google/Chrome/Local State"),
        ("edge_gpu", "Microsoft Edge", "EDGE", "Microsoft Edge/Local State"),
        ("brave_gpu", "Brave Browser", "BRAVE", "BraveSoftware/Brave-Browser/Local State"),
    };

    static string DiscordSettings => Path.Combine(MacPaths.AppSupport, "discord", "settings.json");
    const string UpdateDomain = "/Library/Preferences/com.apple.SoftwareUpdate";

    static string S(string id, string part) => Loc.S($"mac.fix.{id}.{part}");

    /// <summary>Re-reads the live state of every fix; crashes and panics of the last 30 days make some Recommended.</summary>
    public static List<FixItem> Inspect(ScanResult? r, StabilitySummary? s)
    {
        var list = new List<FixItem>();
        var recent = s?.Crashes.Where(c => (DateTime.Now - c.Time).TotalDays <= 30).ToList() ?? new List<CrashEntry>();
        bool CrashedFrom(string app) => recent.Any(c => c.Report?.Process.StartsWith(app, StringComparison.OrdinalIgnoreCase) == true);

        foreach (var (id, app, tag, ls) in Browsers)
        {
            var path = Path.Combine(MacPaths.AppSupport, ls);
            bool installed = File.Exists(path);
            bool off = installed && HardwareAccelerationOff(path, "hardware_acceleration_mode", "enabled");
            var status = !installed ? FixStatus.Info : off ? FixStatus.Applied : CrashedFrom(app) ? FixStatus.Recommended : FixStatus.Optional;
            if (!installed && id != "chrome_gpu") continue;   // Chrome always shown (Windows: chrome_graphite), the others when installed
            list.Add(new FixItem(id, FixGroup.BrowserApps, tag, Loc.F("mac.fix.browser.title", app), Loc.F("mac.fix.browser.explain", app),
                !installed ? Loc.S("mac.fix.not_installed") : off ? Loc.S("mac.fix.gpu_off") : Loc.S("mac.fix.gpu_default"),
                "~/Library/Application Support/" + ls + "  •  hardware_acceleration_mode.enabled",
                status, false, installed && off, Loc.S("mac.fix.browser.apply"), Loc.S("mac.fix.restore_default"), app));
        }

        bool discord = File.Exists(DiscordSettings);
        bool discordOff = discord && HardwareAccelerationOff(DiscordSettings, "enableHardwareAcceleration", null);
        list.Add(new FixItem("discord_hwaccel", FixGroup.BrowserApps, "DISCORD", S("discord", "title"), S("discord", "explain"),
            !discord ? Loc.S("mac.fix.not_installed") : discordOff ? Loc.S("mac.fix.gpu_off") : Loc.S("mac.fix.gpu_default"),
            "~/Library/Application Support/discord/settings.json  •  enableHardwareAcceleration",
            !discord ? FixStatus.Info : discordOff ? FixStatus.Applied : CrashedFrom("Discord") ? FixStatus.Recommended : FixStatus.Optional,
            false, discordOff, S("discord", "apply"), Loc.S("mac.fix.restore_default"), "Discord"));

        // Power Nap: wakes the Mac to sync while it sleeps; a common trigger of sleep / wake panics.
        var nap = Pmset.Get("powernap");
        bool sleepPanics = r?.Panics.Any(p => p.PatternId == "sleep_wake" && p.Time is { } t && (DateTimeOffset.Now - t).TotalDays <= 30) == true;
        list.Add(new FixItem("powernap", FixGroup.SystemMaint, "POWER", S("powernap", "title"), S("powernap", "explain"),
            nap == "0" ? S("powernap", "off") : nap == "1" ? S("powernap", "on") : Loc.S("mac.cf.unknown"), "pmset -a powernap",
            nap == "" ? FixStatus.Info : nap == "0" ? FixStatus.Applied : sleepPanics ? FixStatus.Recommended : FixStatus.Optional,
            true, nap == "0", S("powernap", "apply"), S("powernap", "restore")));

        // Security responses and system data files installed automatically (System Settings › Software Update).
        var critical = Defaults.Read(UpdateDomain, "CriticalUpdateInstall");
        var config = Defaults.Read(UpdateDomain, "ConfigDataInstall");
        bool updatesOn = critical is { Exists: true, Value: "true" } && config is { Exists: true, Value: "true" };
        list.Add(new FixItem("security_updates", FixGroup.SystemMaint, "UPDATE", S("security_updates", "title"), S("security_updates", "explain"),
            updatesOn ? S("security_updates", "on") : S("security_updates", "off"), UpdateDomain + "  •  CriticalUpdateInstall, ConfigDataInstall",
            updatesOn ? FixStatus.Applied : FixStatus.Recommended, true, false, S("security_updates", "apply"), ""));

        // Repair tool (Windows: SFC /scannow): First Aid in Disk Utility.
        list.Add(new FixItem("first_aid", FixGroup.SystemMaint, "SSD", S("first_aid", "title"), S("first_aid", "explain"),
            S("first_aid", "ready"), "Disk Utility › First Aid", FixStatus.Info, false, false, S("first_aid", "apply"), ""));
        return list;
    }

    static bool HardwareAccelerationOff(string path, string key, string? sub)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path))?[key];
            if (sub is not null) node = node?[sub];
            return node?.GetValueKind() == System.Text.Json.JsonValueKind.False;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    /// <summary>Tool fixes open their app; everything else goes through <see cref="Run"/>.</summary>
    public const string DiskUtility = HardwareChecks.DiskUtility;

    /// <summary>
    /// Runs "&lt;id&gt;_apply" / "&lt;id&gt;_restore" ops. Admin ops share one password prompt. The caller quits a running
    /// browser or Discord first (they rewrite their settings when they quit).
    /// </summary>
    public static List<OpResult> Run(IReadOnlyList<string> ops)
    {
        var results = new List<OpResult>();
        var adminCmds = new List<string>();
        var adminResults = new List<OpResult>();
        foreach (var op in ops)
        {
            bool restore = op.EndsWith("_restore", StringComparison.Ordinal);
            var id = op[..op.LastIndexOf('_')];
            try
            {
                var browser = Browsers.FirstOrDefault(b => b.Id == id);
                if (browser.Id is not null)
                {
                    var path = Path.Combine(MacPaths.AppSupport, browser.LocalState);
                    if (!File.Exists(path)) { results.Add(new(op, false, Loc.S("mac.fix.not_installed"), new())); continue; }
                    var rec = JsonSettings.Edit(path, root =>
                    {
                        if (restore) { (root["hardware_acceleration_mode"] as JsonObject)?.Remove("enabled"); return; }
                        if (root["hardware_acceleration_mode"] is not JsonObject mode) root["hardware_acceleration_mode"] = mode = new JsonObject();
                        mode["enabled"] = false;
                    });
                    results.Add(new(op, true, Loc.F(restore ? "mac.fix.restored" : "mac.fix.applied_restart", browser.App), new() { rec }));
                }
                else if (id == "discord_hwaccel")
                {
                    if (!File.Exists(DiscordSettings)) { results.Add(new(op, false, Loc.S("mac.fix.not_installed"), new())); continue; }
                    var rec = JsonSettings.Edit(DiscordSettings, root =>
                    {
                        if (restore) root.Remove("enableHardwareAcceleration"); else root["enableHardwareAcceleration"] = false;
                    });
                    results.Add(new(op, true, Loc.F(restore ? "mac.fix.restored" : "mac.fix.applied_restart", "Discord"), new() { rec }));
                }
                else if (id == "powernap")
                {
                    var before = Pmset.Get("powernap");
                    if (before == "") { results.Add(new(op, false, Loc.S("mac.cf.unknown"), new())); continue; }
                    adminCmds.Add(Pmset.Command("powernap", restore ? "1" : "0"));
                    adminResults.Add(new(op, true, Loc.S(restore ? "mac.fix.powernap.restored" : "mac.fix.powernap.done"),
                        new() { new ChangeRecord { Kind = "pmset", Key = "powernap", Value = before, Admin = true } }));
                }
                else if (id == "security_updates" && !restore)
                {
                    var recs = new List<ChangeRecord> { Defaults.Capture(UpdateDomain, "CriticalUpdateInstall", true), Defaults.Capture(UpdateDomain, "ConfigDataInstall", true) };
                    adminCmds.Add(Defaults.WriteCommand(UpdateDomain, "CriticalUpdateInstall", "bool", "true"));
                    adminCmds.Add(Defaults.WriteCommand(UpdateDomain, "ConfigDataInstall", "bool", "true"));
                    adminResults.Add(new(op, true, Loc.S("mac.fix.security_updates.done"), recs));
                }
                else results.Add(new(op, false, Loc.S("mac.fix.unknown"), new()));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                results.Add(new(op, false, e.Message, new()));
            }
        }
        if (adminCmds.Count > 0)
        {
            var err = Admin.Run(adminCmds);
            results.AddRange(err.Length == 0 ? adminResults : adminResults.Select(a => a with { Ok = false, Message = err, Changes = new() }));
        }
        // Same order as asked (Windows: results come back in the order of the requested ops).
        return ops.Select(o => results.First(x => x.Op == o)).ToList();
    }

    /// <summary>Journals each result (Windows JournalResults) and returns them.</summary>
    public static List<OpResult> RunAndRecord(IReadOnlyList<string> ops, Func<string, string> titleOf)
    {
        var results = Run(ops);
        foreach (var res in results)
            Journal.Add(new JournalEntry
            {
                Op = res.Op, FixId = res.Op[..res.Op.LastIndexOf('_')], Title = titleOf(res.Op), Ok = res.Ok, Message = res.Message,
                Changes = res.Changes, Reversible = res.Ok && res.Changes.Count > 0,
            });
        return results;
    }
}
