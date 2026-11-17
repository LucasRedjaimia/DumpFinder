using System.Diagnostics;

namespace DumpFinder.Mac.Core;

/// <summary>
/// The AI API key lives in the login Keychain (Windows: DPAPI), never in settings.json.
/// It goes through /usr/bin/security: the Keychain item then trusts that tool rather than this ad-hoc signed app,
/// so an app update does not trigger a Keychain prompt. The key is written through "security -i" on stdin,
/// never as a command-line argument (arguments are visible to other processes).
/// </summary>
public static class SecretStore
{
    const string Service = "DumpFinder.AiApiKey";
    const string Account = "DumpFinder";
    const string Tool = "/usr/bin/security";

    /// <summary>Test hook: the self-test swaps the Keychain for memory.</summary>
    public static Dictionary<string, string>? InMemory { get; set; }

    public static string Load()
    {
        if (InMemory is not null) return InMemory.GetValueOrDefault(Service, "");
        var r = Proc.Run(Tool, new[] { "find-generic-password", "-s", Service, "-a", Account, "-w" }, 10000);
        return r.ExitCode == 0 ? r.Stdout.TrimEnd('\n', '\r') : "";
    }

    /// <summary>Saves (or, for an empty key, deletes) the key. False when the Keychain refused it.</summary>
    public static bool Save(string key)
    {
        key = key.Trim();
        if (InMemory is not null)
        {
            if (key.Length == 0) InMemory.Remove(Service); else InMemory[Service] = key;
            return true;
        }
        if (key.Length == 0)
        {
            Proc.Run(Tool, new[] { "delete-generic-password", "-s", Service, "-a", Account }, 10000);
            return true;
        }
        // API keys are plain ASCII tokens; anything that could break the quoted command is refused.
        if (!IsSafe(key)) return false;
        try
        {
            var psi = new ProcessStartInfo(Tool) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-i");
            using var p = Process.Start(psi);
            if (p is null) return false;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            p.StandardInput.WriteLine($"add-generic-password -U -s {Service} -a {Account} -l \"Dump Finder AI key\" -w \"{key}\"");
            p.StandardInput.Close();
            if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { /* gone */ } return false; }
            return Load() == key;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    public static bool IsSafe(string key) => key.Length is > 0 and < 512 && key.All(c => c is > ' ' and < '\x7f' && c is not '"' and not '\\');
}
