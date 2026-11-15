using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinCrashDoctor
{
    /// <summary>
    /// Deletes a file or an empty folder only when it really lies inside a given folder.
    /// The item is opened first (without following a link on the item itself), its final on-disk location is read
    /// from the handle, and the delete goes through that same handle. A folder swapped for a junction between
    /// the scan and the delete therefore cannot redirect an elevated cleanup to System32 or anywhere else.
    /// </summary>
    internal static class SafeDelete
    {
        private const uint DELETE = 0x00010000;
        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_WRITE_ATTRIBUTES = 0x0100;
        private const uint FILE_SHARE_ALL = 0x7;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_ATTRIBUTE_READONLY = 0x1;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const int FileBasicInfo = 0;
        private const int FileDispositionInfo = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_BASIC_INFO
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO
        {
            public byte DeleteFile; // BOOLEAN
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FILE_BASIC_INFO info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref FILE_BASIC_INFO info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref FILE_DISPOSITION_INFO info, uint size);

        /// <summary>Real on-disk path of a folder (links followed), without trailing backslash; null if unreadable.</summary>
        public static string FinalPath(string folder)
        {
            using (SafeFileHandle h = CreateFileW(folder, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                return Final(h);
            }
        }

        /// <summary>Deletes <paramref name="path"/> (a file, a link, or an empty folder) only if it lies under <paramref name="rootFinal"/>.</summary>
        public static bool Delete(string path, string rootFinal)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rootFinal)) return false;
            using (SafeFileHandle h = CreateFileW(path, DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero,
                                                  OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (h.IsInvalid) return false; // in use, protected, or already gone

                string final = Final(h);
                if (final == null || !final.StartsWith(rootFinal + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Cleanup refused, item resolves outside its folder: " + path + " -> " + (final ?? "?"));
                    return false;
                }

                FILE_BASIC_INFO basic;
                uint basicSize = (uint)Marshal.SizeOf(typeof(FILE_BASIC_INFO));
                if (GetFileInformationByHandleEx(h, FileBasicInfo, out basic, basicSize) && (basic.FileAttributes & FILE_ATTRIBUTE_READONLY) != 0)
                {
                    // Clear read-only through the handle (times left at 0 = unchanged).
                    uint attrs = basic.FileAttributes & ~FILE_ATTRIBUTE_READONLY;
                    var update = new FILE_BASIC_INFO { FileAttributes = attrs == 0 ? FILE_ATTRIBUTE_NORMAL : attrs };
                    SetFileInformationByHandle(h, FileBasicInfo, ref update, basicSize);
                }

                var disp = new FILE_DISPOSITION_INFO { DeleteFile = 1 };
                return SetFileInformationByHandle(h, FileDispositionInfo, ref disp, (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO)));
            }
        }

        private static string Final(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0) return null;
            if (n >= sb.Capacity)
            {
                sb = new StringBuilder((int)n + 1);
                n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return null;
            }
            return sb.ToString().TrimEnd('\\');
        }
    }
}
