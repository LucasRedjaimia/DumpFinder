using System.Text.Json;

namespace DumpFinder.Mac.Core;

/// <summary>
/// App data in ~/Library/Application Support/DumpFinder (the Windows app uses %LOCALAPPDATA%\DumpFinder):
/// settings.json and history/ (scans.json, seen_crashes.json). Writes are atomic (temp file + rename).
/// </summary>
public static class AppStore
{
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DumpFinder");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static T Read<T>(string relative) where T : new()
    {
        try
        {
            var path = Path.Combine(Root, relative);
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new T(); }
    }

    public static void Write<T>(string relative, T value)
    {
        try
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* never block the UI on a save */ }
    }
}

/// <summary>Same fields as the Windows settings where they apply (Models.cs AppSettings).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
    /// <summary>0 = follow macOS, 1 = dark, 2 = light (same order as Windows AppThemeMode).</summary>
    public int Theme { get; set; }
    public bool SidebarCollapsed { get; set; }
    public bool HasCompletedTour { get; set; }
    /// <summary>Section tips already shown ("tab1,tab2"), Windows SeenTips.</summary>
    public string SeenTips { get; set; } = "";
    public bool UpdateCheckDisabled { get; set; }
    /// <summary>Version the user chose to skip ("0.3.0"); the next one is offered again.</summary>
    public string SkippedUpdateVersion { get; set; } = "";
    /// <summary>AiProvider index (0 Gemini, 1 Claude, 2 Ollama), as on Windows. The API key is in the Keychain (SecretStore).</summary>
    public int AiProvider { get; set; }
    public string ClaudeModel { get; set; } = "";
    public string OllamaModel { get; set; } = "";

    public static AppSettings Load() => AppStore.Read<AppSettings>("settings.json");
    public void Save() => AppStore.Write("settings.json", this);
}

/// <summary>One scan in history/scans.json (Windows: History.ScanRecord).</summary>
public sealed class ScanRecord
{
    public DateTime Time { get; set; }
    public int Score { get; set; }
    public int Crashes30 { get; set; }
    public int Severe30 { get; set; }
    public string Diagnosis { get; set; } = "";
}

public static class History
{
    const int MaxScans = 365, MaxSeen = 5000;

    public static List<ScanRecord> Scans() => AppStore.Read<List<ScanRecord>>("history/scans.json");

    /// <summary>Last scan of an earlier day, for the "↑ +N since …" pill (Windows History.PreviousDay).</summary>
    public static ScanRecord? PreviousDay(DateTime now) =>
        Scans().Where(s => s.Time.Date < now.Date).OrderByDescending(s => s.Time).FirstOrDefault();

    public static void AddScan(ScanRecord r)
    {
        var list = Scans();
        list.Add(r);
        if (list.Count > MaxScans) list = list.Skip(list.Count - MaxScans).ToList();
        AppStore.Write("history/scans.json", list);
    }

    /// <summary>
    /// Crash keys not seen at the previous scan ("New" pill). The very first scan only records them,
    /// so an existing history is not shown as new (same rule as Windows).
    /// </summary>
    public static HashSet<string> MarkSeen(IEnumerable<string> keys)
    {
        var path = "history/seen_crashes.json";
        bool first = !File.Exists(Path.Combine(AppStore.Root, path));
        var seen = AppStore.Read<List<string>>(path);
        var set = new HashSet<string>(seen);
        var fresh = new HashSet<string>(keys.Where(k => !set.Contains(k)));
        if (fresh.Count > 0 || first)
        {
            seen.AddRange(fresh);
            if (seen.Count > MaxSeen) seen = seen.Skip(seen.Count - MaxSeen).ToList();
            AppStore.Write(path, seen);
        }
        return first ? new HashSet<string>() : fresh;
    }
}
