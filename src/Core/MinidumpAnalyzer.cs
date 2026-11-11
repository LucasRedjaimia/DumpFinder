using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinCrashDoctor
{
    public static class MinidumpAnalyzer
    {
        private static readonly HashSet<string> GenericWindowsModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ntoskrnl.exe", "ntkrnlmp.exe", "ntkrnlpa.exe", "hal.dll", "win32k.sys", "win32kfull.sys",
            "win32kbase.sys", "fltmgr.sys", "ntfs.sys", "wdf01000.sys", "wdfldr.sys", "acpi.sys",
            "pci.sys", "clfs.sys", "tm.sys", "ksecdd.sys", "cng.sys", "clipsp.sys", "msrpc.sys",
            "ndis.sys", "netio.sys", "tcpip.sys", "fwpkclnt.sys", "volmgr.sys", "volsnap.sys",
            "partmgr.sys", "classpnp.sys", "mountmgr.sys", "dxgkrnl.sys", "dxgmms1.sys", "dxgmms2.sys",
            "watchdog.sys", "basicdisplay.sys", "basicrender.sys", "storport.sys", "crashdmp.sys",
            "dump_storport.sys", "dump_stornvme.sys", "dump_dumpfve.sys", "ehstorclass.sys",
            "ntdll.dll", "kernel32.dll", "kernelbase.dll", "user32.dll", "gdi32.dll", "msvcrt.dll",
            "ucrtbase.dll", "rpcrt4.dll", "combase.dll", "sechost.dll", "advapi32.dll"
        };

        public static List<MinidumpReportItem> ScanAllAccessibleDumps(out bool minidumpNeedsAdmin)
        {
            minidumpNeedsAdmin = false;
            List<MinidumpReportItem> results = new List<MinidumpReportItem>();

            // 1. C:\Windows\Minidump
            string winMinidump = @"C:\Windows\Minidump";
            if (Directory.Exists(winMinidump))
            {
                try
                {
                    string[] files = Directory.GetFiles(winMinidump, "*.dmp");
                    foreach (string f in files.OrderByDescending(p => File.GetLastWriteTime(p)).Take(15))
                    {
                        var item = AnalyzeDumpFile(f);
                        if (item != null) results.Add(item);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    minidumpNeedsAdmin = true;
                    // Copies made by an earlier admin scan stay readable.
                    results.AddRange(ScanLocalKernelCopies());
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            // 2. %LOCALAPPDATA%\CrashDumps (User-mode application crashes)
            string localDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            if (Directory.Exists(localDumps))
            {
                try
                {
                    foreach (string f in Directory.GetFiles(localDumps, "*.dmp").OrderByDescending(p => File.GetLastWriteTime(p)).Take(10))
                    {
                        var item = AnalyzeDumpFile(f);
                        if (item != null) results.Add(item);
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            // 3. Chrome & Edge Crashpad dumps
            string[] browserDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Crashpad\reports"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Crashpad\reports")
            };
            foreach (string bDir in browserDirs)
            {
                if (!Directory.Exists(bDir)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(bDir, "*.dmp").OrderByDescending(p => File.GetLastWriteTime(p)).Take(8))
                    {
                        var item = AnalyzeDumpFile(f);
                        if (item != null) results.Add(item);
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            return results.OrderByDescending(r => r.CrashTime).ToList();
        }

        public const string KernelDumpFolder = @"C:\Windows\Minidump";

        /// <summary>%LOCALAPPDATA%\DumpFinder\dumps — readable copies of protected kernel dumps.</summary>
        public static string LocalCopyDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder", "dumps"); }
        }

        /// <summary>Runs in the elevated scan: copies kernel dumps next to the user's data (newest 20).</summary>
        public static void CopyKernelDumpsLocally(List<MinidumpReportItem> dumps)
        {
            if (dumps == null) return;
            try
            {
                Directory.CreateDirectory(LocalCopyDir);
                foreach (var d in dumps)
                {
                    try
                    {
                        string dest = Path.Combine(LocalCopyDir, d.FileName);
                        if (!File.Exists(dest) || new FileInfo(dest).Length != d.FileSizeBytes)
                            File.Copy(d.FullPath, dest, true);
                        d.LocalCopyPath = dest;
                    }
                    catch (Exception ex) { Log.Warn(ex, d.FileName); }
                }
            }
            catch (Exception ex) { Log.Warn(ex, LocalCopyDir); }
        }

        private static List<MinidumpReportItem> ScanLocalKernelCopies()
        {
            var list = new List<MinidumpReportItem>();
            try
            {
                if (!Directory.Exists(LocalCopyDir)) return list;
                foreach (string f in Directory.GetFiles(LocalCopyDir, "*.dmp").OrderByDescending(p => File.GetLastWriteTime(p)).Take(20))
                {
                    var item = AnalyzeDumpFile(f);
                    if (item == null) continue;
                    item.LocalCopyPath = f;
                    item.FullPath = Path.Combine(KernelDumpFolder, item.FileName);
                    list.Add(item);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        public static List<MinidumpReportItem> ScanKernelMinidumpsOnly()
        {
            List<MinidumpReportItem> list = new List<MinidumpReportItem>();
            string winMinidump = @"C:\Windows\Minidump";
            if (!Directory.Exists(winMinidump)) return list;
            try
            {
                foreach (string f in Directory.GetFiles(winMinidump, "*.dmp").OrderByDescending(p => File.GetLastWriteTime(p)).Take(20))
                {
                    var item = AnalyzeDumpFile(f);
                    if (item != null) list.Add(item);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        public static MinidumpReportItem AnalyzeDumpFile(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < 64) return null;

                int maxRead = (int)Math.Min(fi.Length, 4 * 1024 * 1024); // Read up to 4 MB
                byte[] buf = new byte[maxRead];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int read = 0;
                    while (read < maxRead)
                    {
                        int n = fs.Read(buf, read, maxRead - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }

                string sig8 = Encoding.ASCII.GetString(buf, 0, 8);
                if (sig8 == "PAGEDU64" || sig8 == "PAGEDUMP")
                    return ParseKernelPageDump(fi, buf, sig8 == "PAGEDU64");

                uint sig4 = BitConverter.ToUInt32(buf, 0);
                if (sig4 == 0x504D444D) // 'MDMP'
                    return ParseUserMdmp(fi, buf);

                return null;
            }
            catch (Exception ex) { Log.Warn(ex); return null; }
        }

        private static MinidumpReportItem ParseKernelPageDump(FileInfo fi, byte[] buf, bool is64)
        {
            if (is64)
            {
                try
                {
                    KernelDumpInfo k = KernelDumpParser.Parse(buf);
                    if (k != null && k.Structured)
                        return FromStructured(fi, k);
                }
                catch (Exception ex) { Log.Warn(ex, fi.Name); }
            }
            return ParseKernelPageDumpLegacy(fi, buf, is64);
        }

        /// <summary>Report built from the real driver list + stack of a triage dump.</summary>
        private static MinidumpReportItem FromStructured(FileInfo fi, KernelDumpInfo k)
        {
            string bugHex = string.Format("0x{0:X8}", k.BugCheckCode);
            string name = BugCheckCatalog.NameOf(k.BugCheckCode);
            var bc = BugCheckCatalog.Get(k.BugCheckCode);

            var related = k.StackModules
                .Where(m => !m.Equals(k.Culprit, StringComparison.OrdinalIgnoreCase)
                            && !m.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase)
                            && !m.Equals("hal.dll", StringComparison.OrdinalIgnoreCase))
                .Take(6).ToList();

            // Plain explanation: the stop code first, then what we know about the culprit driver.
            string codeFr = bc != null && bc.ExplanationFr != null ? bc.ExplanationFr : "Arrêt " + name + ".";
            string codeEn = bc != null && bc.ExplanationEn != null ? bc.ExplanationEn : "Stop " + name + ".";
            string modFr, modEn;
            ExplainCulprit(k.Culprit, bugHex, "System", out modFr, out modEn);
            bool genericModuleText = modFr.StartsWith("Module identifié", StringComparison.Ordinal);

            return new MinidumpReportItem
            {
                FileName = fi.Name,
                FullPath = fi.FullName,
                CrashTime = k.CrashTimeUtc.HasValue ? k.CrashTimeUtc.Value.ToLocalTime() : fi.LastWriteTime,
                DumpType = "Kernel BSOD (Minidump)",
                BugCheckOrException = bugHex + " " + name,
                BugCheckCode = k.BugCheckCode,
                BugCheckName = name,
                BugCheckParameters = k.Parameters.Select(p => "0x" + p.ToString("X")).ToList(),
                OsBuild = k.OsBuild,
                PrimaryCulpritModule = k.Culprit,
                CulpritEvidenceFr = k.EvidenceFr,
                CulpritEvidenceEn = k.EvidenceEn,
                StackModules = k.StackModules,
                LoadedDriverCount = k.Modules.Count,
                StructuredParse = true,
                ProcessName = "System",
                RelatedModules = related.Count > 0 ? string.Join(", ", related) : k.Culprit,
                HumanExplanationFr = genericModuleText ? codeFr : codeFr + " " + modFr,
                HumanExplanationEn = genericModuleText ? codeEn : codeEn + " " + modEn,
                FileSizeBytes = fi.Length
            };
        }

        private static MinidumpReportItem ParseKernelPageDumpLegacy(FileInfo fi, byte[] buf, bool is64)
        {
            uint bugCheck = BitConverter.ToUInt32(buf, is64 ? 0x38 : 0x34);
            string bugHex = string.Format("0x{0:X8}", bugCheck);

            // Extract PDB RSDS symbols & .sys / .exe names referenced inside the dump
            List<string> drivers = ExtractReferencedModules(buf);
            string exeName = ExtractReferencedExe(buf);
            if (string.IsNullOrEmpty(exeName)) exeName = "System / Kernel";

            string culprit = PickPrimaryCulpritDriver(drivers, bugCheck);
            string others = string.Join(", ", drivers.Where(d => !d.Equals(culprit, StringComparison.OrdinalIgnoreCase)).Take(6));

            string expFr, expEn;
            ExplainCulprit(culprit, bugHex, exeName, out expFr, out expEn);

            return new MinidumpReportItem
            {
                FileName = fi.Name,
                FullPath = fi.FullName,
                CrashTime = fi.LastWriteTime,
                DumpType = "Kernel BSOD (Minidump)",
                BugCheckOrException = bugHex + " " + BugCheckCatalog.NameOf(bugCheck),
                BugCheckCode = bugCheck,
                BugCheckName = BugCheckCatalog.NameOf(bugCheck),
                PrimaryCulpritModule = culprit,
                ProcessName = exeName,
                RelatedModules = string.IsNullOrEmpty(others) ? culprit : others,
                HumanExplanationFr = expFr,
                HumanExplanationEn = expEn,
                FileSizeBytes = fi.Length
            };
        }

        private static MinidumpReportItem ParseUserMdmp(FileInfo fi, byte[] buf)
        {
            uint streamCount = BitConverter.ToUInt32(buf, 8);
            uint dirRva = BitConverter.ToUInt32(buf, 12);
            uint timestamp = BitConverter.ToUInt32(buf, 20);

            DateTime crashTime = fi.LastWriteTime;
            if (timestamp > 1000000000 && timestamp < 2100000000)
                crashTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp).ToLocalTime();

            uint exCode = 0;
            ulong exAddr = 0;
            string hostExe = "";
            string faultingMod = "";
            List<string> loadedMods = new List<string>();

            if (dirRva > 0 && dirRva + streamCount * 12 <= buf.Length && streamCount < 128)
            {
                // First pass: find ExceptionStream (type 6)
                for (int i = 0; i < streamCount; i++)
                {
                    int entryOff = (int)dirRva + i * 12;
                    uint sType = BitConverter.ToUInt32(buf, entryOff);
                    uint sSize = BitConverter.ToUInt32(buf, entryOff + 4);
                    uint sRva  = BitConverter.ToUInt32(buf, entryOff + 8);

                    if (sType == 6 && sRva + 32 <= buf.Length)
                    {
                        exCode = BitConverter.ToUInt32(buf, (int)sRva + 8);
                        exAddr = BitConverter.ToUInt64(buf, (int)sRva + 24);
                    }
                }

                // Second pass: ModuleListStream (type 4)
                for (int i = 0; i < streamCount; i++)
                {
                    int entryOff = (int)dirRva + i * 12;
                    uint sType = BitConverter.ToUInt32(buf, entryOff);
                    uint sRva  = BitConverter.ToUInt32(buf, entryOff + 8);

                    if (sType == 4 && sRva + 4 <= buf.Length)
                    {
                        uint modCount = BitConverter.ToUInt32(buf, (int)sRva);
                        int pos = (int)sRva + 4;
                        for (int m = 0; m < modCount && m < 512 && pos + 108 <= buf.Length; m++)
                        {
                            ulong baseAddr = BitConverter.ToUInt64(buf, pos);
                            uint imgSize   = BitConverter.ToUInt32(buf, pos + 8);
                            uint nameRva   = BitConverter.ToUInt32(buf, pos + 20);

                            string modPath = ReadMdmpString(buf, nameRva);
                            if (!string.IsNullOrEmpty(modPath))
                            {
                                string shortName = Path.GetFileName(modPath);
                                if (m == 0 && string.IsNullOrEmpty(hostExe))
                                    hostExe = shortName;

                                if (exAddr >= baseAddr && exAddr < baseAddr + imgSize && string.IsNullOrEmpty(faultingMod))
                                    faultingMod = shortName;

                                if (!GenericWindowsModules.Contains(shortName) && !loadedMods.Contains(shortName))
                                    loadedMods.Add(shortName);
                            }
                            pos += 108;
                        }
                    }
                }
            }

            // Fallback scan for Crashpad / raw strings if streams were compact
            string asciiSample = Encoding.ASCII.GetString(buf, 0, Math.Min(buf.Length, 512 * 1024));
            bool isGraphite = asciiSample.IndexOf("GraphiteDawnD3D11", StringComparison.OrdinalIgnoreCase) >= 0;

            if (string.IsNullOrEmpty(hostExe))
            {
                if (fi.FullName.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0) hostExe = "chrome.exe";
                else if (fi.FullName.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0) hostExe = "msedge.exe";
                else hostExe = ExtractReferencedExe(buf) ?? fi.Name;
            }

            if (string.IsNullOrEmpty(faultingMod))
            {
                if (isGraphite) faultingMod = "GraphiteDawnD3D11 (Skia GPU)";
                else if (loadedMods.Count > 0) faultingMod = loadedMods[0];
                else faultingMod = hostExe;
            }

            string codeStr = exCode != 0 ? string.Format("0x{0:X8}", exCode) : (isGraphite ? "GPU Hang / Watchdog" : "Crash Dump");
            string expFr, expEn;
            ExplainCulprit(faultingMod, codeStr, hostExe, out expFr, out expEn);

            return new MinidumpReportItem
            {
                FileName = fi.Name,
                FullPath = fi.FullName,
                CrashTime = crashTime,
                DumpType = "Application / GPU (MDMP)",
                BugCheckOrException = codeStr,
                PrimaryCulpritModule = faultingMod,
                ProcessName = hostExe,
                RelatedModules = loadedMods.Count > 0 ? string.Join(", ", loadedMods.Take(5)) : faultingMod,
                HumanExplanationFr = expFr,
                HumanExplanationEn = expEn,
                FileSizeBytes = fi.Length
            };
        }

        private static string ReadMdmpString(byte[] buf, uint rva)
        {
            if (rva == 0 || rva + 4 >= buf.Length) return "";
            uint byteLen = BitConverter.ToUInt32(buf, (int)rva);
            if (byteLen == 0 || byteLen > 1024 || rva + 4 + byteLen > buf.Length) return "";
            return Encoding.Unicode.GetString(buf, (int)rva + 4, (int)byteLen);
        }

        private static List<string> ExtractReferencedModules(byte[] buf)
        {
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> ordered = new List<string>();

            // 1. Scan RSDS PDB headers ('R','S','D','S' + 20 bytes + ASCII name.pdb)
            for (int i = 0; i < buf.Length - 32; i++)
            {
                if (buf[i] == (byte)'R' && buf[i + 1] == (byte)'S' && buf[i + 2] == (byte)'D' && buf[i + 3] == (byte)'S')
                {
                    int start = i + 24;
                    int end = start;
                    while (end < buf.Length && end - start < 120 && buf[end] >= 32 && buf[end] < 127)
                        end++;
                    if (end > start + 4)
                    {
                        string pdb = Encoding.ASCII.GetString(buf, start, end - start);
                        if (pdb.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                        {
                            string baseName = Path.GetFileNameWithoutExtension(pdb);
                            if (!string.IsNullOrEmpty(baseName) && baseName.Length >= 3)
                            {
                                string mod = (baseName.StartsWith("ntkrnl", StringComparison.OrdinalIgnoreCase) ? "ntoskrnl.exe" : (baseName + ".sys"));
                                if (found.Add(mod)) ordered.Add(mod);
                            }
                        }
                    }
                }
            }

            // 2. Scan UTF-16LE *.sys strings
            string uni = Encoding.Unicode.GetString(buf, 0, Math.Min(buf.Length, 1024 * 1024));
            foreach (Match m in Regex.Matches(uni, @"([a-zA-Z0-9_\-]{3,32}\.sys)", RegexOptions.IgnoreCase))
            {
                string s = m.Groups[1].Value;
                if (found.Add(s)) ordered.Add(s);
            }

            // 3. Scan ASCII *.sys strings (e.g. MmUnloadedDrivers / ImageFileName tables)
            string asc = Encoding.ASCII.GetString(buf, 0, Math.Min(buf.Length, 1024 * 1024));
            foreach (Match m in Regex.Matches(asc, @"([a-zA-Z0-9_\-]{3,32}\.sys)", RegexOptions.IgnoreCase))
            {
                string s = m.Groups[1].Value;
                if (found.Add(s)) ordered.Add(s);
            }

            return ordered;
        }

        private static string ExtractReferencedExe(byte[] buf)
        {
            string ascii = Encoding.ASCII.GetString(buf, 0, Math.Min(buf.Length, 512 * 1024));
            foreach (Match m in Regex.Matches(ascii, @"([a-zA-Z0-9_\-]{3,28}\.exe)", RegexOptions.IgnoreCase))
            {
                string exe = m.Groups[1].Value;
                if (!exe.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase) &&
                    !exe.Equals("ntkrnlmp.exe", StringComparison.OrdinalIgnoreCase))
                    return exe;
            }
            return "System";
        }

        private static string PickPrimaryCulpritDriver(List<string> mods, uint bugCheck)
        {
            // Priority 1: Non-generic 3rd-party drivers (GPU, Anti-cheat, RGB, Audio, Network)
            foreach (string m in mods)
            {
                if (!GenericWindowsModules.Contains(m) &&
                    !m.Equals("dxgkrnl.sys", StringComparison.OrdinalIgnoreCase) &&
                    !m.Equals("dxgmms2.sys", StringComparison.OrdinalIgnoreCase) &&
                    !m.Equals("watchdog.sys", StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }

            // Priority 2: Specific Windows subsystem driver
            foreach (string m in mods)
            {
                if (m.Equals("dxgkrnl.sys", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("dxgmms2.sys", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("stornvme.sys", StringComparison.OrdinalIgnoreCase))
                    return m;
            }

            if (bugCheck == 0x116 || bugCheck == 0x119) return "nvlddmkm.sys / amdkmdag.sys (GPU TDR)";
            return mods.FirstOrDefault() ?? "ntoskrnl.exe";
        }

        private static void ExplainCulprit(string culprit, string code, string proc, out string fr, out string en)
        {
            string c = (culprit ?? "").ToLowerInvariant();
            if (c.Contains("nvlddmkm") || c.Contains("nvwgf2") || c.Contains("nvoglv"))
            {
                fr = "Pilote graphique NVIDIA (nvlddmkm.sys). Causes fréquentes : délai TDR trop court, Multiplane Overlay (MPO), Skia Graphite dans Chrome, ou undervolt GPU trop agressif (vers 750–850 mV).";
                en = "NVIDIA graphics driver (nvlddmkm.sys). Common causes: TDR timeout too short, Windows MPO, Chrome Skia Graphite, or an overly aggressive GPU undervolt (around 750–850 mV).";
            }
            else if (c.Contains("amdkmdag") || c.Contains("amdwddmg") || c.Contains("amdxc") || c.Contains("atikmpag"))
            {
                fr = "Pilote graphique AMD Radeon. Causes fréquentes : veille profonde ULPS, pilote remplacé en silence par Windows Update, ou Multiplane Overlay (MPO).";
                en = "AMD Radeon graphics driver. Common causes: ULPS deep sleep, Windows Update silently replacing Adrenalin, or Windows MPO.";
            }
            else if (c.Contains("igdkmd") || c.Contains("igd10") || c.Contains("igd12"))
            {
                fr = "Pilote graphique Intel Arc / Iris Xe (" + culprit + "). Causes fréquentes : économie d'énergie PCIe ASPM L1 en sortie de veille, délai TDR trop court, ou Resizable BAR désactivé.";
                en = "Intel Arc / Iris Xe graphics driver (" + culprit + "). Common causes: PCIe ASPM L1 power saving on wake, TDR timeout too short, or Resizable BAR disabled.";
            }
            else if (c.Contains("rt25cx") || c.Contains("rtcx21") || c.Contains("rt640") || c.Contains("rtwlane") || c.Contains("mtkwl") || c.Contains("mtkbt"))
            {
                fr = "Pilote réseau / Wi-Fi / Bluetooth (" + culprit + "). Causes fréquentes : économie d'énergie EEE / Green Ethernet (Realtek 2.5GbE) ou veille PCIe / Démarrage rapide (MediaTek RZ616) ; le correctif Réseau Anti-Freeze couvre ces deux pistes.";
                en = "Network / Wi-Fi / Bluetooth driver (" + culprit + "). Common causes: EEE / Green Ethernet power saving (Realtek 2.5GbE) or PCIe sleep / Fast Startup (MediaTek RZ616); the Network Anti-Freeze fix covers both.";
            }
            else if (c.Contains("graphite") || c.Contains("chrome_elf") || c.Contains("libgles"))
            {
                fr = "Moteur GPU de Chromium (GraphiteDawnD3D11). Souvent lié à un blocage de Direct3D 11 pendant le décodage vidéo matériel : désactiver Skia Graphite et MPO dans Correctifs aide en général.";
                en = "Chromium GPU backend (GraphiteDawnD3D11). Often tied to a Direct3D 11 hang during hardware video decoding: turning off Skia Graphite and MPO in Fixes usually helps.";
            }
            else if (c.Contains("stornvme") || c.Contains("nvme"))
            {
                fr = "Pilote SSD NVMe de Microsoft (stornvme.sys). Causes fréquentes : délai dépassé côté contrôleur du SSD, ou allocation HMB sur les SSD sans DRAM sous Windows 11 ; le correctif NVMe HMB couvre cette seconde piste.";
                en = "Microsoft NVMe SSD driver (stornvme.sys). Common causes: an SSD controller timeout, or HMB allocation on DRAM-less SSDs under Windows 11; the NVMe HMB fix covers the latter.";
            }
            else if (c.Contains("vgk") || c.Contains("easyanticheat") || c.Contains("bedaisy") || c.Contains("randgrid"))
            {
                fr = "Pilote Anti-Cheat Ring-0 (" + culprit + ") : conflit fréquent sous Win11 24H2 avec la Protection de la pile en mode noyau (Shadow Stacks), Auto HDR, un BIOS ancien ou un driver RGB.";
                en = "Ring-0 Anti-Cheat driver (" + culprit + "): frequent conflict on Win11 24H2 with Kernel-mode Hardware-enforced Stack Protection, Auto HDR, outdated BIOS, or legacy RGB drivers.";
            }
            else if (c.Contains("ene.sys") || c.Contains("winring0") || c.Contains("inpout") || c.Contains("gdrv"))
            {
                fr = "Pilote matériel bas niveau / RGB (" + culprit + "). Ce type de pilote est une cause connue de corruptions de la mémoire du noyau et d'écrans bleus aléatoires.";
                en = "Low-level RGB / hardware monitoring driver (" + culprit + "). This kind of driver is a known cause of kernel memory corruption and random blue screens.";
            }
            else
            {
                fr = "Module identifié : " + culprit + " (Processus : " + proc + ", Code : " + code + ").";
                en = "Identified module: " + culprit + " (Process: " + proc + ", Code: " + code + ").";
            }
        }
    }
}
