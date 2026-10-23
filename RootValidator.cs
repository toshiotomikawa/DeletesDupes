using System;
using System.IO;

namespace DeleteDuplicates
{
    internal sealed class ValidatedRoot
    {
        /// <summary>Canonical root, e.g. "D:\Photos" or "C:\".</summary>
        public string Root;

        /// <summary>Root guaranteed to end with a directory separator.</summary>
        public string RootPrefix;

        public bool IsInside(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return false;

            string canonical;

            try
            {
                canonical = Path.GetFullPath(fullPath);
            }
            catch (Exception)
            {
                return false;
            }

            return canonical.Equals(Root, StringComparison.OrdinalIgnoreCase) ||
                   canonical.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public string MakeRelative(string fullPath)
        {
            if (fullPath.Length > RootPrefix.Length &&
                fullPath.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(RootPrefix.Length);
            }

            return fullPath;
        }
    }

    internal static class RootValidator
    {
        /// <summary>
        /// Canonicalises and vets the folder-background argument. Anything that
        /// cannot guarantee a recoverable Recycle Bin move is rejected.
        /// </summary>
        public static ValidatedRoot Validate(string argument, OperationLogger logger, out string rejectionReason)
        {
            rejectionReason = null;

            if (string.IsNullOrEmpty(argument))
            {
                rejectionReason = "no root argument supplied";
                return null;
            }

            // A shell namespace identifier such as "::{20D04FE0-...}" or
            // "shell:MyComputerFolder" is not a filesystem path.
            if (argument.StartsWith("::", StringComparison.Ordinal) ||
                argument.IndexOf("::{", StringComparison.Ordinal) >= 0 ||
                argument.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "argument is a shell namespace identifier, not a filesystem path";
                return null;
            }

            // Explorer supplies a fully qualified drive path. Never resolve
            // relative, drive-relative, UNC or device paths against process cwd.
            if (argument.Length < 3 || !char.IsLetter(argument[0]) ||
                argument[1] != ':' || (argument[2] != '\\' && argument[2] != '/'))
            {
                rejectionReason = "a fully qualified local drive path is required";
                return null;
            }

            string full;

            try
            {
                full = Path.GetFullPath(argument);
            }
            catch (Exception exception)
            {
                rejectionReason = "path could not be canonicalised: " + exception.Message;
                return null;
            }

            if (PathHelper.IsUnc(full))
            {
                rejectionReason = "UNC roots are unsupported; a network share has no recoverable local Recycle Bin";
                return null;
            }

            string root = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // A drive root trims down to "C:" — restore the separator so that
            // the path stays absolute rather than drive-relative.
            if (root.Length == 2 && root[1] == ':')
                root += Path.DirectorySeparatorChar;

            if (root.Length == 0)
            {
                rejectionReason = "path resolved to an empty root";
                return null;
            }

            if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) ||
                PathHelper.IsProtectedDirectory(root) ||
                root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
            {
                rejectionReason = "volume, profile and protected system roots are unsupported";
                return null;
            }

            if (!Directory.Exists(root))
            {
                rejectionReason = "directory does not exist";
                return null;
            }

            if (PathHelper.HasUnsafeAncestor(PathHelper.Join(root, ".root-check")))
            {
                rejectionReason = "root or ancestor is a reparse/offline directory or cannot be inspected";
                return null;
            }

            string driveRoot;

            try
            {
                driveRoot = Path.GetPathRoot(root);
            }
            catch (Exception exception)
            {
                rejectionReason = "drive root could not be determined: " + exception.Message;
                return null;
            }

            if (string.IsNullOrEmpty(driveRoot))
            {
                rejectionReason = "path has no drive root";
                return null;
            }

            uint driveType = NativeMethods.GetDriveTypeW(driveRoot);

            switch (driveType)
            {
                case NativeMethods.DRIVE_FIXED:
                    break;

                case NativeMethods.DRIVE_REMOTE:
                    rejectionReason = "network drives are unsupported; a recoverable Recycle Bin move cannot be guaranteed";
                    return null;

                case NativeMethods.DRIVE_CDROM:
                    rejectionReason = "optical drives are unsupported";
                    return null;

                default:
                    rejectionReason = "unsupported or unknown drive type " + driveType;
                    return null;
            }

            string prefix = root;

            char last = prefix[prefix.Length - 1];

            if (last != Path.DirectorySeparatorChar && last != Path.AltDirectorySeparatorChar)
                prefix += Path.DirectorySeparatorChar;

            if (logger != null)
            {
                logger.Write("ROOT",
                    "path=" + OperationLogger.Quote(root),
                    "driveType=" + driveType);
            }

            return new ValidatedRoot { Root = root, RootPrefix = prefix };
        }
    }
}
