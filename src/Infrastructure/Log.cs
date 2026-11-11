using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>
    /// Minimal thread-safe file logger: %LOCALAPPDATA%\DumpFinder\logs\dumpfinder-yyyyMMdd.log.
    /// Never throws, never writes to stdout (stdout is reserved for the MCP stdio transport).
    /// Replaces the silent <c>catch { }</c> blocks so failures leave a trace.
    /// </summary>
    public static class Log
    {
        private const int RetentionDays = 14;
        private static readonly object Sync = new object();
        private static string _dir;
        private static bool _pruned;

        public static string Directory
        {
            get
            {
                if (_dir == null)
                {
                    _dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DumpFinder", "logs");
                }
                return _dir;
            }
        }

        public static void Info(string message,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "")
        {
            Write("INFO ", message, null, member, file);
        }

        public static void Warn(string message,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "")
        {
            Write("WARN ", message, null, member, file);
        }

        /// <summary>Expected/benign failure (missing registry key, WMI class absent, access denied...).</summary>
        public static void Warn(Exception ex, string context = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "")
        {
            Write("WARN ", context, ex, member, file);
        }

        /// <summary>Unexpected failure that likely degrades a feature.</summary>
        public static void Error(Exception ex, string context = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "")
        {
            Write("ERROR", context, ex, member, file);
        }

        private static void Write(string level, string message, Exception ex, string member, string file)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(' ').Append(level).Append(' ');
                sb.Append(Path.GetFileNameWithoutExtension(file)).Append('.').Append(member);
                if (!string.IsNullOrEmpty(message)) sb.Append(" | ").Append(message);
                if (ex != null)
                {
                    sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    if (level == "ERROR" && ex.StackTrace != null)
                        sb.AppendLine().Append(ex.StackTrace);
                }
                sb.AppendLine();

                lock (Sync)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    if (!_pruned) { _pruned = true; Prune(); }
                    string path = Path.Combine(Directory, "dumpfinder-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }

        private static void Prune()
        {
            try
            {
                DateTime limit = DateTime.Now.AddDays(-RetentionDays);
                foreach (string f in System.IO.Directory.GetFiles(Directory, "dumpfinder-*.log"))
                {
                    if (File.GetLastWriteTime(f) < limit) File.Delete(f);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
