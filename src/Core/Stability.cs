using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCrashDoctor
{
    public enum StabilityLevel
    {
        Stable,
        Fragile,
        Unstable
    }

    /// <summary>One point of the 30-day stability timeline.</summary>
    public class StabilityDay
    {
        public DateTime Date { get; set; }
        public double Score { get; set; }
        public List<CrashEventItem> Events { get; set; }
    }

    /// <summary>
    /// Stability score 0–100 derived from the crash events of the last 30 days.
    /// Severity-weighted penalties that fade with age, so a crash fixed three weeks ago
    /// weighs much less than one from yesterday.
    /// (Phase 3 will blend in Windows' own reliability index.)
    /// </summary>
    public static class Stability
    {
        public const int WindowDays = 30;

        public static double Penalty(CrashCategory c)
        {
            switch (c)
            {
                case CrashCategory.BSOD: return 20;
                case CrashCategory.UnexpectedReboot: return 16;
                case CrashCategory.HardwareWhea: return 15;
                case CrashCategory.GpuDriver: return 10;
                case CrashCategory.StorageNvme: return 10;
                case CrashCategory.MemoryExhaustion: return 7;
                case CrashCategory.BrowserGpu: return 3;
                case CrashCategory.AppCrash: return 2;
                default: return 2;
            }
        }

        /// <summary>Weight of an event by age: full for a week, then fading to 20 % at 30 days.</summary>
        private static double AgeWeight(double ageDays)
        {
            if (ageDays < 0) ageDays = 0;
            if (ageDays <= 7) return 1.0;
            if (ageDays >= WindowDays) return 0.2;
            return 1.0 - 0.8 * (ageDays - 7) / (WindowDays - 7);
        }

        /// <summary>Each repeat within a category weighs 30 % less than the previous one.</summary>
        private const double RepeatDamping = 0.7;

        /// <summary>
        /// Sums penalties with diminishing returns per category, so a noisy app that crashes daily
        /// cannot outweigh one blue screen (a category's total is capped at ~3.3× its base penalty).
        /// </summary>
        private static double DampedPenalty(IEnumerable<KeyValuePair<CrashCategory, double>> weighted)
        {
            double total = 0;
            foreach (var group in weighted.GroupBy(p => p.Key))
            {
                double factor = 1.0;
                foreach (var w in group.Select(p => p.Value).OrderByDescending(v => v))
                {
                    total += Penalty(group.Key) * w * factor;
                    factor *= RepeatDamping;
                }
            }
            return total;
        }

        public static int Score(IEnumerable<CrashEventItem> events, DateTime now)
        {
            var weighted = (events ?? Enumerable.Empty<CrashEventItem>())
                .Where(e => (now - e.Timestamp).TotalDays <= WindowDays)
                .Select(e => new KeyValuePair<CrashCategory, double>(e.Category, AgeWeight((now - e.Timestamp).TotalDays)));
            return (int)Math.Round(Math.Max(5, Math.Min(100, 100 - DampedPenalty(weighted))));
        }

        /// <summary>Categories drawn as markers on the timeline (app/browser crashes are too frequent to be signal).</summary>
        public static bool IsMarkerWorthy(CrashCategory c)
        {
            return c != CrashCategory.AppCrash && c != CrashCategory.BrowserGpu;
        }

        /// <summary>Home score: 60 % crash analysis + 40 % Windows reliability index (when available).</summary>
        public static int Blend(int crashScore, double? windowsIndex)
        {
            return windowsIndex.HasValue ? (int)Math.Round(0.6 * crashScore + 0.4 * windowsIndex.Value * 10) : crashScore;
        }

        public static StabilityLevel LevelOf(int score)
        {
            if (score >= 85) return StabilityLevel.Stable;
            if (score >= 60) return StabilityLevel.Fragile;
            return StabilityLevel.Unstable;
        }

        /// <summary>
        /// Daily series for the timeline: each day carries the events of that day and a value that dips
        /// on crash days and recovers over the following days (3-day half-life).
        /// </summary>
        public static List<StabilityDay> Timeline(IEnumerable<CrashEventItem> events, DateTime now, int days = WindowDays)
        {
            var list = (events ?? Enumerable.Empty<CrashEventItem>()).ToList();
            var result = new List<StabilityDay>();
            DateTime today = now.Date;
            for (int i = days - 1; i >= 0; i--)
            {
                DateTime d = today.AddDays(-i);
                DateTime dayEnd = d.AddDays(1);
                double penalty = DampedPenalty(list
                    .Where(e => e.Timestamp < dayEnd && (dayEnd - e.Timestamp).TotalDays <= 14)
                    .Select(e => new KeyValuePair<CrashCategory, double>(e.Category, Math.Pow(0.5, (dayEnd - e.Timestamp).TotalDays / 3.0) * 1.6)));
                result.Add(new StabilityDay
                {
                    Date = d,
                    Score = Math.Max(5, Math.Min(100, 100 - penalty)),
                    Events = list.Where(e => e.Timestamp >= d && e.Timestamp < dayEnd).ToList()
                });
            }
            return result;
        }

        public static bool IsSevere(CrashCategory c)
        {
            return c == CrashCategory.BSOD || c == CrashCategory.UnexpectedReboot || c == CrashCategory.HardwareWhea;
        }
    }
}
