using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    /// <summary>One point of an NVIDIA voltage/frequency curve: the frequency the driver gives it plus the user's offset.</summary>
    public sealed class VfPoint
    {
        public double MilliVolts;
        public double BaseMHz;
        public double OffsetMHz;
        public double MHz { get { return BaseMHz + OffsetMHz; } }
    }

    /// <summary>A curve saved in MSI Afterburner (Startup, Profile 1–5) or the one applied right now.</summary>
    public sealed class VfProfile
    {
        public string Id;              // "Startup", "Profile1"…"Profile5", "Live"
        public List<VfPoint> Points;
        public int CoreBoostMHz;
        public int MemBoostMHz;
        public bool Modified { get { return Points != null && Points.Any(p => Math.Abs(p.OffsetMHz) >= 1); } }
    }

    public enum CurveTipKind { Danger, Warning, Info }

    public sealed class CurveTip
    {
        public CurveTipKind Kind;
        public string Text;
    }

    /// <summary>What the analysis says about one curve: where it flattens, a safer version of it, and tips.</summary>
    public sealed class CurveAdvice
    {
        public double TargetMv;            // where the plateau starts
        public double PlateauMHz;          // the plateau as saved
        public double RunsAtMv;            // where the GPU really ends up (the first point at the curve's top clock)
        public double RunsAtMHz;
        public double SuggestedMHz;        // the plateau of the suggested curve
        public List<double> Suggested;     // same length as the curve; null when nothing to suggest (stock curve)
        public List<KeyValuePair<double, double>> KeyPoints = new List<KeyValuePair<double, double>>();   // (mV, MHz) to set
        public List<CurveTip> Tips = new List<CurveTip>();
    }

    /// <summary>Everything Dump Finder can read about the GPU curve: saved profiles, stock curve, the live one.</summary>
    public sealed class GpuCurveInfo
    {
        public string GpuId;               // VEN_10DE&DEV_…&BUS_…, the Afterburner profile name
        public string ProfileFile;
        public List<VfProfile> Profiles = new List<VfProfile>();
        public VfProfile Stock;            // base frequencies, no offset
        public VfProfile Live;             // null when Afterburner is not running (or its interface is off)
        public List<string> ActiveIds = new List<string>();   // saved profiles matching the live curve ("Stock" when untouched)
        public Dictionary<string, double> GapToLive = new Dictionary<string, double>();   // per saved profile: largest clock gap to the live curve
        public bool LiveIsCustom { get { return Live != null && ActiveIds.Count == 0; } }
    }

    /// <summary>
    /// Reads the NVIDIA voltage/frequency curve from MSI Afterburner, read-only: the saved profiles (Profiles\VEN_10DE….cfg,
    /// VFCurve= blobs) and, when Afterburner runs, the curve applied right now through its documented control
    /// shared memory (SDK, MACMSharedMemory.h). Nothing is ever written: the suggested curve is shown, not applied.
    /// </summary>
    public static class GpuCurve
    {
        public static readonly string[] ProfileIds = { "Startup", "Profile1", "Profile2", "Profile3", "Profile4", "Profile5" };

        // ---------- Saved profiles ----------
        /// <summary>
        /// A VFCurve= value: a 12-byte header (format 0x00020000, point count, a reserved DWORD) then per point three
        /// little-endian floats: voltage mV, base frequency MHz, offset MHz (the same order as the live control memory).
        /// Checked against the live curve on an RTX 5080: base + offset of every saved point equals the applied clock.
        /// Null when the value is empty or not in that format.
        /// </summary>
        public static List<VfPoint> ParseSaved(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.Trim();
            if (hex.Length % 2 != 0 || hex.Length < 16) return null;
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int hi = HexVal(hex[2 * i]), lo = HexVal(hex[2 * i + 1]);
                if (hi < 0 || lo < 0) return null;
                b[i] = (byte)(hi * 16 + lo);
            }
            if (BitConverter.ToUInt32(b, 0) != 0x00020000) return null;
            uint n = BitConverter.ToUInt32(b, 4);
            if (n < 2 || n > 256 || SavedHeader + n * 12 > b.Length) return null;
            var pts = new List<VfPoint>((int)n);
            for (int k = 0; k < n; k++)
            {
                int o = SavedHeader + k * 12;
                var p = new VfPoint { MilliVolts = BitConverter.ToSingle(b, o), BaseMHz = BitConverter.ToSingle(b, o + 4), OffsetMHz = BitConverter.ToSingle(b, o + 8) };
                if (!Sane(p)) return null;
                pts.Add(p);
            }
            return Ordered(pts);
        }

        private const int SavedHeader = 12;

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        private static bool Sane(VfPoint p)
        {
            return !double.IsNaN(p.MilliVolts) && !double.IsNaN(p.BaseMHz) && !double.IsNaN(p.OffsetMHz)
                   && p.MilliVolts >= 300 && p.MilliVolts <= 1500 && p.BaseMHz >= 0 && p.BaseMHz <= 6000 && Math.Abs(p.OffsetMHz) <= 2000;
        }

        /// <summary>Voltages must rise point after point: anything else means the format was misread.</summary>
        private static List<VfPoint> Ordered(List<VfPoint> pts)
        {
            for (int i = 1; i < pts.Count; i++) if (pts[i].MilliVolts <= pts[i - 1].MilliVolts) return null;
            return pts;
        }

        /// <summary>The [sections] of an Afterburner profile file: Startup, Profile1–5 and Defaults, with their curve and offsets.</summary>
        public static Dictionary<string, VfProfile> ParseProfileFile(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, VfProfile>(StringComparer.OrdinalIgnoreCase);
            VfProfile cur = null;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                if (l.StartsWith("[", StringComparison.Ordinal) && l.EndsWith("]", StringComparison.Ordinal))
                {
                    cur = new VfProfile { Id = l.Substring(1, l.Length - 2) };
                    result[cur.Id] = cur;
                    continue;
                }
                if (cur == null) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                string key = l.Substring(0, eq), val = l.Substring(eq + 1);
                long kHz;
                if (key == "VFCurve") cur.Points = ParseSaved(val);
                else if (key == "CoreClkBoost" && long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out kHz)) cur.CoreBoostMHz = (int)(kHz / 1000);
                else if (key == "MemClkBoost" && long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out kHz)) cur.MemBoostMHz = (int)(kHz / 1000);
            }
            return result;
        }

        // ---------- Live curve (Afterburner SDK, control shared memory v2) ----------
        // MACM_SHARED_MEMORY_HEADER: signature 'MACM', version, header size, GPU count, entry size, master GPU…
        // MACM_SHARED_MEMORY_GPU_ENTRY: 69 DWORDs of settings, then MACM_SHARED_MEMORY_VF_CURVE (version, flags,
        // point count, 256 × {µV, kHz, offset kHz}, …), then szGpuId[MAX_PATH] at the end of the entry.
        private const uint MacmSignature = 0x4D41434D;        // 'MACM' as written by Afterburner (bytes "MCAM")
        private const int EntryMemBoostCur = 51 * 4;
        private const int EntryCoreBoostCur = 47 * 4;
        private const int EntryVfPointCount = 69 * 4 + 8;
        private const int EntryVfPoints = 69 * 4 + 12;
        private const int MaxPath = 260;

        /// <summary>The GPU entries of a MACM snapshot: (Afterburner GPU id, curve, core and memory offsets). Empty when invalid.</summary>
        public static List<Tuple<string, VfProfile>> ParseLive(byte[] m)
        {
            var list = new List<Tuple<string, VfProfile>>();
            if (m == null || m.Length < 24 || BitConverter.ToUInt32(m, 0) != MacmSignature) return list;
            if ((BitConverter.ToUInt32(m, 4) >> 16) != 2) return list;                       // v2.x layout only
            int header = (int)BitConverter.ToUInt32(m, 8), count = (int)BitConverter.ToUInt32(m, 12), size = (int)BitConverter.ToUInt32(m, 16);
            if (header < 24 || count < 0 || count > 16 || size < EntryVfPoints + 256 * 12 + MaxPath) return list;
            for (int g = 0; g < count; g++)
            {
                int e = header + g * size;
                if (e + size > m.Length) break;
                int n = (int)BitConverter.ToUInt32(m, e + EntryVfPointCount);
                if (n < 2 || n > 256) continue;
                var pts = new List<VfPoint>(n);
                bool ok = true;
                for (int k = 0; k < n && ok; k++)
                {
                    int o = e + EntryVfPoints + k * 12;
                    var p = new VfPoint
                    {
                        MilliVolts = BitConverter.ToUInt32(m, o) / 1000.0,
                        BaseMHz = BitConverter.ToUInt32(m, o + 4) / 1000.0,
                        OffsetMHz = BitConverter.ToInt32(m, o + 8) / 1000.0
                    };
                    ok = Sane(p);
                    pts.Add(p);
                }
                if (!ok || Ordered(pts) == null) continue;
                string id = System.Text.Encoding.ASCII.GetString(m, e + size - MaxPath, MaxPath);
                int nul = id.IndexOf('\0');
                if (nul >= 0) id = id.Substring(0, nul);
                list.Add(Tuple.Create(id.Trim(), new VfProfile
                {
                    Id = "Live",
                    Points = pts,
                    CoreBoostMHz = BitConverter.ToInt32(m, e + EntryCoreBoostCur) / 1000,
                    MemBoostMHz = BitConverter.ToInt32(m, e + EntryMemBoostCur) / 1000
                }));
            }
            return list;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr OpenFileMappingA(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr size);
        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
        private const uint FILE_MAP_READ = 4;

        /// <summary>A copy of Afterburner's control memory (read-only mapping, under its access mutex), null when not available.</summary>
        public static byte[] ReadLiveMemory()
        {
            IntPtr h = OpenFileMappingA(FILE_MAP_READ, false, "MACMSharedMemory");
            if (h == IntPtr.Zero) return null;
            System.Threading.Mutex mutex = null;
            bool owned = false;
            try
            {
                try { mutex = System.Threading.Mutex.OpenExisting(@"Global\Access_MACMSharedMemory", System.Security.AccessControl.MutexRights.Synchronize); owned = mutex.WaitOne(500); }
                catch (Exception) { }                               // no mutex: read anyway, the checks below reject a torn copy
                IntPtr p = MapViewOfFile(h, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
                if (p == IntPtr.Zero) return null;
                try
                {
                    int header = Marshal.ReadInt32(p, 8), count = Marshal.ReadInt32(p, 12), size = Marshal.ReadInt32(p, 16);
                    if (header < 24 || header > 4096 || count < 0 || count > 16 || size <= 0 || size > 65536) return null;
                    var b = new byte[header + count * size];
                    Marshal.Copy(p, b, 0, b.Length);
                    return b;
                }
                finally { UnmapViewOfFile(p); }
            }
            catch (Exception ex) { Log.Warn(ex, "MACM"); return null; }
            finally
            {
                if (mutex != null) { if (owned) { try { mutex.ReleaseMutex(); } catch (Exception) { } } mutex.Dispose(); }
                CloseHandle(h);
            }
        }

        // ---------- Putting it together ----------
        /// <summary>MSI Afterburner's folder (uninstall entry, else the default location), null when not installed.</summary>
        public static string AfterburnerDir()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Afterburner"))
                {
                    string loc = k != null ? (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"') : "";
                    if (loc.Length > 0 && Directory.Exists(Path.Combine(loc, "Profiles"))) return loc;
                }
            }
            catch (Exception ex) { Log.Warn(ex, "Afterburner location"); }
            string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MSI Afterburner");
            return Directory.Exists(Path.Combine(def, "Profiles")) ? def : null;
        }

        /// <summary>
        /// The curve information for the NVIDIA card: the Afterburner profile file named after the card Afterburner
        /// reports live, else the newest VEN_10DE profile. Null when Afterburner or an NVIDIA curve is absent.
        /// </summary>
        public static GpuCurveInfo Load()
        {
            string dir = AfterburnerDir();
            if (dir == null) return null;
            List<Tuple<string, VfProfile>> live = ParseLive(ReadLiveMemory());
            var files = new DirectoryInfo(Path.Combine(dir, "Profiles")).GetFiles("VEN_10DE&*.cfg").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            if (files.Count == 0) return null;

            Tuple<string, VfProfile> liveEntry = live.FirstOrDefault(t => files.Any(f => Path.GetFileNameWithoutExtension(f.Name).Equals(t.Item1, StringComparison.OrdinalIgnoreCase)));
            FileInfo file = liveEntry != null ? files.First(f => Path.GetFileNameWithoutExtension(f.Name).Equals(liveEntry.Item1, StringComparison.OrdinalIgnoreCase)) : files[0];
            return Build(Path.GetFileNameWithoutExtension(file.Name), file.FullName, File.ReadAllLines(file.FullName), liveEntry != null ? liveEntry.Item2 : null);
        }

        /// <summary>Assembles the info from a profile file's lines and an optional live curve (also used by the self-test).</summary>
        public static GpuCurveInfo Build(string gpuId, string file, IEnumerable<string> lines, VfProfile live)
        {
            Dictionary<string, VfProfile> sections = ParseProfileFile(lines);
            var info = new GpuCurveInfo { GpuId = gpuId, ProfileFile = file };
            foreach (string id in ProfileIds)
            {
                VfProfile p;
                if (sections.TryGetValue(id, out p) && p.Points != null) info.Profiles.Add(p);
            }
            // The stock curve as Afterburner draws it (grey line): the driver's current base clocks when it runs, which
            // drift with temperature, else the defaults saved in the file.
            VfProfile defaults;
            VfProfile src = live ?? (sections.TryGetValue("Defaults", out defaults) && defaults.Points != null ? defaults : info.Profiles.FirstOrDefault());
            if (src == null) return null;
            info.Stock = new VfProfile { Id = "Stock", Points = src.Points.Select(p => new VfPoint { MilliVolts = p.MilliVolts, BaseMHz = p.BaseMHz }).ToList() };

            if (live != null)
            {
                info.Live = new VfProfile { Id = "Live", CoreBoostMHz = live.CoreBoostMHz, MemBoostMHz = live.MemBoostMHz, Points = live.Points };
                // Afterburner recomputes the offsets when it applies a profile (the base clocks are not the saved ones):
                // a saved profile is the applied one when its real clocks match, point by point, within two clock bins.
                if (!info.Live.Modified) info.ActiveIds.Add("Stock");
                foreach (VfProfile p in info.Profiles)
                {
                    double gap = GapMHz(p, info.Live);
                    info.GapToLive[p.Id] = gap;
                    if (gap <= MatchTolerance && p.Modified == info.Live.Modified && p.MemBoostMHz == info.Live.MemBoostMHz) info.ActiveIds.Add(p.Id);
                }
            }
            return info;
        }

        /// <summary>Two clock bins: what temperature alone moves a point by between saving and applying.</summary>
        public const double MatchTolerance = 30;

        /// <summary>Largest difference in real clock between two curves, over the voltages both have (infinity when none).</summary>
        public static double GapMHz(VfProfile a, VfProfile b)
        {
            double gap = -1;
            foreach (VfPoint p in a.Points)
            {
                VfPoint q = b.Points.FirstOrDefault(x => Math.Abs(x.MilliVolts - p.MilliVolts) <= 1);
                if (q != null) gap = Math.Max(gap, Math.Abs(p.MHz - q.MHz));
            }
            return gap < 0 ? double.PositiveInfinity : gap;
        }

        // ---------- Analysis ----------
        /// <summary>One clock bin of margin is not enough to absorb a bad sample: the suggested plateau sits two bins (15 MHz) lower.</summary>
        public const double Margin = 15;

        /// <summary>
        /// Reads a curve the way the GPU will use it and suggests a safer version of the same idea: same target
        /// voltage, the plateau a little lower, a ramp that climbs no faster than the stock curve plus the plateau's
        /// offset, and nothing above the plateau. The tips are heuristics to validate with the stability test, not proof.
        /// </summary>
        public static CurveAdvice Analyze(VfProfile curve, VfProfile stock)
        {
            var a = new CurveAdvice();
            if (curve == null || curve.Points == null || curve.Points.Count < 2) return a;
            List<VfPoint> c = curve.Points;
            double[] eff = c.Select(p => p.MHz).ToArray();
            double[] st = c.Select(p => StockAt(stock, p)).ToArray();
            double max = eff.Max();
            int iMax = Array.IndexOf(eff, max);
            int iV = Array.FindIndex(eff, v => v >= max - Margin);
            a.TargetMv = c[iV].MilliVolts;
            a.PlateauMHz = eff[iV];
            a.RunsAtMv = c[iMax].MilliVolts;
            a.RunsAtMHz = max;

            if (!curve.Modified)
            {
                a.Tips.Add(new CurveTip { Kind = CurveTipKind.Info, Text = Localizer.S("gpu_curve.tip_stock") });
                if (curve.MemBoostMHz != 0) a.Tips.Add(MemoryTip(curve.MemBoostMHz));
                return a;
            }

            // A point above the plateau, further right: the GPU climbs to it, so it runs at that voltage, not the target one.
            if (iMax > iV && max > eff[iV] + 5)
                a.Tips.Add(new CurveTip { Kind = CurveTipKind.Danger, Text = Localizer.F("gpu_curve.tip_spike", Mv(c[iMax].MilliVolts), Mhz(max), Mhz(eff[iV]), Mv(c[iV].MilliVolts)) });

            // Points below the target pushed harder than the target itself: they are the ones used at light load
            // (video, desktop), where an undervolt that is not stable makes the NVIDIA driver stop responding.
            double plateauOffset = eff[iV] - st[iV];
            int from = -1, to = -1; double worst = 0;
            for (int i = 0; i < iV; i++)
            {
                double off = eff[i] - st[i];
                if (off <= plateauOffset + Margin) continue;
                if (from < 0) from = i;
                to = i;
                worst = Math.Max(worst, off);
            }
            if (from >= 0)
                a.Tips.Add(new CurveTip { Kind = CurveTipKind.Warning, Text = Localizer.F("gpu_curve.tip_overboost", Mv(c[from].MilliVolts), Mv(c[to].MilliVolts), Signed(worst), Signed(plateauOffset)) });

            a.Tips.Add(new CurveTip { Kind = CurveTipKind.Info, Text = Localizer.F("gpu_curve.tip_plateau", Mhz(eff[iV]), Mv(c[iV].MilliVolts), Signed(plateauOffset)) });
            if (curve.MemBoostMHz != 0) a.Tips.Add(MemoryTip(curve.MemBoostMHz));

            // Suggested curve: the same idea made safer. Flat two bins under the plateau from the target voltage on,
            // and below it no point pushed more than the target (offset capped); no point is ever raised, and the
            // curve never goes down (lowering earlier points, never raising later ones).
            double target = eff[iV] - Margin;
            double cap = target - st[iV];
            var s = new double[c.Count];
            for (int i = 0; i < c.Count; i++) s[i] = i >= iV ? target : Math.Min(eff[i], st[i] + Math.Max(0, cap));
            for (int i = s.Length - 2; i >= 0; i--) s[i] = Math.Min(s[i], s[i + 1]);
            a.Suggested = s.Select(v => Math.Round(v)).ToList();
            a.SuggestedMHz = Math.Round(target);

            // Points to change by hand (every ~25 mV among those that move), then the plateau.
            double lastMv = double.MinValue;
            for (int i = 0; i < iV; i++)
            {
                // Less than one clock bin (~7.5 MHz) changes nothing the card would do: not worth a row.
                if (Math.Abs(a.Suggested[i] - eff[i]) < Margin / 2 || c[i].MilliVolts - lastMv < 24) continue;
                a.KeyPoints.Add(new KeyValuePair<double, double>(c[i].MilliVolts, a.Suggested[i]));
                lastMv = c[i].MilliVolts;
            }
            a.KeyPoints.Add(new KeyValuePair<double, double>(c[iV].MilliVolts, a.SuggestedMHz));
            return a;
        }

        private static double StockAt(VfProfile stock, VfPoint p)
        {
            if (stock == null || stock.Points == null) return p.BaseMHz;
            VfPoint near = stock.Points.OrderBy(q => Math.Abs(q.MilliVolts - p.MilliVolts)).First();
            return Math.Abs(near.MilliVolts - p.MilliVolts) <= 1 ? near.BaseMHz : p.BaseMHz;
        }

        private static CurveTip MemoryTip(int memBoost)
        {
            return new CurveTip { Kind = CurveTipKind.Info, Text = Localizer.F("gpu_curve.tip_memory", Signed(memBoost)) };
        }

        public static string Mv(double mv) { return Math.Round(mv).ToString("0", CultureInfo.InvariantCulture) + " mV"; }
        public static string Mhz(double mhz) { return Math.Round(mhz).ToString("0", CultureInfo.InvariantCulture) + " MHz"; }
        private static string Signed(double v) { return (v >= 0 ? "+" : "−") + Math.Abs(Math.Round(v)).ToString("0", CultureInfo.InvariantCulture); }

        /// <summary>Display name of a profile id ("Startup" → "At startup", "Profile3" → "Profile 3").</summary>
        public static string ProfileName(string id)
        {
            if (id == "Live") return Localizer.S("gpu_curve.applied_now");
            if (id == "Startup") return Localizer.S("gpu_curve.startup");
            if (id == "Stock") return Localizer.S("gpu_curve.stock");
            if (id != null && id.StartsWith("Profile", StringComparison.Ordinal)) return Localizer.F("gpu_curve.profile_n", id.Substring(7));
            return id;
        }
    }
}
