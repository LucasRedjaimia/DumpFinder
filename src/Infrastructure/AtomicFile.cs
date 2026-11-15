using System;
using System.IO;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>
    /// Writes a file through a temporary copy then swaps it in: a crash or power cut in the middle leaves the old
    /// file intact instead of a truncated one (which used to reset the settings and lose the saved API key).
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string content, Encoding encoding)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, encoding))
            {
                sw.Write(content);
                sw.Flush();
                fs.Flush(true); // on disk before the swap
            }
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }

        /// <summary>Keeps a copy of an unreadable file before it gets rewritten. Never throws.</summary>
        public static void Quarantine(string path)
        {
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch (Exception ex) { Log.Warn(ex, path); }
        }
    }
}
