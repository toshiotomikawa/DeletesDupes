using System;

namespace DeleteDuplicates
{
    /// <summary>
    /// Volume serial number plus 128-bit file identifier. Two paths with the
    /// same identity are the same underlying filesystem object, i.e. hard-link
    /// aliases of one another.
    /// </summary>
    internal struct FileIdentity : IEquatable<FileIdentity>
    {
        public readonly ulong VolumeSerialNumber;
        public readonly ulong IdLow;
        public readonly ulong IdHigh;
        public readonly uint LinkCount;
        public readonly bool IsValid;

        private FileIdentity(ulong volumeSerialNumber, ulong idLow, ulong idHigh, uint linkCount)
        {
            VolumeSerialNumber = volumeSerialNumber;
            IdLow = idLow;
            IdHigh = idHigh;
            LinkCount = linkCount;
            IsValid = true;
        }

        /// <summary>
        /// Reads the identity of an existing file. Returns an invalid identity
        /// when the handle cannot be opened; callers then treat the path as
        /// having no known alias.
        /// </summary>
        public static FileIdentity Read(string fullPath, out int win32Error)
        {
            win32Error = 0;

            IntPtr handle = NativeMethods.OpenMetadataHandle(fullPath, false, true);

            if (handle == NativeMethods.InvalidHandleValue)
            {
                win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                return default(FileIdentity);
            }

            try
            {
                return Read(handle, out win32Error);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        public static FileIdentity Read(IntPtr handle, out int win32Error)
        {
            win32Error = 0;
                ulong volume;
                byte[] id;
                uint links;

                if (!NativeMethods.TryGetFileIdentity(handle, out volume, out id, out links) || id == null)
                {
                    win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    return default(FileIdentity);
                }

                ulong low = BitConverter.ToUInt64(id, 0);
                ulong high = BitConverter.ToUInt64(id, 8);

                return new FileIdentity(volume, low, high, links);
        }

        public bool Equals(FileIdentity other)
        {
            return IsValid && other.IsValid &&
                   VolumeSerialNumber == other.VolumeSerialNumber &&
                   IdLow == other.IdLow &&
                   IdHigh == other.IdHigh;
        }

        public override bool Equals(object obj)
        {
            return obj is FileIdentity && Equals((FileIdentity)obj);
        }

        public override int GetHashCode()
        {
            if (!IsValid)
                return 0;

            ulong mixed = VolumeSerialNumber ^ IdLow ^ (IdHigh * 31);
            return (int)(mixed ^ (mixed >> 32));
        }

        public override string ToString()
        {
            if (!IsValid)
                return "invalid";

            return VolumeSerialNumber.ToString("X") + ":" +
                   IdHigh.ToString("X16") + IdLow.ToString("X16");
        }
    }
}
