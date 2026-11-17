using System.Diagnostics;

namespace DumpFinder.Mac.Core;

/// <summary>Runs a system tool (absolute path, fixed arguments) and captures its output, with a timeout.</summary>
public static class Proc
{
    public sealed record Result(int ExitCode, string Stdout, bool TimedOut);

    public static Result Run(string file, IEnumerable<string> args, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Stable, parseable output whatever the user's language (decimal separators, messages).
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"cannot start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new Result(-1, "", true);
        }
        return new Result(p.ExitCode, stdout.Result, false);
    }

    public static string Sysctl(string name)
    {
        var r = Run("/usr/sbin/sysctl", new[] { "-n", name }, 5000);
        return r.ExitCode == 0 ? r.Stdout.Trim() : "";
    }
}
