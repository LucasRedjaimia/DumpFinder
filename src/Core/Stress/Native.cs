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

        // ---------------- Which physical disk(s) hold a volume
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

        private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000; // FILE_ANY_ACCESS: no admin needed
        private const uint OPEN_EXISTING = 3, FILE_SHARE_READ_WRITE = 3;

        /// <summary>Physical disk numbers ("\Device\HarddiskN") behind a volume such as "C:"; empty when unknown.</summary>
        public static List<int> VolumeDiskNumbers(string drive)
        {
            var list = new List<int>();
            IntPtr buf = IntPtr.Zero;
            try
            {
                string vol = @"\\.\" + drive.TrimEnd('\\');
                using (SafeFileHandle h = CreateFileW(vol, 0, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return list;
                    const int max = 16;
                    uint size = 8 + 24 * max; // VOLUME_DISK_EXTENTS: count (+pad), then DISK_EXTENT {DiskNumber(+pad), offset, length}
                    buf = Marshal.AllocHGlobal((int)size);
                    uint ret;
                    if (!DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, buf, size, out ret, IntPtr.Zero)) return list;
                    int n = Math.Min(max, Marshal.ReadInt32(buf, 0));
                    for (int i = 0; i < n; i++)
                    {
                        int disk = Marshal.ReadInt32(buf, 8 + 24 * i);
                        if (!list.Contains(disk)) list.Add(disk);
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
            return list;
        }

        // ---------------- Kill-on-close job for helper processes (nvidia-smi -lms runs until killed)
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObjectW(IntPtr security, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        private static readonly object JobLock = new object();
        private static IntPtr _killJob;

        /// <summary>
        /// Puts a child process in a job that Windows closes when this process exits (handle never closed on purpose),
        /// which kills the child even if the app is terminated without running its cleanup. Best effort.
        /// </summary>
        public static void KillWithThisProcess(System.Diagnostics.Process p)
        {
            try
            {
                lock (JobLock)
                {
                    if (_killJob == IntPtr.Zero)
                    {
                        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
                        if (job == IntPtr.Zero) return;
                        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                        info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        if (!SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                            return; // handle intentionally leaked (tiny, once)
                        _killJob = job;
                    }
                    AssignProcessToJobObject(_killJob, p.Handle);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }
    }
}
