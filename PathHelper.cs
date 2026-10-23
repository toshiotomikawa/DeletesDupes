using System;
using System.IO;

namespace DeleteDuplicates
{
    /// <summary>
    /// Path utilities. .NET Framework 4.8 still applies MAX_PATH rules to many
    /// managed APIs, so every raw file open goes through <see cref="Extended"/>.
    /// </summary>
    internal static class PathHelper
    {
        private const string DevicePrefix = @"\\?\";
        private const string UncDevicePrefix = @"\\?\UNC\";

        /// <summary>
        /// Returns a \\?\-prefixed path suitable for CreateFileW / FileStream.
        /// The extended form disables path normalisation, so the input must
        /// already be fully qualified and normalised.
        /// </summary>
        public static string Extended(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return fullPath;

            if (fullPath.StartsWith(DevicePrefix, StringComparison.Ordinal) ||
                fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return fullPath;
            }

            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return UncDevicePrefix + fullPath.Substring(2);

            return DevicePrefix + fullPath;
        }

        public static bool IsUnc(string fullPath)
        {
            return !string.IsNullOrEmpty(fullPath) &&
                   fullPath.StartsWith(@"\\", StringComparison.Ordinal) &&
                   !fullPath.StartsWith(DevicePrefix, StringComparison.Ordinal);
        }

        public static bool IsProtectedDirectory(string path)
        {
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase))
                return true;
            string[] protectedRoots = {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            return Array.Exists(protectedRoots, root => !string.IsNullOrEmpty(root) &&
                (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)));
        }

        // Path-based Shell operations are not an adversarial filesystem
        // sandbox. Reject existing reparse ancestors as conservative protection.
        public static bool HasUnsafeAncestor(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(directory))
                {
                    FileAttributes attributes = File.GetAttributes(Extended(directory));
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                        return true;
                    string parent = Path.GetDirectoryName(directory);
                    if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                        break;
                    directory = parent;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Joins a directory and a child name without invoking Path.Combine's
        /// rooted-second-argument behaviour.
        /// </summary>
        public static string Join(string directory, string name)
        {
            if (directory.Length == 0)
                return name;

            char last = directory[directory.Length - 1];

            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                return directory + name;

            return directory + Path.DirectorySeparatorChar + name;
        }
    }
}
