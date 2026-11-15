using System;
using System.Management;

namespace WinCrashDoctor
{
    /// <summary>
    /// WMI searchers with a timeout. Without one, a stuck or corrupted WMI service (winmgmt) makes the
    /// query, and therefore the whole scan, wait forever. Results are forward-only (less memory).
    /// </summary>
    public static class Wmi
    {
        /// <summary>Per-call timeout (Win32_PnPSignedDriver alone can take several seconds on a healthy PC).</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        private static EnumerationOptions Options()
        {
            return new EnumerationOptions { Timeout = Timeout, ReturnImmediately = true, Rewindable = false };
        }

        public static ManagementObjectSearcher Searcher(string query)
        {
            return new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(query), Options());
        }

        public static ManagementObjectSearcher Searcher(string scope, string query)
        {
            return new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), Options());
        }

        public static ManagementObjectSearcher Searcher(ManagementScope scope, ObjectQuery query)
        {
            return new ManagementObjectSearcher(scope, query, Options());
        }
    }
}
