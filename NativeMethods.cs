using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeleteDuplicates
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public long ToLong()
        {
            return ((long)dwHighDateTime << 32) | dwLowDateTime;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;

        /// <summary>Reparse tag when dwFileAttributes has FILE_ATTRIBUTE_REPARSE_POINT.</summary>
        public uint dwReserved0;
        public uint dwReserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;

        public long Length
        {
            get { return ((long)nFileSizeHigh << 32) | nFileSizeLow; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ID_128
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Identifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public FILE_ID_128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    internal static class NativeMethods
    {
        // ---- File attributes -------------------------------------------------
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
        public const uint FILE_ATTRIBUTE_OFFLINE = 0x00001000;
        public const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
        public const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;

        // ---- Cloud Files placeholder states ---------------------------------
        public const uint CF_PLACEHOLDER_STATE_NO_STATES = 0x00000000;
        public const uint CF_PLACEHOLDER_STATE_PLACEHOLDER = 0x00000001;
        public const uint CF_PLACEHOLDER_STATE_SYNC_ROOT = 0x00000002;
        public const uint CF_PLACEHOLDER_STATE_ESSENTIAL_PROP_PRESENT = 0x00000004;
        public const uint CF_PLACEHOLDER_STATE_IN_SYNC = 0x00000008;
        public const uint CF_PLACEHOLDER_STATE_PARTIAL = 0x00000010;
        public const uint CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK = 0x00000020;
        public const uint CF_PLACEHOLDER_STATE_INVALID = 0xFFFFFFFF;

        // ---- Drive types -----------------------------------------------------
        public const uint DRIVE_UNKNOWN = 0;
        public const uint DRIVE_NO_ROOT_DIR = 1;
        public const uint DRIVE_REMOVABLE = 2;
        public const uint DRIVE_FIXED = 3;
        public const uint DRIVE_REMOTE = 4;
        public const uint DRIVE_CDROM = 5;
        public const uint DRIVE_RAMDISK = 6;

        // ---- CreateFile ------------------------------------------------------
        public const uint FILE_READ_ATTRIBUTES = 0x0080;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint FILE_SHARE_DELETE = 0x00000004;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        public const int ERROR_NO_MORE_FILES = 18;
        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_PATH_NOT_FOUND = 3;
        public const int ERROR_ACCESS_DENIED = 5;

        // GetFileInformationByHandleEx classes
        private const int FileAttributeTagInfo = 9;
        private const int FileIdInfo = 18;

        private const int FindExInfoBasic = 1;
        private const int FindExSearchNameMatch = 0;
        private const int FIND_FIRST_EX_LARGE_FETCH = 2;

        public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExW(
            string lpFileName,
            int fInfoLevelId,
            out WIN32_FIND_DATAW lpFindFileData,
            int fSearchOp,
            IntPtr lpSearchFilter,
            int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            IntPtr hFile,
            int fileInformationClass,
            IntPtr lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            IntPtr hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetDriveTypeW(string lpRootPathName);

        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint CfGetPlaceholderStateFromAttributeTag(
            uint fileAttributes,
            uint reparseTag);

        // ---------------------------------------------------------------------

        public static IntPtr FindFirst(string searchPattern, out WIN32_FIND_DATAW data)
        {
            return FindFirstFileExW(
                searchPattern,
                FindExInfoBasic,
                out data,
                FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);
        }

        public static bool FindNext(IntPtr handle, out WIN32_FIND_DATAW data)
        {
            return FindNextFileW(handle, out data);
        }

        private static int _cloudApiState; // 0 = unknown, 1 = available, 2 = missing

        /// <summary>
        /// Placeholder state for the given attributes/reparse tag, or
        /// CF_PLACEHOLDER_STATE_INVALID when the Cloud Files API is unavailable
        /// (pre-1709) or the query fails.
        /// </summary>
        public static uint GetPlaceholderState(uint fileAttributes, uint reparseTag)
        {
            if (_cloudApiState == 2)
                return CF_PLACEHOLDER_STATE_INVALID;

            try
            {
                uint state = CfGetPlaceholderStateFromAttributeTag(fileAttributes, reparseTag);
                _cloudApiState = 1;
                return state;
            }
            catch (DllNotFoundException)
            {
                _cloudApiState = 2;
            }
            catch (EntryPointNotFoundException)
            {
                _cloudApiState = 2;
            }

            return CF_PLACEHOLDER_STATE_INVALID;
        }

        public static bool CloudApiUnavailable
        {
            get { return _cloudApiState == 2; }
        }

        /// <summary>
        /// Opens a metadata-only handle. <paramref name="openReparsePoint"/>
        /// keeps a dehydrated cloud placeholder from being hydrated.
        /// </summary>
        public static IntPtr OpenMetadataHandle(string fullPath, bool isDirectory, bool openReparsePoint)
        {
            uint flags = FILE_FLAG_BACKUP_SEMANTICS;

            if (openReparsePoint)
                flags |= FILE_FLAG_OPEN_REPARSE_POINT;

            return CreateFileW(
                PathHelper.Extended(fullPath),
                FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                flags,
                IntPtr.Zero);
        }

        public static bool TryGetAttributeTagInfo(IntPtr handle, out FILE_ATTRIBUTE_TAG_INFO info)
        {
            info = new FILE_ATTRIBUTE_TAG_INFO();

            int size = Marshal.SizeOf(typeof(FILE_ATTRIBUTE_TAG_INFO));
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, buffer, (uint)size))
                    return false;

                info = (FILE_ATTRIBUTE_TAG_INFO)Marshal.PtrToStructure(buffer, typeof(FILE_ATTRIBUTE_TAG_INFO));
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// 128-bit file identity (works on ReFS as well as NTFS). Falls back to
        /// the legacy 64-bit index when FileIdInfo is not supported.
        /// </summary>
        public static bool TryGetFileIdentity(IntPtr handle, out ulong volumeSerial, out byte[] fileId, out uint linkCount)
        {
            volumeSerial = 0;
            fileId = null;
            linkCount = 0;

            int size = Marshal.SizeOf(typeof(FILE_ID_INFO));
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (GetFileInformationByHandleEx(handle, FileIdInfo, buffer, (uint)size))
                {
                    FILE_ID_INFO idInfo =
                        (FILE_ID_INFO)Marshal.PtrToStructure(buffer, typeof(FILE_ID_INFO));

                    volumeSerial = idInfo.VolumeSerialNumber;
                    fileId = idInfo.FileId.Identifier;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            BY_HANDLE_FILE_INFORMATION legacy;

            if (!GetFileInformationByHandle(handle, out legacy))
                return false;

            linkCount = legacy.nNumberOfLinks;

            if (fileId == null)
            {
                volumeSerial = legacy.dwVolumeSerialNumber;

                fileId = new byte[16];
                BitConverter.GetBytes(legacy.nFileIndexLow).CopyTo(fileId, 0);
                BitConverter.GetBytes(legacy.nFileIndexHigh).CopyTo(fileId, 4);
            }

            return true;
        }

        public static bool TryGetBasicInfo(IntPtr handle, out BY_HANDLE_FILE_INFORMATION info)
        {
            return GetFileInformationByHandle(handle, out info);
        }

        public static string DescribeLastError(int error)
        {
            try
            {
                return new Win32Exception(error).Message;
            }
            catch (Exception)
            {
                return "Win32 error " + error;
            }
        }
    }
}
