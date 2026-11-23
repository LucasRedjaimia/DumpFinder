using System.Security;

namespace DumpFinder.Mac.Core;

/// <summary>What a new diagnostic report means for the Sentinel (Windows TraySentinel.LiveAlert).</summary>
public enum LiveAlert { None, Panic, Memory }

/// <summary>
/// Watches the diagnostic report folders (Windows: the System event log) and raises an alert when macOS
/// writes a kernel panic or a low-memory (Jetsam) report. App crashes do not alert, as on Windows.
/// </summary>
public sealed class CrashWatcher : IDisposable
{
    readonly List<FileSystemWatcher> _watchers = new();
    readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Raised on a thread-pool thread with the kind and the report path.</summary>
    public event Action<LiveAlert, string>? Alert;

    public void Start()
    {
        Stop();
        foreach (var dir in new[] { DiagnosticReports.SystemDir, DiagnosticReports.UserDir })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var w = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
                w.Created += OnFile;
                w.Renamed += (s, e) => OnFile(s, e);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    public void Stop()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    void OnFile(object? sender, FileSystemEventArgs e)
    {
        var kind = Classify(e.Name ?? "");
        if (kind == LiveAlert.None) return;
        lock (_seen) { if (!_seen.Add(e.FullPath)) return; }   // created then renamed: one alert
        Alert?.Invoke(kind, e.FullPath);
    }

    /// <summary>"panic-full-2026-11-17-101112.panic", "Kernel-….panic" → Panic; "JetsamEvent-….ips" → Memory.</summary>
    public static LiveAlert Classify(string fileName)
    {
        if (fileName.EndsWith(".panic", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("panic", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".ips", StringComparison.OrdinalIgnoreCase)))
            return LiveAlert.Panic;
        if (fileName.StartsWith("JetsamEvent", StringComparison.OrdinalIgnoreCase)) return LiveAlert.Memory;
        return LiveAlert.None;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// "Open at login" (Windows: HKCU Run key with --tray): a per-user LaunchAgent that starts the app with --tray,
/// no window. Nothing needs an administrator. The path follows the app when it moves (RefreshPathIfEnabled).
/// </summary>
public static class LoginItem
{
    public const string TrayArg = "--tray";
    public const string Label = "io.github.lucasredjaimia.dumpfinder.sentinel";

    /// <summary>Test hook: the self-test points this at a temporary folder.</summary>
    public static string AgentsDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
    static string PlistPath => Path.Combine(AgentsDir, Label + ".plist");

    public static bool IsEnabled
    {
        get
        {
            try { return File.Exists(PlistPath) && File.ReadAllText(PlistPath).Contains("<string>" + TrayArg + "</string>", StringComparison.Ordinal); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>False when it could not be changed (or the app runs from a translocated copy).</summary>
    public static bool Set(bool enabled, string exe)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(PlistPath)) File.Delete(PlistPath);
                return true;
            }
            if (McpServer.IsTranslocated(exe)) return false;
            Directory.CreateDirectory(AgentsDir);
            var tmp = PlistPath + ".tmp";
            File.WriteAllText(tmp, Plist(exe));
            File.Move(tmp, PlistPath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Called on every normal launch (Windows Autostart.RefreshPathIfEnabled, same rule). Repoints the agent at the
    /// running binary when the registered one is gone (the app was moved or deleted) or is an older Dump Finder (a new
    /// version unzipped somewhere else while the old app is still there). Another copy of the same or an older version
    /// (a second download, a dev build, `dotnet run`) never takes it over.
    /// </summary>
    public static void RefreshPathIfEnabled(string exe)
    {
        try
        {
            if (!File.Exists(PlistPath) || McpServer.IsTranslocated(exe)) return;
            if (ShouldRepoint(RegisteredExe(File.ReadAllText(PlistPath)), IsEnabled, exe, File.Exists, VersionOf, UpdateChecker.CurrentVersion))
                Set(true, exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Pure decision behind <see cref="RefreshPathIfEnabled"/>. True only when the option is on, the agent does not
    /// already name <paramref name="currentExe"/>, and the binary it names is unusable (no path, file missing) or an
    /// existing Dump Finder older than <paramref name="currentVersion"/>. An unknown version (not Dump Finder, not in
    /// an app bundle, unreadable) is left alone.
    /// </summary>
    public static bool ShouldRepoint(string? registeredExe, bool enabled, string currentExe, Func<string, bool> fileExists,
                                     Func<string, Version?> versionOf, Version? currentVersion)
    {
        if (!enabled || string.IsNullOrEmpty(currentExe)) return false;
        if (string.IsNullOrEmpty(registeredExe) || !registeredExe.StartsWith('/')) return true;   // launchd cannot start anything from it
        if (string.Equals(registeredExe, currentExe, StringComparison.Ordinal)) return false;
        bool exists;
        try { exists = fileExists(registeredExe); }
        catch (Exception) { exists = true; }                                                       // when in doubt, leave the user's agent alone
        if (!exists) return true;
        Version? registered;
        try { registered = versionOf(registeredExe); }
        catch (Exception) { registered = null; }
        return registered is not null && currentVersion is not null && registered < currentVersion;
    }

    /// <summary>The binary the agent starts: the first ProgramArguments string, unescaped; null when there is none.</summary>
    public static string? RegisteredExe(string plist)
    {
        var m = System.Text.RegularExpressions.Regex.Match(plist, @"<key>ProgramArguments</key>\s*<array>\s*<string>([^<]*)</string>");
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
    }

    /// <summary>
    /// The version of a Dump Finder binary, read from its bundle (…/Dump Finder.app/Contents/MacOS/DumpFinder →
    /// Contents/Info.plist): null when it is not inside a Dump Finder bundle or the version cannot be read.
    /// </summary>
    public static Version? VersionOf(string exe)
    {
        try
        {
            var dir = Path.GetDirectoryName(exe);
            if (dir is null || Path.GetFileName(dir) != "MacOS") return null;
            var info = Path.Combine(Path.GetDirectoryName(dir)!, "Info.plist");
            if (!File.Exists(info)) return null;
            string text = File.ReadAllText(info);
            if (PlistString(text, "CFBundleIdentifier") != BundleId) return null;
            return Version.TryParse(PlistString(text, "CFBundleShortVersionString"), out var v) ? v : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>Bundle identifier written by mac/build.sh.</summary>
    public const string BundleId = "io.github.lucasredjaimia.dumpfinder";

    static string? PlistString(string plist, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(plist, "<key>" + key + @"</key>\s*<string>([^<]*)</string>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static string Plist(string exe) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(exe)}</string>
            <string>{TrayArg}</string>
          </array>
          <key>RunAtLoad</key><true/>
          <key>ProcessType</key><string>Interactive</string>
        </dict>
        </plist>

        """;
}
