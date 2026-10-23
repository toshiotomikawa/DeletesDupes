using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DeleteDuplicates
{
    /// <summary>
    /// Manual depth-first traversal of the root subtree. Reparse points are
    /// never followed and every enumerated path is re-checked against the root
    /// boundary as defence in depth.
    /// </summary>
    internal sealed class FileTreeScanner
    {
        internal static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".jpe", ".jfif",
                ".png", ".apng",
                ".bmp", ".dib",
                ".gif",
                ".tif", ".tiff",
                ".webp",
                ".heic", ".heif",
                ".avif",
                ".ico"
            };

        private readonly ValidatedRoot _root;
        private readonly OperationLogger _logger;

        public FileTreeScanner(ValidatedRoot root, OperationLogger logger)
        {
            _root = root;
            _logger = logger;
        }

        private struct PendingDirectory
        {
            public string Path;
            public int Depth;

            public PendingDirectory(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }
        }

        public List<CandidateImage> Scan()
        {
            var candidates = new List<CandidateImage>();
            var stack = new Stack<PendingDirectory>();

            stack.Push(new PendingDirectory(_root.Root, 0));

            _logger.Write("SCAN", "path=" + OperationLogger.Quote(_root.Root));

            while (stack.Count > 0)
            {
                PendingDirectory current = stack.Pop();

                ScanDirectory(current, candidates, stack);
            }

            return candidates;
        }

        private void ScanDirectory(
            PendingDirectory directory,
            List<CandidateImage> candidates,
            Stack<PendingDirectory> stack)
        {
            if (PathHelper.HasUnsafeAncestor(PathHelper.Join(directory.Path, ".directory-check")))
            {
                _logger.Counters.ReparseEntriesSkipped++;
                return;
            }

            string pattern = PathHelper.Join(PathHelper.Extended(directory.Path), "*");

            WIN32_FIND_DATAW data;
            IntPtr handle = NativeMethods.FindFirst(pattern, out data);

            if (handle == NativeMethods.InvalidHandleValue)
            {
                int error = Marshal.GetLastWin32Error();

                if (error != NativeMethods.ERROR_NO_MORE_FILES)
                {
                    _logger.Counters.DirectoriesFailed++;

                    _logger.Write("ERROR",
                        "path=" + OperationLogger.Quote(directory.Path),
                        "operation=\"Enumerate\"",
                        "win32=" + error,
                        "message=" + OperationLogger.Quote(NativeMethods.DescribeLastError(error)));
                }

                return;
            }

            _logger.Counters.DirectoriesVisited++;

            // Children are pushed in reverse so that the stack pops them in
            // name order. Ordering is cosmetic here; the deletion order is
            // established later by the deterministic sort.
            var childDirectories = new List<PendingDirectory>();

            try
            {
                do
                {
                    string name = data.cFileName;

                    if (name == "." || name == "..")
                        continue;

                    string childPath = PathHelper.Join(directory.Path, name);
                    bool isDirectory = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    bool isReparse = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                    uint reparseTag = isReparse ? data.dwReserved0 : 0;

                    if (isReparse)
                    {
                        uint state = NativeMethods.GetPlaceholderState(data.dwFileAttributes, reparseTag);
                        bool isPlaceholder =
                            state != NativeMethods.CF_PLACEHOLDER_STATE_INVALID &&
                            (state & NativeMethods.CF_PLACEHOLDER_STATE_PLACEHOLDER) != 0;

                        if (!isPlaceholder)
                        {
                            // Junction, mount point or symbolic link. Never
                            // entered: it can escape the root or form a loop,
                            // and deleting a link is not deleting a duplicate.
                            _logger.Counters.ReparseEntriesSkipped++;

                            _logger.Write("SKIP",
                                "path=" + OperationLogger.Quote(childPath),
                                "reason=" + OperationLogger.Quote(isDirectory
                                    ? "directory reparse point; subtree not entered"
                                    : "file reparse point"),
                                "reparseTag=0x" + reparseTag.ToString("X8"));

                            continue;
                        }

                        if (IsNotFullyLocal(state))
                        {
                            _logger.Counters.CloudPlaceholdersSkipped++;

                            _logger.Write("SKIP",
                                "path=" + OperationLogger.Quote(childPath),
                                "reason=" + OperationLogger.Quote(isDirectory
                                    ? "cloud placeholder directory is not fully present locally; subtree not entered"
                                    : "cloud placeholder is not fully present locally"),
                                "placeholderState=0x" + state.ToString("X8"));

                            continue;
                        }
                    }
                    else if (HasOfflineAttribute(data.dwFileAttributes))
                    {
                        // Fallback for providers that do not use the Cloud
                        // Files API, and for pre-1709 systems.
                        _logger.Counters.CloudPlaceholdersSkipped++;

                        _logger.Write("SKIP",
                            "path=" + OperationLogger.Quote(childPath),
                            "reason=\"contents are not fully present locally (offline or recall-on-access attribute)\"",
                            "attributes=0x" + data.dwFileAttributes.ToString("X8"));

                        continue;
                    }

                    if (!_root.IsInside(childPath))
                    {
                        _logger.Write("SKIP",
                            "path=" + OperationLogger.Quote(childPath),
                            "reason=\"canonical path falls outside the scan root\"");

                        continue;
                    }

                    if (isDirectory)
                    {
                        if (PathHelper.IsProtectedDirectory(childPath))
                            continue;
                        childDirectories.Add(new PendingDirectory(childPath, directory.Depth + 1));
                        continue;
                    }

                    if (!ImageExtensions.Contains(Path.GetExtension(name)))
                        continue;

                    candidates.Add(new CandidateImage
                    {
                        FullPath = childPath,
                        RelativePath = _root.MakeRelative(childPath),
                        Depth = directory.Depth,
                        Length = data.Length,
                        LastWriteTimeUtc = ToUtc(data.ftLastWriteTime)
                    });

                    _logger.Counters.ImageCandidates++;
                }
                while (NativeMethods.FindNext(handle, out data));
            }
            finally
            {
                NativeMethods.FindClose(handle);
            }

            for (int i = childDirectories.Count - 1; i >= 0; i--)
                stack.Push(childDirectories[i]);
        }

        private static bool IsNotFullyLocal(uint placeholderState)
        {
            return (placeholderState & NativeMethods.CF_PLACEHOLDER_STATE_PARTIAL) != 0 ||
                   (placeholderState & NativeMethods.CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK) != 0;
        }

        private static bool HasOfflineAttribute(uint attributes)
        {
            return (attributes & NativeMethods.FILE_ATTRIBUTE_OFFLINE) != 0 ||
                   (attributes & NativeMethods.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 ||
                   (attributes & NativeMethods.FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0;
        }

        private static DateTime ToUtc(FILETIME fileTime)
        {
            try
            {
                return DateTime.FromFileTimeUtc(fileTime.ToLong());
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Re-reads the live state of a path immediately before deletion.
        /// Returns false when the entry is gone, has become a reparse point, is
        /// no longer fully local, or has changed size.
        /// </summary>
        public static bool StillEligible(CandidateImage candidate, out string reason)
        {
            reason = null;
            if (PathHelper.HasUnsafeAncestor(candidate.FullPath))
            {
                reason = "ancestor became reparse/offline or cannot be inspected";
                return false;
            }

            IntPtr handle = NativeMethods.OpenMetadataHandle(candidate.FullPath, false, true);

            if (handle == NativeMethods.InvalidHandleValue)
            {
                int error = Marshal.GetLastWin32Error();
                reason = "metadata handle could not be opened (" +
                         NativeMethods.DescribeLastError(error) + ")";
                return false;
            }

            try
            {
                FILE_ATTRIBUTE_TAG_INFO info;

                if (!NativeMethods.TryGetAttributeTagInfo(handle, out info))
                {
                    reason = "attribute/tag information could not be read";
                    return false;
                }

                bool isReparse = (info.FileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                if (isReparse)
                {
                    uint state = NativeMethods.GetPlaceholderState(info.FileAttributes, info.ReparseTag);
                    bool isPlaceholder =
                        state != NativeMethods.CF_PLACEHOLDER_STATE_INVALID &&
                        (state & NativeMethods.CF_PLACEHOLDER_STATE_PLACEHOLDER) != 0;

                    if (!isPlaceholder)
                    {
                        reason = "path is now a reparse point";
                        return false;
                    }

                    if (IsNotFullyLocal(state))
                    {
                        reason = "cloud placeholder is no longer fully present locally";
                        return false;
                    }
                }
                else if (HasOfflineAttribute(info.FileAttributes))
                {
                    reason = "contents are no longer fully present locally";
                    return false;
                }
                int identityError;
                FileIdentity identity = FileIdentity.Read(handle, out identityError);
                BY_HANDLE_FILE_INFORMATION basic;
                if (!candidate.Identity.IsValid || !identity.Equals(candidate.Identity) ||
                    !NativeMethods.TryGetBasicInfo(handle, out basic))
                {
                    reason = "file identity unavailable or changed since scan";
                    return false;
                }
                long length = ((long)basic.nFileSizeHigh << 32) | basic.nFileSizeLow;
                if (length != candidate.Length || ToUtc(basic.ftLastWriteTime) != candidate.LastWriteTimeUtc ||
                    (basic.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0)
                {
                    reason = "file size, write time or type changed since scan";
                    return false;
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }

            return true;
        }
    }
}
