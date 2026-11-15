using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinCrashDoctor.Stress
{
    public enum LiveEventKind { GpuDriver, Whea, Disk }

    public sealed class LiveEvent
    {
        public DateTime Time;
        public LiveEventKind Kind;
        public string Provider;
        public int Id;
        /// <summary>
        /// Recorded and reported as a warning, but never aborts or fails the stage: a corrected WHEA error, or a disk
        /// event that is about another disk (or whose disk cannot be identified, for disk 7/11/153).
        /// </summary>
        public bool WarningOnly;
        /// <summary>Short localized qualifier shown after the event ("corrected", "other disk"…), or null.</summary>
        public string Note;
        public override string ToString() { return Provider + " " + Id + (string.IsNullOrEmpty(Note) ? "" : " (" + Note + ")"); }
    }

    /// <summary>
    /// Watches the System log during a test for the errors an unstable machine produces before (or instead of)
    /// crashing: GPU driver resets, WHEA hardware errors (corrected ones are kept as an early warning of an unstable
    /// CPU/RAM/PCIe link, uncorrected ones are failures) and storage errors / timeouts.
    /// </summary>
    internal sealed class EventWatch
    {
        private readonly DateTime _since;
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly object _seenLock = new object(); // timer callbacks may overlap

        private static readonly object DiskLock = new object();
        private static List<int> _testDisks;

        public EventWatch(DateTime since) { _since = since; }

        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        /// <summary>
        /// WHEA-Logger IDs for errors the hardware CORRECTED (17 = PCIe, 19 = machine check, 47 = memory). Every
        /// other WHEA ID we watch (1, 18, 20, 46) is fatal / uncorrected.
        /// </summary>
        private static bool IsCorrectedWhea(int id) { return id == 17 || id == 19 || id == 47; }

        public List<LiveEvent> Poll()
        {
            var found = new List<LiveEvent>();
            try
            {
                string utc = _since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                string xpath = "*[System[(EventID=13 or EventID=14 or EventID=153 or EventID=4101 or EventID=1 or EventID=17 or EventID=18 or EventID=19 or EventID=20 or EventID=46 or EventID=47"
                             + " or EventID=7 or EventID=11 or EventID=51 or EventID=55 or EventID=129) and TimeCreated[@SystemTime >= '" + utc + "']]]";
                using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath)))
                {
                    EventRecord rec;
                    while ((rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            long key = rec.RecordId ?? (rec.TimeCreated.HasValue ? rec.TimeCreated.Value.Ticks : 0);
                            bool fresh;
                            lock (_seenLock) fresh = _seen.Add(key);
                            if (!fresh) continue;
                            LiveEventKind kind;
                            string provider = rec.ProviderName ?? "";
                            if (!Classify(provider, rec.Id, out kind)) continue;
                            var ev = new LiveEvent
                            {
                                Time = rec.TimeCreated.HasValue ? rec.TimeCreated.Value.ToLocalTime() : DateTime.Now,
                                Kind = kind,
                                Provider = rec.ProviderName,
                                Id = rec.Id
                            };
                            if (kind == LiveEventKind.Whea && IsCorrectedWhea(rec.Id))
                            {
                                ev.WarningOnly = true;
                                ev.Note = L("corrigée", "corrected", "corregido", "korrigiert", "corretto", "corrigido");
                            }
                            else if (kind == LiveEventKind.Disk)
                                QualifyDisk(rec, provider, ev);
                            found.Add(ev);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return found;
        }

        /// <summary>
        /// A disk event only counts against the disk stage when it is about the disk under test. The device is read
        /// from the event data ("\Device\HarddiskN\…", or the volume letter for Ntfs 55). When it cannot be told,
        /// disk 7 / 11 / 153 (often a USB stick or card reader) become warnings; controller events keep failing.
        /// </summary>
        private static void QualifyDisk(EventRecord rec, string provider, LiveEvent ev)
        {
            bool? onTestDisk = null;
            try
            {
                var sb = new StringBuilder();
                string volume = null;
                if (rec.Properties != null)
                    foreach (EventProperty p in rec.Properties)
                    {
                        string s = p != null && p.Value != null ? Convert.ToString(p.Value, CultureInfo.InvariantCulture) : null;
                        if (string.IsNullOrEmpty(s)) continue;
                        sb.Append(s).Append(' ');
                        if (volume == null && Regex.IsMatch(s.Trim(), @"^[A-Za-z]:\\?$")) volume = s.Trim().Substring(0, 2);
                    }
                Match m = Regex.Match(sb.ToString(), @"\\Device\\Harddisk(\d+)\\", RegexOptions.IgnoreCase);
                int disk;
                List<int> test = TestDisks();
                if (m.Success && test.Count > 0 && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out disk))
                    onTestDisk = test.Contains(disk);
                else if (volume != null)
                    onTestDisk = string.Equals(volume, StressPlanner.TestDrive, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Warn(ex); }

            if (onTestDisk == true) return;
            if (onTestDisk == false)
            {
                ev.WarningOnly = true;
                ev.Note = L("autre disque", "other disk", "otro disco", "anderes Laufwerk", "altro disco", "outro disco");
                return;
            }
            bool diskClassEvent = provider.Equals("disk", StringComparison.OrdinalIgnoreCase) && (ev.Id == 7 || ev.Id == 11 || ev.Id == 153);
            if (diskClassEvent)
            {
                ev.WarningOnly = true;
                ev.Note = L("disque non identifié", "disk not identified", "disco no identificado", "Laufwerk nicht ermittelt", "disco non identificato", "disco não identificado");
            }
        }

        /// <summary>Physical disk numbers behind the test drive (computed once; empty when unknown).</summary>
        private static List<int> TestDisks()
        {
            lock (DiskLock)
            {
                if (_testDisks == null) _testDisks = Native.VolumeDiskNumbers(StressPlanner.TestDrive);
                return _testDisks;
            }
        }

        private static bool Classify(string p, int id, out LiveEventKind kind)
        {
            kind = LiveEventKind.Whea;
            if (Has(p, "WHEA")) { kind = LiveEventKind.Whea; return true; }
            if (Has(p, "nvlddmkm") || Has(p, "amdkmdag") || Has(p, "amdwddmg") || Has(p, "amdkmdap") || Has(p, "igfx") || Has(p, "igdkmd")
                || (p.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101))
            {
                kind = LiveEventKind.GpuDriver; return id == 13 || id == 14 || id == 153 || id == 4101;
            }
            if (Has(p, "stornvme") || Has(p, "storahci") || Has(p, "storport") || p.Equals("disk", StringComparison.OrdinalIgnoreCase)
                || (Has(p, "Ntfs") && id == 55))
            {
                kind = LiveEventKind.Disk; return true;
            }
            return false;
        }

        private static bool Has(string s, string part) { return s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }
    }
}
