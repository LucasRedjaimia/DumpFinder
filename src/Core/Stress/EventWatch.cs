using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace WinCrashDoctor.Stress
{
    public enum LiveEventKind { GpuDriver, Whea, Disk }

    public sealed class LiveEvent
    {
        public DateTime Time;
        public LiveEventKind Kind;
        public string Provider;
        public int Id;
        public override string ToString() { return Provider + " " + Id; }
    }

    /// <summary>
    /// Watches the System log during a test for the errors an unstable machine produces before (or instead of)
    /// crashing: GPU driver resets, WHEA hardware errors (corrected ones included — they are the early warning
    /// of an unstable CPU/RAM/PCIe link) and storage timeouts.
    /// </summary>
    internal sealed class EventWatch
    {
        private readonly DateTime _since;
        private readonly HashSet<long> _seen = new HashSet<long>();

        public EventWatch(DateTime since) { _since = since; }

        public List<LiveEvent> Poll()
        {
            var found = new List<LiveEvent>();
            try
            {
                string utc = _since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                string xpath = "*[System[(EventID=13 or EventID=14 or EventID=153 or EventID=4101 or EventID=17 or EventID=18 or EventID=19 or EventID=46 or EventID=47"
                             + " or EventID=7 or EventID=11 or EventID=51 or EventID=55 or EventID=129) and TimeCreated[@SystemTime >= '" + utc + "']]]";
                using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath)))
                {
                    EventRecord rec;
                    while ((rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            long key = rec.RecordId ?? (rec.TimeCreated.HasValue ? rec.TimeCreated.Value.Ticks : 0);
                            if (!_seen.Add(key)) continue;
                            LiveEventKind kind;
                            if (!Classify(rec.ProviderName ?? "", rec.Id, out kind)) continue;
                            found.Add(new LiveEvent
                            {
                                Time = rec.TimeCreated.HasValue ? rec.TimeCreated.Value.ToLocalTime() : DateTime.Now,
                                Kind = kind,
                                Provider = rec.ProviderName,
                                Id = rec.Id
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return found;
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
