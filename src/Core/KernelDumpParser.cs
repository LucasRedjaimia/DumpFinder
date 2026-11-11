using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>A driver from the dump's loaded-module list.</summary>
    public class DumpModule
    {
        public string Name;
        public ulong Base;
        public uint Size;
        public uint TimeDateStamp;

        public bool Contains(ulong addr) { return addr >= Base && addr < Base + Size; }
    }

    public class KernelDumpInfo
    {
        public uint BugCheckCode;
        public ulong[] Parameters = new ulong[4];
        public int OsBuild;
        public uint DumpType;
        public DateTime? CrashTimeUtc;
        public List<DumpModule> Modules = new List<DumpModule>();
        public List<string> StackModules = new List<string>();
        public string Culprit;
        public string EvidenceFr;
        public string EvidenceEn;
        public bool Structured;
    }

    /// <summary>
    /// Parses 64-bit Windows kernel crash dumps ("PAGEDU64").
    /// Small/triage dumps (C:\Windows\Minidump, DumpType 4) carry a TRIAGE_DUMP64 block at 0x2000 with the
    /// loaded driver list (DUMP_DRIVER_ENTRY64 + string pool) and the raw stack of the crashing thread.
    /// The culprit is derived from (1) the stop-code parameter that holds a code address,
    /// (2) the first non-Microsoft driver found on the raw stack — no symbols needed.
    /// Layout validated against Windows 11 24H2 (build 26100) dumps.
    /// </summary>
    public static class KernelDumpParser
    {
        private const int HeaderSize64 = 0x2000;
        private const int OffBugCheck = 0x38;
        private const int OffParams = 0x40;
        private const int OffMinor = 0x0C;
        private const int OffDumpType = 0xF98;
        private const int OffSystemTime = 0xFA8;
        private const uint DumpTypeTriage = 4;

        // DUMP_DRIVER_ENTRY64: ULONG DriverNameOffset; (pad) ; KLDR_DATA_TABLE_ENTRY LdrEntry (starts at +8)
        private const int DrvDllBase = 0x08 + 0x30;
        private const int DrvSizeOfImage = 0x08 + 0x40;
        private const int DrvTimeDateStamp = 0x88;
        private const int DefaultDriverStride = 0x90;

        /// <summary>Core kernel images that host the crash mechanics rather than cause it.</summary>
        private static readonly HashSet<string> CoreImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ntoskrnl.exe", "ntkrnlmp.exe", "hal.dll", "kdcom.dll", "mcupdate_authenticamd.dll", "mcupdate_genuineintel.dll"
        };

        public static KernelDumpInfo Parse(byte[] buf)
        {
            if (buf == null || buf.Length < HeaderSize64 + 0x80) return null;
            if (Encoding.ASCII.GetString(buf, 0, 8) != "PAGEDU64") return null;

            var info = new KernelDumpInfo
            {
                BugCheckCode = U32(buf, OffBugCheck),
                OsBuild = (int)U32(buf, OffMinor),
                DumpType = U32(buf, OffDumpType)
            };
            for (int i = 0; i < 4; i++) info.Parameters[i] = U64(buf, OffParams + 8 * i);

            long ft = (long)U64(buf, OffSystemTime);
            if (ft > 0)
            {
                try { info.CrashTimeUtc = DateTime.FromFileTimeUtc(ft); }
                catch (ArgumentOutOfRangeException) { }
            }

            if (info.DumpType == DumpTypeTriage)
                ParseTriage(buf, info);

            PickCulprit(info);
            return info;
        }

        private static void ParseTriage(byte[] buf, KernelDumpInfo info)
        {
            int t = HeaderSize64;
            uint callStackOff = U32(buf, t + 0x28);
            uint callStackSize = U32(buf, t + 0x2C);
            uint driverListOff = U32(buf, t + 0x30);
            uint driverCount = U32(buf, t + 0x34);
            uint stringPoolOff = U32(buf, t + 0x38);
            uint stringPoolSize = U32(buf, t + 0x3C);

            if (driverCount == 0 || driverCount > 4096 || driverListOff == 0 || driverListOff >= buf.Length) return;

            int stride = DefaultDriverStride;
            if (stringPoolOff > driverListOff)
            {
                long s = (stringPoolOff - driverListOff) / driverCount;
                if (s >= 0x80 && s <= 0x100 && (stringPoolOff - driverListOff) % driverCount == 0) stride = (int)s;
            }

            long poolEnd = Math.Min(buf.Length, (long)stringPoolOff + stringPoolSize);
            for (int i = 0; i < driverCount; i++)
            {
                long e = driverListOff + (long)i * stride;
                if (e + stride > buf.Length) break;
                uint nameOff = U32(buf, (int)e);
                string name = ReadDumpString(buf, nameOff, stringPoolOff, poolEnd);
                ulong dllBase = U64(buf, (int)e + DrvDllBase);
                uint size = U32(buf, (int)e + DrvSizeOfImage);
                if (string.IsNullOrEmpty(name) || (dllBase >> 48) != 0xFFFF || size == 0 || size > 0x40000000) continue;
                info.Modules.Add(new DumpModule { Name = name, Base = dllBase, Size = size, TimeDateStamp = U32(buf, (int)e + DrvTimeDateStamp) });
            }
            info.Structured = info.Modules.Count > 0;

            // Raw stack of the crashing thread: collect driver images that appear as return addresses.
            if (callStackOff > 0 && callStackSize > 0 && callStackOff + callStackSize <= buf.Length && info.Modules.Count > 0)
            {
                for (long p = callStackOff; p + 8 <= callStackOff + callStackSize; p += 8)
                {
                    ulong v = U64(buf, (int)p);
                    if ((v >> 48) != 0xFFFF) continue;
                    var m = FindModule(info.Modules, v);
                    if (m != null && !info.StackModules.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
                        info.StackModules.Add(m.Name);
                }
            }
        }

        private static void PickCulprit(KernelDumpInfo info)
        {
            var bc = BugCheckCatalog.Get(info.BugCheckCode);

            // 1. The stop code tells us which parameter is a code address.
            if (bc != null && bc.AddressParam >= 1 && bc.AddressParam <= 4 && info.Modules.Count > 0)
            {
                ulong addr = info.Parameters[bc.AddressParam - 1];
                var m = FindModule(info.Modules, addr);
                if (m != null && !CoreImages.Contains(m.Name))
                {
                    info.Culprit = m.Name;
                    info.EvidenceFr = "Le paramètre " + bc.AddressParam + " de l'erreur (0x" + addr.ToString("X") + ") pointe à l'intérieur de " + m.Name + ".";
                    info.EvidenceEn = "Stop parameter " + bc.AddressParam + " (0x" + addr.ToString("X") + ") points inside " + m.Name + ".";
                    return;
                }
            }

            // 2. First third-party driver on the raw stack.
            string thirdParty = info.StackModules.FirstOrDefault(n => !CoreImages.Contains(n) && !IsMicrosoftDriver(n));
            if (thirdParty != null)
            {
                info.Culprit = thirdParty;
                info.EvidenceFr = "Premier pilote non-Microsoft présent sur la pile d'appels au moment du crash.";
                info.EvidenceEn = "First non-Microsoft driver on the call stack at crash time.";
                return;
            }

            // 3. First non-core image on the stack (a Microsoft driver such as dxgkrnl or stornvme).
            string firstDriver = info.StackModules.FirstOrDefault(n => !CoreImages.Contains(n));
            if (firstDriver != null)
            {
                info.Culprit = firstDriver;
                info.EvidenceFr = "Pilote Windows le plus haut sur la pile d'appels ; aucun pilote tiers n'y apparaît.";
                info.EvidenceEn = "Highest Windows driver on the call stack; no third-party driver appears there.";
                return;
            }

            info.Culprit = info.StackModules.FirstOrDefault() ?? "ntoskrnl.exe";
            info.EvidenceFr = "Aucun pilote identifiable : le noyau lui-même a détecté l'erreur (souvent un problème matériel).";
            info.EvidenceEn = "No identifiable driver: the kernel itself detected the error (often a hardware problem).";
        }

        public static DumpModule FindModule(List<DumpModule> modules, ulong addr)
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i].Contains(addr)) return modules[i];
            return null;
        }

        private static readonly Dictionary<string, bool> MsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Checks the driver file's company name on this PC (cached).</summary>
        public static bool IsMicrosoftDriver(string name)
        {
            lock (MsCache)
            {
                bool r;
                if (MsCache.TryGetValue(name, out r)) return r;
                r = false;
                try
                {
                    string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string[] candidates = { Path.Combine(sys, "drivers", name), Path.Combine(sys, name) };
                    foreach (var c in candidates)
                    {
                        if (!File.Exists(c)) continue;
                        var vi = FileVersionInfo.GetVersionInfo(c);
                        r = (vi.CompanyName ?? "").IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    }
                }
                catch (Exception ex) { Log.Warn(ex, name); }
                MsCache[name] = r;
                return r;
            }
        }

        private static string ReadDumpString(byte[] buf, uint off, uint poolStart, long poolEnd)
        {
            if (off < poolStart || off + 4 > poolEnd) return null;
            uint len = U32(buf, (int)off);
            if (len == 0 || len > 260 || off + 4 + len * 2 > poolEnd) return null;
            return Encoding.Unicode.GetString(buf, (int)off + 4, (int)len * 2);
        }

        private static uint U32(byte[] b, int o) { return o >= 0 && o + 4 <= b.Length ? BitConverter.ToUInt32(b, o) : 0; }
        private static ulong U64(byte[] b, int o) { return o >= 0 && o + 8 <= b.Length ? BitConverter.ToUInt64(b, o) : 0; }
    }
}
