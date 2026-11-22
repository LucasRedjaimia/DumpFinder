using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WinCrashDoctor
{
    /// <summary>
    /// In-app update: downloads the new DumpFinder.exe from this repository's GitHub release, checks it, puts it in
    /// place of the running exe (same path, so the startup entry and MCP configurations keep working) and restarts.
    ///
    /// Windows refuses to delete a running exe but lets it be renamed on the same volume: the running file becomes
    /// "&lt;name&gt;.old_&lt;random&gt;.exe", the new one takes its name, and the next launch deletes the old one.
    /// Checks before anything is replaced: the file comes from this repository (redirects only to GitHub's download
    /// hosts), its SHA-256 equals the digest GitHub publishes for the asset, and it is a Dump Finder exe of exactly
    /// the announced version. Anything wrong leaves the current exe untouched.
    /// </summary>
    public static class SelfUpdate
    {
        public const string UpdatedArg = "--updated";
        private const long MaxSize = 200L << 20;
        private const string OldMarker = ".old_";

        public static string ExePath { get { return Elevation.ExePath; } }

        /// <summary>Hosts a release download may be redirected to (GitHub's own download servers), HTTPS only.</summary>
        public static bool IsDownloadHost(Uri u)
        {
            if (u == null || u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h == "objects.githubusercontent.com" || h == "release-assets.githubusercontent.com"
                   || h == "github-releases.githubusercontent.com";
        }

        /// <summary>Why this release cannot be installed from the app (null when it can).</summary>
        public static string CannotInstall(UpdateInfo u)
        {
            if (u == null || u.ExeUrl == null) return "no exe in this release";
            if (u.ExeSha256 == null) return "no checksum published for the exe";
            if (u.ExeSize <= 0 || u.ExeSize > MaxSize) return "unexpected size";
            return null;
        }

        /// <summary>
        /// Downloads the release exe next to the running one ("&lt;name&gt;.download") and checks it. Returns its path;
        /// throws <see cref="UpdateException"/> with a user-facing reason (the partial file is removed).
        /// </summary>
        public static async Task<string> DownloadAsync(UpdateInfo u, IProgress<double> progress, CancellationToken ct)
        {
            string why = CannotInstall(u);
            if (why != null) throw new UpdateException(Localizer.S("self_update.not_installable"), why);
            string dir = Path.GetDirectoryName(ExePath);
            string target = Path.Combine(dir, Path.GetFileNameWithoutExtension(ExePath) + ".download");
            try
            {
                if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("DumpFinder-Update");
                    Uri url = new Uri(u.ExeUrl);
                    for (int hop = 0; ; hop++)
                    {
                        if (!IsDownloadHost(url)) throw new UpdateException(Localizer.S("self_update.failed_download"), "host not allowed: " + url.Host);
                        using (HttpResponseMessage resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                        {
                            int code = (int)resp.StatusCode;
                            if (code >= 300 && code < 400 && resp.Headers.Location != null && hop < 5)
                            {
                                url = resp.Headers.Location.IsAbsoluteUri ? resp.Headers.Location : new Uri(url, resp.Headers.Location);
                                continue;
                            }
                            if (!resp.IsSuccessStatusCode) throw new UpdateException(Localizer.S("self_update.failed_download"), "HTTP " + code);
                            long? len = resp.Content.Headers.ContentLength;
                            if (len.HasValue && len.Value != u.ExeSize) throw new UpdateException(Localizer.S("self_update.failed_check"), "size " + len + " ≠ " + u.ExeSize);
                            using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                var buf = new byte[81920];
                                long total = 0;
                                int n;
                                while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                                {
                                    total += n;
                                    if (total > u.ExeSize) throw new UpdateException(Localizer.S("self_update.failed_check"), "larger than announced");
                                    await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                                    if (progress != null) progress.Report((double)total / u.ExeSize);
                                }
                            }
                            break;
                        }
                    }
                }
                string bad = Verify(target, u);
                if (bad != null) throw new UpdateException(Localizer.S("self_update.failed_check"), bad);
                return target;
            }
            catch (UnauthorizedAccessException ex) { TryDelete(target); throw new UpdateException(Localizer.S("self_update.folder_read_only"), ex.Message); }
            catch (UpdateException) { TryDelete(target); throw; }
            catch (OperationCanceledException) { TryDelete(target); throw; }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
            {
                TryDelete(target);
                throw new UpdateException(Localizer.S("self_update.failed_download"), ex.Message);
            }
        }

        /// <summary>Null when the file is the announced exe: size, SHA-256 digest, Dump Finder product, exact version.</summary>
        public static string Verify(string file, UpdateInfo u)
        {
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length != u.ExeSize) return "size " + (fi.Exists ? fi.Length : 0) + " ≠ " + u.ExeSize;
            string hash = Sha256(file);
            if (!string.Equals(hash, u.ExeSha256, StringComparison.OrdinalIgnoreCase)) return "SHA-256 " + hash + " ≠ " + u.ExeSha256;
            FileVersionInfo v;
            try { v = FileVersionInfo.GetVersionInfo(file); } catch (Exception ex) { return "not an exe: " + ex.Message; }
            if (v.ProductName != "Dump Finder") return "product " + v.ProductName;
            Version fileVersion = UpdateChecker.ParseVersion(v.FileVersion);
            if (fileVersion == null || fileVersion != u.Version) return "version " + v.FileVersion + " ≠ " + u.Version;
            return null;
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        /// <summary>
        /// Puts <paramref name="downloaded"/> in place of <paramref name="exe"/>: the current file is renamed aside
        /// (allowed while it runs), the new one takes its name. On any failure the current exe is put back.
        /// Returns the path the old exe was moved to.
        /// </summary>
        public static string Swap(string downloaded, string exe)
        {
            string dir = Path.GetDirectoryName(exe);
            string old = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + OldMarker + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            try { File.Move(exe, old); }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                throw new UpdateException(Localizer.S("self_update.folder_read_only"), ex.Message);
            }
            try { File.Move(downloaded, exe); }
            catch (Exception ex)
            {
                try { File.Move(old, exe); } catch (Exception back) { Log.Error(back, "update rollback"); }
                throw new UpdateException(Localizer.S("self_update.failed_replace"), ex.Message);
            }
            return old;
        }

        /// <summary>Starts the updated exe (it shows "updated to …" and cleans up), so the caller can exit.</summary>
        public static void Restart(string exe, Version version)
        {
            Process.Start(new ProcessStartInfo(exe, UpdatedArg + " " + version.ToString(3)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <summary>
        /// Deletes the exes left aside by previous updates ("&lt;name&gt;.old_*.exe" next to this one) and an unfinished
        /// download. An old exe still running (its process not closed yet) is left for the next launch.
        /// </summary>
        public static int CleanupOld(string exe = null)
        {
            exe = exe ?? ExePath;
            int removed = 0;
            try
            {
                string dir = Path.GetDirectoryName(exe), stem = Path.GetFileNameWithoutExtension(exe);
                foreach (string f in Directory.GetFiles(dir, stem + OldMarker + "*.exe"))
                    if (TryDelete(f)) removed++;
                string partial = Path.Combine(dir, stem + ".download");
                if (File.Exists(partial) && File.GetLastWriteTimeUtc(partial) < DateTime.UtcNow.AddHours(-1) && TryDelete(partial)) removed++;
            }
            catch (Exception ex) { Log.Warn(ex, "update cleanup"); }
            return removed;
        }

        private static bool TryDelete(string f)
        {
            try { if (File.Exists(f)) { File.Delete(f); return true; } } catch (Exception) { }
            return false;
        }
    }

    /// <summary>An update that could not be installed: a user-facing message and the technical reason (logged).</summary>
    public sealed class UpdateException : Exception
    {
        public string Detail { get; private set; }
        public UpdateException(string message, string detail) : base(message) { Detail = detail; }
    }
}
