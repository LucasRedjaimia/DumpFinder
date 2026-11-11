using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace WinCrashDoctor
{
    public class ReliabilityPoint
    {
        public DateTime Date { get; set; }
        /// <summary>Windows System Stability Index, 1 (worst) to 10 (best).</summary>
        public double Index { get; set; }
    }

    /// <summary>
    /// Windows' own Reliability Monitor index (Win32_ReliabilityStabilityMetrics, hourly samples).
    /// Empty when the RAC task is disabled on the machine.
    /// </summary>
    public static class Reliability
    {
        public static List<ReliabilityPoint> Daily(int days)
        {
            var result = new List<ReliabilityPoint>();
            try
            {
                string since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.Date.AddDays(-days));
                var q = new ObjectQuery("SELECT TimeGenerated, SystemStabilityIndex FROM Win32_ReliabilityStabilityMetrics WHERE TimeGenerated >= '" + since + "'");
                var samples = new List<KeyValuePair<DateTime, double>>();
                using (var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), q))
                using (var col = searcher.Get())
                {
                    foreach (ManagementObject mo in col)
                    {
                        using (mo)
                        {
                            object t = mo["TimeGenerated"], v = mo["SystemStabilityIndex"];
                            if (t == null || v == null) continue;
                            samples.Add(new KeyValuePair<DateTime, double>(ManagementDateTimeConverter.ToDateTime(t.ToString()), Convert.ToDouble(v)));
                        }
                    }
                }
                // One value per day: the last sample of the day.
                result = samples
                    .GroupBy(s => s.Key.Date)
                    .Select(g => new ReliabilityPoint { Date = g.Key, Index = Math.Round(g.OrderBy(s => s.Key).Last().Value, 2) })
                    .OrderBy(p => p.Date)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Win32_ReliabilityStabilityMetrics");
            }
            return result;
        }

        public static double? Latest(List<ReliabilityPoint> points)
        {
            if (points == null || points.Count == 0) return null;
            return points[points.Count - 1].Index;
        }
    }
}
