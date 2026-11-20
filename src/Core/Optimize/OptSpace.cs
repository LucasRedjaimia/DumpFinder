using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace WinCrashDoctor.Optimize
{
    /// <summary>A program from the Windows uninstall list, with the space it takes.</summary>
    public sealed class InstalledProgram
    {
        public string Name;
        public string Publisher;
        public string Location;      // install folder, null when unknown
        public long Bytes;
        public bool Estimated;       // size reported by the installer (no readable install folder)
        public DateTime? Installed;
        public string Uninstall;     // UninstallString
    }

    /// <summary>A file or folder of the measured tree. Files are kept only when they are among the largest of their folder.</summary>
    public sealed class SpaceNode
    {
        public string Name;
        public SpaceNode Parent;
        public long Bytes;
        public bool IsFolder;
        public List<SpaceNode> Kids;

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                var parts = new List<string>();
                for (var n = this; n.Parent != null; n = n.Parent) parts.Add(n.Name);
                parts.Reverse();
                string root = this.Root().Name;
                return root.EndsWith("\\", StringComparison.Ordinal) ? root + string.Join("\\", parts) : root + "\\" + string.Join("\\", parts);
            }
        }

        public SpaceNode Root() { var n = this; while (n.Parent != null) n = n.Parent; return n; }
    }

    /// <summary>
    /// One read-only walk of a folder or drive (Optimiser › Storage): every folder summed, the largest files kept.
    /// Junctions and symbolic links are not followed (counted where they point); OneDrive files that only live in
    /// the cloud count for nothing, since they take no space on this PC.
    /// </summary>
    public sealed class SpaceScan
    {
        public SpaceNode Root { get; private set; }
        public List<SpaceNode> LargestFiles { get; private set; }
        public long ScannedBytes { get; private set; }
        public int ScannedFiles { get; private set; }

        /// <summary>Files kept per folder for the drill-down (the rest still counts in the folder's size).</summary>
        private const int FilesKeptPerFolder = 100;
        private const int LargestCount = 60;

        public static SpaceScan Scan(string root, CancellationToken ct, Action<long, int> progress = null, long bigFile = 100L << 20)
        {
            root = Path.GetFullPath(root);
            if (root.Length > 3) root = root.TrimEnd('\\');
            var scan = new SpaceScan { Root = new SpaceNode { Name = root, IsFolder = true, Kids = new List<SpaceNode>() } };
            var dirs = new List<SpaceNode> { scan.Root };
            var pending = new Stack<KeyValuePair<SpaceNode, string>>();
            pending.Push(new KeyValuePair<SpaceNode, string>(scan.Root, root));
            var big = new List<SpaceNode>();

            while (pending.Count > 0 && !ct.IsCancellationRequested)
            {
                var item = pending.Pop();
                SpaceNode dir = item.Key;
                var files = new List<SpaceNode>();
                foreach (var e in Native.List(item.Value))
                {
                    if (ct.IsCancellationRequested) break;
                    string full = Combine(item.Value, e.Name);
                    if (e.IsDirectory)
                    {
                        if (e.IsReparse && !e.IsCloudFolder) continue;     // junctions / links: counted where they point
                        var sub = new SpaceNode { Name = e.Name, Parent = dir, IsFolder = true, Kids = new List<SpaceNode>() };
                        dir.Kids.Add(sub);
                        dirs.Add(sub);
                        pending.Push(new KeyValuePair<SpaceNode, string>(sub, full));
                        continue;
                    }
                    long len = e.CloudOnly ? 0 : e.Size;
                    // Compressed or sparse large files (VM disks, NTFS-compressed folders): the space really used.
                    if (len >= bigFile) len = Math.Min(len, Native.AllocatedBytes(full) ?? len);
                    dir.Bytes += len;
                    scan.ScannedBytes += len;
                    if (++scan.ScannedFiles % 20000 == 0 && progress != null) progress(scan.ScannedBytes, scan.ScannedFiles);
                    var node = new SpaceNode { Name = e.Name, Parent = dir, Bytes = len };
                    files.Add(node);
                    if (len >= bigFile) big.Add(node);
                }
                if (files.Count > FilesKeptPerFolder) files = files.OrderByDescending(f => f.Bytes).Take(FilesKeptPerFolder).ToList();
                dir.Kids.AddRange(files);
            }

            // Children always come after their parent in "dirs": walking it backwards adds every folder to its parent once.
            for (int i = dirs.Count - 1; i > 0; i--) dirs[i].Parent.Bytes += dirs[i].Bytes;
            scan.LargestFiles = big.OrderByDescending(f => f.Bytes).Take(LargestCount).ToList();
            return scan;
        }

        private static string Combine(string dir, string name) { return dir.EndsWith("\\", StringComparison.Ordinal) ? dir + name : dir + "\\" + name; }

        /// <summary>The children of a folder, largest first (the drill-down list).</summary>
        public static List<SpaceNode> ChildrenOf(SpaceNode dir)
        {
            return dir == null || dir.Kids == null ? new List<SpaceNode>() : dir.Kids.OrderByDescending(k => k.Bytes).ToList();
        }

        /// <summary>Finds the node of a path inside this tree (null when it is outside or was not kept).</summary>
        public SpaceNode Find(string path)
        {
            string root = Root.Name.TrimEnd('\\');
            path = path.TrimEnd('\\');
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return Root;
            if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            var n = Root;
            foreach (string part in path.Substring(root.Length + 1).Split('\\'))
            {
                n = n.Kids == null ? null : n.Kids.FirstOrDefault(k => k.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (n == null) return null;
            }
            return n;
        }

        /// <summary>After a move to the Recycle Bin: the item leaves the tree and its size leaves every parent.</summary>
        public void Forget(SpaceNode node)
        {
            if (node == null || node.Parent == null) return;
            node.Parent.Kids.Remove(node);
            for (var p = node.Parent; p != null; p = p.Parent) p.Bytes -= node.Bytes;
            ScannedBytes -= node.Bytes;
            LargestFiles.RemoveAll(f => IsUnder(f, node));
        }

        private static bool IsUnder(SpaceNode n, SpaceNode ancestor)
        {
            for (var p = n; p != null; p = p.Parent) if (p == ancestor) return true;
            return false;
        }

        // ==================== Native enumeration ====================
        internal struct Entry
        {
            public string Name;
            public long Size;
            public bool IsDirectory, IsReparse, CloudOnly, IsCloudFolder;
        }

        internal static class Native
        {
            private const int FindExInfoBasic = 1;
            private const int FindExSearchNameMatch = 0;
            private const int FIND_FIRST_EX_LARGE_FETCH = 2;
            private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
            private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
            private const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
            private const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
            private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
            private const uint IO_REPARSE_TAG_CLOUD_MASK = 0xFFFF0FFF;
            private const uint IO_REPARSE_TAG_CLOUD = 0x9000001A;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WIN32_FIND_DATAW
            {
                public uint dwFileAttributes;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
                public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATAW lpFindFileData, int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr hFindFile);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

            private static readonly IntPtr Invalid = new IntPtr(-1);

            /// <summary>Long-path form for the Win32 calls (paths over 260 characters are common in node_modules).</summary>
            internal static string Long(string path)
            {
                if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
                if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path.Substring(2);
                return @"\\?\" + path;
            }

            /// <summary>The entries of one folder; an unreadable folder gives none.</summary>
            public static IEnumerable<Entry> List(string dir)
            {
                WIN32_FIND_DATAW fd;
                string pattern = Long(dir.EndsWith("\\", StringComparison.Ordinal) ? dir + "*" : dir + "\\*");
                IntPtr h = FindFirstFileExW(pattern, FindExInfoBasic, out fd, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
                if (h == Invalid) yield break;
                try
                {
                    do
                    {
                        if (fd.cFileName == "." || fd.cFileName == "..") continue;
                        uint a = fd.dwFileAttributes;
                        bool reparse = (a & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                        yield return new Entry
                        {
                            Name = fd.cFileName,
                            Size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow,
                            IsDirectory = (a & FILE_ATTRIBUTE_DIRECTORY) != 0,
                            IsReparse = reparse,
                            IsCloudFolder = reparse && (fd.dwReserved0 & IO_REPARSE_TAG_CLOUD_MASK) == IO_REPARSE_TAG_CLOUD,
                            CloudOnly = (a & (FILE_ATTRIBUTE_OFFLINE | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)) != 0
                        };
                    } while (FindNextFileW(h, out fd));
                }
                finally { FindClose(h); }
            }

            /// <summary>Space a file really takes on disk (compressed / sparse), null when unknown.</summary>
            public static long? AllocatedBytes(string path)
            {
                uint high;
                uint low = GetCompressedFileSizeW(Long(path), out high);
                if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return null;
                return ((long)high << 32) | low;
            }
        }
    }

    /// <summary>
    /// Optimiser › Storage: what takes space, as installed programs, a drill-down of a folder or drive and the largest
    /// files, with Show in Explorer, Move to Recycle Bin (journaled: Undo puts it back) and the program's own uninstaller.
    /// </summary>
    public static class OptSpace
    {
        /// <summary>The user profile (overridable by the self-test).</summary>
        public static string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ---------- Places to measure ----------
        public sealed class Place
        {
            public string Root;
            public bool IsProfile;
            public string Label;
        }

        public static List<Place> Places()
        {
            var list = new List<Place> { new Place { Root = Profile, IsProfile = true, Label = Localizer.S("space.my_folder") } };
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        list.Add(new Place { Root = d.RootDirectory.FullName, Label = Localizer.F("space.drive", d.Name.TrimEnd('\\')) });
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        // ---------- Protected items: only "Show in Explorer" ----------
        /// <summary>Profile items kept out of the Recycle Bin themselves (their content can still be managed).</summary>
        private static readonly HashSet<string> ProfileKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "Favorites", "Contacts", "Links", "Saved Games",
            "Searches", "3D Objects", "AppData", "OneDrive", "Application Data", "Local Settings", "My Documents", "Cookies",
            "NetHood", "PrintHood", "Recent", "SendTo", "Start Menu", "Templates", "IntelGraphicsProfiles",
            ".config", ".local", ".docker", ".gitconfig", ".git-credentials", ".netrc", ".bashrc", ".bash_profile", ".profile",
            ".wslconfig", ".npmrc", ".pypirc"
        };

        /// <summary>Profile folders kept with everything inside: keys, credentials, Windows' and Store apps' data, Dump Finder's data.</summary>
        private static readonly string[] ProfileTree =
        {
            ".ssh", ".gnupg", ".aws", ".azure", ".kube",
            @"AppData\Local\Microsoft", @"AppData\Roaming\Microsoft", @"AppData\LocalLow\Microsoft",
            @"AppData\Local\Packages", @"AppData\Local\Programs", @"AppData\Local\DumpFinder", @"AppData\Local\Temp"
        };

        /// <summary>Top-level folders of any drive kept with everything inside (Windows, programs, recovery, the Recycle Bin…).</summary>
        private static readonly HashSet<string> DriveTree = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
            "Recovery", "Boot", "EFI", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "$GetCurrent", "Config.Msi",
            "Documents and Settings", "MSOCache", "OneDriveTemp", "WindowsApps", "WpSystem", "Users"
        };

        /// <summary>System files at the root of a drive.</summary>
        private static readonly HashSet<string> DriveRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT", "BOOTSECT.BAK"
        };

        /// <summary>
        /// The profile itself and its standard folders, keys and app data (<see cref="ProfileKeep"/>, <see cref="ProfileTree"/>),
        /// every drive root, Windows, programs, other accounts' profiles, recovery and system files are never offered for the
        /// Recycle Bin. Programs are removed with their own uninstaller (Programs view).
        /// </summary>
        public static bool IsProtected(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string full;
            try { full = Path.GetFullPath(path); } catch (Exception) { return true; }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;               // network / device paths
            string root = Path.GetPathRoot(full);
            full = full.TrimEnd('\\');
            if (root == null || full.Length <= root.TrimEnd('\\').Length) return true;       // C:\ itself

            string profile = Profile.TrimEnd('\\');
            if (full.Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase))
            {
                string rel = full.Substring(profile.Length + 1);
                string[] parts = rel.Split('\\');
                if (parts.Length == 1)
                    return ProfileKeep.Contains(parts[0]) || parts[0].StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase)
                           || parts[0].StartsWith("ntuser", StringComparison.OrdinalIgnoreCase);
                if (parts[0].Equals("AppData", StringComparison.OrdinalIgnoreCase) && parts.Length == 2) return true;   // Local, Roaming, LocalLow
                foreach (string t in ProfileTree)
                    if (rel.Equals(t, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            // Outside the profile: Windows' own folders, wherever they live, then the per-drive rules.
            foreach (var sf in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            {
                string s = Environment.GetFolderPath(sf);
                if (string.IsNullOrEmpty(s)) continue;
                s = s.TrimEnd('\\');
                if (full.Equals(s, StringComparison.OrdinalIgnoreCase) || full.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            string users = Path.GetDirectoryName(profile);
            if (!string.IsNullOrEmpty(users) && (full.Equals(users, StringComparison.OrdinalIgnoreCase) || full.StartsWith(users + "\\", StringComparison.OrdinalIgnoreCase)))
                return true;                                                                  // other accounts, Public, Default
            string[] top = full.Substring(root.TrimEnd('\\').Length + 1).Split('\\');
            if (DriveTree.Contains(top[0])) return true;
            if (top.Length == 1 && DriveRootFiles.Contains(top[0])) return true;
            return false;
        }

        // ---------- Installed programs ----------
        /// <summary>
        /// The programs Windows lists under Installed apps (uninstall keys, 64- and 32-bit, machine and user), with the
        /// size of their install folder when it can be read, else the size their installer reported. Updates and
        /// hidden system components are left out.
        /// </summary>
        public static List<InstalledProgram> Programs(CancellationToken ct)
        {
            var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<RegistryKey>();
            try
            {
                sources.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64));
                sources.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32));
                sources.Add(RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64));
                foreach (RegistryKey hive in sources)
                {
                    using (RegistryKey un = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (un == null) continue;
                        foreach (string name in un.GetSubKeyNames())
                        {
                            if (ct.IsCancellationRequested) break;
                            try
                            {
                                using (RegistryKey k = un.OpenSubKey(name))
                                {
                                    InstalledProgram p = Read(k);
                                    if (p == null) continue;
                                    string key = p.Name + "\u0001" + (k.GetValue("DisplayVersion") as string);
                                    if (!found.ContainsKey(key)) found[key] = p;
                                }
                            }
                            catch (Exception ex) when (ex is System.Security.SecurityException || ex is IOException || ex is UnauthorizedAccessException) { }
                        }
                    }
                }
            }
            finally { foreach (var h in sources) h.Dispose(); }

            foreach (InstalledProgram p in found.Values)
            {
                if (ct.IsCancellationRequested) break;
                long measured = p.Location != null ? FolderSize(p.Location, ct) : 0;
                if (measured > 0) { p.Bytes = measured; p.Estimated = false; }
            }
            return found.Values.Where(p => p.Bytes > 0).OrderByDescending(p => p.Bytes).ToList();
        }

        private static InstalledProgram Read(RegistryKey k)
        {
            string display = k.GetValue("DisplayName") as string;
            string uninstall = k.GetValue("UninstallString") as string;
            if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(uninstall)) return null;
            if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) return null;
            if (k.GetValue("ParentKeyName") != null) return null;
            string release = k.GetValue("ReleaseType") as string;
            if (release != null && (release.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 || release.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0)) return null;

            var p = new InstalledProgram
            {
                Name = display.Trim(),
                Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                Uninstall = uninstall.Trim(),
                Estimated = true
            };
            object est = k.GetValue("EstimatedSize");
            if (est is int kb && kb > 0) p.Bytes = kb * 1024L;
            string loc = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"');
            if (loc.Length > 0)
            {
                try
                {
                    loc = Path.GetFullPath(Environment.ExpandEnvironmentVariables(loc)).TrimEnd('\\');
                    // A drive root or a shared system folder is not the program's own folder: keep the reported size.
                    if (Directory.Exists(loc) && !IsSharedFolder(loc)) p.Location = loc;
                }
                catch (Exception) { }
            }
            string date = k.GetValue("InstallDate") as string;
            DateTime d;
            if (date != null && DateTime.TryParseExact(date.Trim(), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
                p.Installed = d;
            return p;
        }

        private static bool IsSharedFolder(string loc)
        {
            string root = Path.GetPathRoot(loc) ?? "";
            if (loc.Length <= root.TrimEnd('\\').Length) return true;
            foreach (var sf in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                                       Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
                                       Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                                       Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
            {
                string s = Environment.GetFolderPath(sf).TrimEnd('\\');
                if (s.Length > 0 && loc.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            return windows.Length > 0 && loc.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static long FolderSize(string path, CancellationToken ct = default(CancellationToken))
        {
            try { return SpaceScan.Scan(path, ct).Root.Bytes; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return 0; }
        }

        // ---------- Actions ----------
        /// <summary>Opens Explorer with the item selected (the folder itself when it no longer exists).</summary>
        public static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), Arguments = "/select,\"" + path + "\"", UseShellExecute = false });
            }
            catch (Exception ex) { Log.Warn(ex, path); }
        }

        /// <summary>Splits an UninstallString into the program and its arguments ("C:\x\unins000.exe" /SILENT, MsiExec.exe /X{…}).</summary>
        public static bool SplitCommand(string command, out string exe, out string args)
        {
            exe = null; args = "";
            if (string.IsNullOrWhiteSpace(command)) return false;
            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = command.IndexOf('"', 1);
                if (end < 0) return false;
                exe = command.Substring(1, end - 1);
                args = command.Substring(end + 1).Trim();
            }
            else
            {
                int at = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (at < 0) { exe = command; }
                else
                {
                    exe = command.Substring(0, at + 4);
                    args = command.Substring(at + 4).Trim();
                }
            }
            exe = Environment.ExpandEnvironmentVariables(exe);
            return exe.Length > 0;
        }

        /// <summary>Starts the program's own uninstaller (it asks for administrator rights itself when it needs them).</summary>
        public static bool LaunchUninstaller(InstalledProgram p, out string error)
        {
            error = null;
            string exe, args;
            if (!SplitCommand(p.Uninstall, out exe, out args)) { error = p.Uninstall; return false; }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = true });
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { error = Localizer.S("space.cancelled"); return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public enum RecycleResult { Recycled, DeletedPermanently, Cancelled, Failed, Protected, Gone }

        /// <summary>
        /// Moves a file or folder to the Recycle Bin and journals it (Undo in Tools › History puts it back). When
        /// Windows cannot keep it (too large for the bin, a drive without one), Windows itself warns before deleting.
        /// </summary>
        public static RecycleResult MoveToRecycleBin(string path, string title, long bytes, IntPtr owner, out string message)
        {
            message = null;
            if (IsProtected(path)) { message = Localizer.S("space.protected"); return RecycleResult.Protected; }
            if (!File.Exists(path) && !Directory.Exists(path)) { message = Localizer.S("space.gone"); return RecycleResult.Gone; }
            DateTime before = DateTime.UtcNow.AddSeconds(-2);

            bool aborted;
            int rc = ShellRecycle(path, owner, out aborted);
            bool gone = !File.Exists(path) && !Directory.Exists(path);
            if (!gone)
            {
                message = aborted ? Localizer.S("space.cancelled") : Localizer.F("space.not_moved", Localizer.S("space.in_use_or_admin")) + (rc != 0 ? " (0x" + rc.ToString("X") + ")" : "");
                return aborted ? RecycleResult.Cancelled : RecycleResult.Failed;
            }

            string info = FindRecycled(path, before);
            var entry = new JournalEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Time = DateTime.Now,
                Op = "space_recycle",
                FixId = "space_recycle",
                Title = Localizer.S("space.recycled_title") + title,
                Ok = true,
                Reversible = info != null,
                Changes = info != null ? new List<ChangeRecord> { new ChangeRecord { Kind = "recycle", FilePath = path, BackupPath = info } } : new List<ChangeRecord>()
            };
            message = info != null ? Localizer.F("space.recycled", title, Human(bytes)) : Localizer.F("space.deleted_permanently", title, Human(bytes));
            entry.Message = message;
            History.AddJournal(new[] { entry });
            return info != null ? RecycleResult.Recycled : RecycleResult.DeletedPermanently;
        }

        // ---------- Recycle Bin ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

        private const uint FO_DELETE = 3;
        private const ushort FOF_NOCONFIRMATION = 0x10;
        private const ushort FOF_ALLOWUNDO = 0x40;
        private const ushort FOF_NOERRORUI = 0x400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>
        /// The shell's own delete with undo (the Recycle Bin). FOF_WANTNUKEWARNING keeps Windows' warning when an item
        /// would be destroyed instead of recycled, so nothing is ever deleted for good without the user saying so.
        /// Runs on an STA thread, as the shell expects.
        /// </summary>
        private static int ShellRecycle(string path, IntPtr owner, out bool aborted)
        {
            int rc = -1; bool ab = false;
            var t = new Thread(() =>
            {
                var op = new SHFILEOPSTRUCTW
                {
                    hwnd = owner,
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0",
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NOERRORUI
                };
                try { rc = SHFileOperationW(ref op); ab = op.fAnyOperationsAborted != 0; }
                catch (Exception ex) { Log.Warn(ex, path); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            aborted = ab;
            return rc;
        }

        /// <summary>This account's Recycle Bin folder on the drive of <paramref name="path"/>.</summary>
        public static string BinFolder(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                string sid = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(root) || sid == null) return null;
                return Path.Combine(root, "$Recycle.Bin", sid);
            }
            catch (Exception) { return null; }
        }

        /// <summary>The $I file Windows wrote for <paramref name="original"/> after <paramref name="since"/> (null when none).</summary>
        private static string FindRecycled(string original, DateTime since)
        {
            string bin = BinFolder(original);
            if (bin == null || !Directory.Exists(bin)) return null;
            try
            {
                return new DirectoryInfo(bin).GetFiles("$I*")
                    .Where(f => f.LastWriteTimeUtc >= since)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault(f => string.Equals(ReadInfo(f), original, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Log.Warn(ex, bin); return null; }
        }

        /// <summary>The original path stored in a Recycle Bin $I file (Windows Vista+ formats 1 and 2), null when unreadable.</summary>
        public static string ReadInfo(string infoFile)
        {
            try
            {
                byte[] b = File.ReadAllBytes(infoFile);
                if (b.Length < 24) return null;
                long version = BitConverter.ToInt64(b, 0);
                string s;
                if (version == 2 && b.Length >= 28)
                {
                    int chars = BitConverter.ToInt32(b, 24);
                    if (chars <= 0 || 28 + chars * 2 > b.Length) return null;
                    s = Encoding.Unicode.GetString(b, 28, chars * 2);
                }
                else if (version == 1 && b.Length >= 24 + 520) s = Encoding.Unicode.GetString(b, 24, 520);
                else return null;
                int nul = s.IndexOf('\0');
                return nul >= 0 ? s.Substring(0, nul) : s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Undo of a "recycle" record: puts the item back where it was. The record comes from a user-writable journal,
        /// so it is checked again: the $I file must be in this account's Recycle Bin on the item's drive, name that very
        /// path, and the place must be free. Null on success, else the reason.
        /// </summary>
        public static string RestoreRecycled(ChangeRecord c)
        {
            if (c == null || string.IsNullOrEmpty(c.FilePath) || string.IsNullOrEmpty(c.BackupPath)) return "incomplete record";
            string target, info;
            try { target = Path.GetFullPath(c.FilePath); info = Path.GetFullPath(c.BackupPath); }
            catch (Exception) { return "invalid path"; }
            string bin = BinFolder(target);
            if (bin == null || !string.Equals(Path.GetDirectoryName(info), bin, StringComparison.OrdinalIgnoreCase)) return "not in this account's Recycle Bin";
            string infoName = Path.GetFileName(info);
            if (!infoName.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) return "not a Recycle Bin entry";
            if (!File.Exists(info)) return Localizer.S("space.no_longer_in_bin");
            if (!string.Equals(ReadInfo(info), target, StringComparison.OrdinalIgnoreCase)) return "Recycle Bin entry does not match";
            string data = Path.Combine(bin, "$R" + infoName.Substring(2));
            bool isDir = Directory.Exists(data);
            if (!isDir && !File.Exists(data)) return Localizer.S("space.no_longer_in_bin");
            if (File.Exists(target) || Directory.Exists(target)) return Localizer.S("space.restore_exists");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (isDir) Directory.Move(data, target); else File.Move(data, target);
            try { File.Delete(info); } catch (Exception ex) { Log.Warn(ex, info); }
            return null;
        }

        public static string Human(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / 1073741824.0).ToString("0.0", Localizer.Culture) + " " + Localizer.S("stress_text.gb");
            if (bytes >= 1L << 20) return (bytes / 1048576.0).ToString("0", Localizer.Culture) + " " + Localizer.S("stress_text.mb");
            return Math.Max(1, bytes / 1024) + " KB";
        }
    }
}
