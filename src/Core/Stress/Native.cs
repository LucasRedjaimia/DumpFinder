using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinCrashDoctor.Stress
{
    /// <summary>kernel32 calls used by the stress engines (memory status, core topology, affinity, unbuffered I/O).</summary>
    internal static class Native
    {
        // ---------------- Memory status
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static ulong AvailablePhys()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? m.ullAvailPhys : 0;
        }

        public static ulong TotalPhys()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? m.ullTotalPhys : 0;
        }

        // ---------------- Core topology & affinity
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
        {
            public UIntPtr ProcessorMask;
            public int Relationship;
            public ulong Reserved1;
            public ulong Reserved2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint returnLength);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentProcessorNumber();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);

        private static List<int> _cores;

        /// <summary>The first logical processor of every physical core (one thread per core, like CoreCycler).</summary>
        public static List<int> FirstLogicalOfEachCore()
        {
            if (_cores != null) return _cores;
            var list = new List<int>();
            try
            {
                uint len = 0;
                GetLogicalProcessorInformation(IntPtr.Zero, ref len);
                if (len > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)len);
                    try
                    {
                        if (GetLogicalProcessorInformation(buf, ref len))
                        {
                            int size = Marshal.SizeOf(typeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION));
                            for (int off = 0; off + size <= len; off += size)
                            {
                                var info = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION)Marshal.PtrToStructure(buf + off, typeof(SYSTEM_LOGICAL_PROCESSOR_INFORMATION));
                                if (info.Relationship != 0) continue; // RelationProcessorCore
                                ulong mask = info.ProcessorMask.ToUInt64();
                                for (int b = 0; b < 64; b++)
                                    if ((mask & (1UL << b)) != 0) { list.Add(b); break; }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            if (list.Count == 0)
                for (int i = 0; i < Environment.ProcessorCount; i++) list.Add(i);
            list.Sort();
            _cores = list;
            return list;
        }

        /// <summary>Pins the calling OS thread to one logical processor (0 = any).</summary>
        public static void PinCurrentThread(int logical)
        {
            ulong mask = logical < 0 ? ulong.MaxValue >> (64 - Math.Min(64, Environment.ProcessorCount)) : (1UL << logical);
            SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(mask));
        }

        // ---------------- Keep the PC awake during long tests
        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint flags);
        public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        // ---------------- Unbuffered file I/O
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint CREATE_ALWAYS = 2;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
        public const uint FILE_FLAG_DELETE_ON_CLOSE = 0x04000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, uint count, out uint written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint count, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPos, uint method);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);

        /// <summary>Page-aligned buffer, as unbuffered I/O requires.</summary>
        public static IntPtr AllocAligned(long bytes)
        {
            return VirtualAlloc(IntPtr.Zero, new UIntPtr((ulong)bytes), 0x1000 | 0x2000 /* COMMIT|RESERVE */, 0x04 /* READWRITE */);
        }

        public static void FreeAligned(IntPtr p)
        {
            if (p != IntPtr.Zero) VirtualFree(p, UIntPtr.Zero, 0x8000 /* RELEASE */);
        }
    }
}
