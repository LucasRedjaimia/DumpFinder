using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WinCrashDoctor.Stress;

namespace WinCrashDoctor
{
    /// <summary>One applied (or failed) fix, with what it changed.</summary>
    public class JournalEntry
    {
        public string Id { get; set; }
        public DateTime Time { get; set; }
        public string FixId { get; set; }
        public string Op { get; set; }
        public string Title { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
        public bool Reversible { get; set; }
        public DateTime? RevertedAt { get; set; }
        public List<ChangeRecord> Changes { get; set; }
    }

    /// <summary>Summary of one scan, for trends and before/after comparisons.</summary>
    public class ScanRecord
    {
        public DateTime Time { get; set; }
        public int Score { get; set; }
        public int CrashScore { get; set; }
        public double? WindowsIndex { get; set; }
        public int Crashes30 { get; set; }
        public int Severe30 { get; set; }
        public string Diagnosis { get; set; }
    }

    /// <summary>
    /// Local history in %LOCALAPPDATA%\DumpFinder\history (small JSON files, newest kept).
    /// Nothing leaves the PC.
    /// </summary>
    public static class History
    {
        private const int MaxScans = 365;
        private const int MaxJournal = 500;
        private static readonly object Sync = new object();

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder", "history"); }
        }

        private static string ScansFile { get { return Path.Combine(Dir, "scans.json"); } }
        private static string JournalFile { get { return Path.Combine(Dir, "journal.json"); } }
        private static string SeenFile { get { return Path.Combine(Dir, "seen_crashes.json"); } }
        private static string StressFile { get { return Path.Combine(Dir, "stress.json"); } }
        private static string StressMarkerFile { get { return Path.Combine(Dir, "stress_running.json"); } }
        private const int MaxStressRuns = 50;

        // ---------------- Scans
        public static List<ScanRecord> Scans()
        {
            var list = Load<List<ScanRecord>>(ScansFile) ?? new List<ScanRecord>();
            foreach (var s in list) s.Time = Local(s.Time);
            return list;
        }

        public static void AddScan(ScanRecord r)
        {
            lock (Sync)
            {
                var list = Scans();
                list.Add(r);
                if (list.Count > MaxScans) list = list.Skip(list.Count - MaxScans).ToList();
                Save(ScansFile, list);
            }
        }

        /// <summary>Most recent scan from an earlier day (for "since …" comparisons).</summary>
        public static ScanRecord PreviousDay(DateTime now)
        {
            return Scans().Where(s => s.Time.Date < now.Date).OrderByDescending(s => s.Time).FirstOrDefault();
        }

        // ---------------- Fix journal
        public static List<JournalEntry> Journal()
        {
            var list = Load<List<JournalEntry>>(JournalFile) ?? new List<JournalEntry>();
            foreach (var j in list) { j.Time = Local(j.Time); if (j.RevertedAt.HasValue) j.RevertedAt = Local(j.RevertedAt.Value); }
            return list.OrderByDescending(j => j.Time).ToList();
        }

        public static void AddJournal(IEnumerable<JournalEntry> entries)
        {
            lock (Sync)
            {
                var list = Load<List<JournalEntry>>(JournalFile) ?? new List<JournalEntry>();
                list.AddRange(entries);
                if (list.Count > MaxJournal) list = list.OrderByDescending(j => j.Time).Take(MaxJournal).ToList();
                Save(JournalFile, list);
            }
        }

        public static void MarkReverted(string id)
        {
            lock (Sync)
            {
                var list = Load<List<JournalEntry>>(JournalFile) ?? new List<JournalEntry>();
                var e = list.FirstOrDefault(j => j.Id == id);
                if (e == null) return;
                e.RevertedAt = DateTime.Now;
                Save(JournalFile, list);
            }
        }

        // ---------------- New-crash detection
        /// <summary>Keys never seen before, without remembering them (used by the tray at logon).</summary>
        public static List<string> PeekNewCrashes(IEnumerable<string> currentKeys)
        {
            if (!File.Exists(SeenFile)) return new List<string>();
            var seen = new HashSet<string>(Load<List<string>>(SeenFile) ?? new List<string>());
            return currentKeys.Where(k => !seen.Contains(k)).Distinct().ToList();
        }

        /// <summary>
        /// Returns the keys in <paramref name="currentKeys"/> never seen before and remembers them.
        /// The very first call only records (no "new" crashes on first launch).
        /// </summary>
        public static List<string> TakeNewCrashes(IEnumerable<string> currentKeys)
        {
            lock (Sync)
            {
                bool firstRun = !File.Exists(SeenFile);
                var seen = new HashSet<string>(Load<List<string>>(SeenFile) ?? new List<string>());
                var fresh = currentKeys.Where(k => !seen.Contains(k)).Distinct().ToList();
                foreach (var k in fresh) seen.Add(k);
                Save(SeenFile, seen.Skip(Math.Max(0, seen.Count - 5000)).ToList());
                return firstRun ? new List<string>() : fresh;
            }
        }

        /// <summary>JavaScriptSerializer round-trips DateTime as UTC: bring it back to local time.</summary>
        public static DateTime Local(DateTime t)
        {
            return t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;
        }

        // ---------------- Stability tests
        public static List<StressRunRecord> StressRuns()
        {
            var list = Load<List<StressRunRecord>>(StressFile) ?? new List<StressRunRecord>();
            foreach (var r in list) r.Time = Local(r.Time);
            return list.OrderByDescending(r => r.Time).ToList();
        }

        public static void AddStressRun(StressRunRecord r)
        {
            lock (Sync)
            {
                var list = Load<List<StressRunRecord>>(StressFile) ?? new List<StressRunRecord>();
                list.Add(r);
                if (list.Count > MaxStressRuns) list = list.Skip(list.Count - MaxStressRuns).ToList();
                Save(StressFile, list);
            }
        }

        public static void SaveStressMarker(StressRunningMarker m) { Save(StressMarkerFile, m); }

        public static StressRunningMarker PeekStressMarker() { return Load<StressRunningMarker>(StressMarkerFile); }

        public static void ClearStressMarker()
        {
            if (Localizer.ReadOnlySettings) return;
            try { if (File.Exists(StressMarkerFile)) File.Delete(StressMarkerFile); } catch (Exception ex) { Log.Warn(ex); }
        }

        // ---------------- IO
        private static T Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, path);
                return null;
            }
        }

        private static void Save(string path, object data)
        {
            if (Localizer.ReadOnlySettings) return; // screenshot harness never writes
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, path);
            }
        }
    }
}
