using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public class DeepAnalysisResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string BucketId { get; set; }
        public string ModuleName { get; set; }
        public string ImageName { get; set; }
        public string SymbolName { get; set; }
        public string ProcessName { get; set; }
        public string ImageVersion { get; set; }
        public List<string> StackFrames { get; set; }
        public string RawOutputPath { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public double DurationSeconds { get; set; }
    }

    /// <summary>
    /// Optional "deep" analysis through the Windows debugger (cdb.exe from WinDbg / Windows SDK):
    /// runs <c>!analyze -v</c> with Microsoft's public symbol server and extracts the failure bucket,
    /// faulting image and symbolized stack. Results are cached per dump file.
    /// </summary>
    public static class DeepAnalyzer
    {
        public const string WingetId = "Microsoft.WinDbg";
        private const string SymbolServer = "https://msdl.microsoft.com/download/symbols";

        private static string BaseDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder"); }
        }

        public static string CacheDir { get { return Path.Combine(BaseDir, "analysis"); } }
        public static string SymbolCache { get { return Path.Combine(BaseDir, "symbols"); } }

        /// <summary>Locates cdb.exe: WinDbg (Store / winget) app alias first, then the Windows SDK.</summary>
        public static string FindCdb()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates =
            {
                Path.Combine(local, @"Microsoft\WindowsApps\cdbX64.exe"),
                Path.Combine(pf86, @"Windows Kits\10\Debuggers\x64\cdb.exe"),
                Path.Combine(pf, @"Windows Kits\10\Debuggers\x64\cdb.exe")
            };
            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch (Exception ex) { Log.Warn(ex, c); }
            }
            return null;
        }

        public static bool IsAvailable { get { return FindCdb() != null; } }

        private static string CacheKey(string dumpPath)
        {
            var fi = new FileInfo(dumpPath);
            return Path.GetFileNameWithoutExtension(fi.Name) + "_" + fi.Length + "_" + fi.LastWriteTimeUtc.Ticks;
        }

        public static DeepAnalysisResult TryLoadCached(string dumpPath)
        {
            try
            {
                if (string.IsNullOrEmpty(dumpPath) || !File.Exists(dumpPath)) return null;
                string json = Path.Combine(CacheDir, CacheKey(dumpPath) + ".json");
                if (!File.Exists(json)) return null;
                return new JavaScriptSerializer().Deserialize<DeepAnalysisResult>(File.ReadAllText(json, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, dumpPath);
                return null;
            }
        }

        /// <summary>Blocking: runs cdb (≈15 s once symbols are cached, longer the first time). Call off the UI thread.</summary>
        public static DeepAnalysisResult Analyze(string dumpPath, int timeoutMs = 300000)
        {
            var result = new DeepAnalysisResult { AnalyzedAt = DateTime.Now, StackFrames = new List<string>() };
            string cdb = FindCdb();
            if (cdb == null) { result.Error = "cdb.exe not found"; return result; }
            if (!File.Exists(dumpPath)) { result.Error = "dump not readable: " + dumpPath; return result; }

            var sw = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(SymbolCache);
                var psi = new ProcessStartInfo(cdb,
                    "-z \"" + dumpPath + "\" -y \"srv*" + SymbolCache + "*" + SymbolServer + "\" -c \"!analyze -v; q\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                string output;
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch (Exception kex) { Log.Warn(kex); }
                        result.Error = "timeout";
                        return result;
                    }
                    output = outTask.Result;
                }

                string key = CacheKey(dumpPath);
                result.RawOutputPath = Path.Combine(CacheDir, key + ".txt");
                File.WriteAllText(result.RawOutputPath, output, Encoding.UTF8);

                ParseAnalyze(output, result);
                result.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                result.Ok = !string.IsNullOrEmpty(result.BucketId) || !string.IsNullOrEmpty(result.ImageName);
                if (!result.Ok) result.Error = "no !analyze output";

                File.WriteAllText(Path.Combine(CacheDir, key + ".json"), new JavaScriptSerializer().Serialize(result), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "cdb " + dumpPath);
                result.Error = ex.Message;
            }
            return result;
        }

        private static void ParseAnalyze(string output, DeepAnalysisResult r)
        {
            string[] lines = output.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                r.BucketId = r.BucketId ?? Field(l, "FAILURE_BUCKET_ID:");
                r.ModuleName = r.ModuleName ?? Field(l, "MODULE_NAME:");
                r.ImageName = r.ImageName ?? Field(l, "IMAGE_NAME:");
                r.SymbolName = r.SymbolName ?? Field(l, "SYMBOL_NAME:");
                r.ProcessName = r.ProcessName ?? Field(l, "PROCESS_NAME:");
                r.ImageVersion = r.ImageVersion ?? Field(l, "IMAGE_VERSION:");

                if (l == "STACK_TEXT:" && r.StackFrames.Count == 0)
                {
                    for (int j = i + 1; j < lines.Length && r.StackFrames.Count < 16; j++)
                    {
                        string s = lines[j].Trim();
                        if (s.Length == 0) break;
                        int cut = s.LastIndexOf(" : ", StringComparison.Ordinal);
                        string frame = cut >= 0 ? s.Substring(cut + 3).Trim() : s;
                        if (frame.Length > 0) r.StackFrames.Add(frame);
                    }
                }
            }
        }

        private static string Field(string line, string key)
        {
            if (!line.StartsWith(key, StringComparison.Ordinal)) return null;
            string v = line.Substring(key.Length).Trim();
            return v.Length > 0 ? v : null;
        }

        /// <summary>Opens a visible console that installs WinDbg with winget (user-initiated).</summary>
        public static bool LaunchInstaller()
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/k winget install --id " + WingetId + " -e --accept-source-agreements && echo. && echo WinDbg installe : revenez dans Dump Finder.")
                { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "winget");
                return false;
            }
        }
    }
}
