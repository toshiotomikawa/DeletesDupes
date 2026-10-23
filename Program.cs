using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DeleteDuplicates
{
    internal static class Program
    {
        private const string Version = "1.0.0";

        /// <summary>Per-session single-instance mutex. One job at a time.</summary>
        private const string MutexName =
            @"Local\DeleteDuplicates_5A8A08A5_8BD8_43CB_88CC_8A0F6657914B";

        private const int ExitSuccess = 0;
        private const int ExitInvalidRoot = 1;
        private const int ExitNoLog = 2;
        private const int ExitAlreadyRunning = 3;
        private const int ExitUnhandled = 4;

        [STAThread]
        private static int Main(string[] args)
        {
            // .NET Framework 4.8 keeps the legacy MAX_PATH behaviour unless
            // these switches are cleared. Combined with longPathAware in the
            // manifest this allows extended-length paths.
            try
            {
                AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
                AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            }
            catch (Exception)
            {
            }

            // Logging is a safety requirement, not a convenience. Without a log
            // the program refuses to touch anything.
            OperationLogger logger = OperationLogger.TryCreate();

            if (logger == null)
                return ExitNoLog;

            try
            {
                string argument = args.Length > 0 ? args[0] : null;

                string rejection;
                ValidatedRoot root = RootValidator.Validate(argument, null, out rejection);

                logger.WriteHeader(Version, root == null ? string.Empty : root.Root, argument);

                if (args.Length != 1)
                {
                    logger.Write("ABORT",
                        "reason=\"exactly one root argument is required\"",
                        "argumentCount=" + args.Length);

                    logger.WriteSummary();
                    return ExitInvalidRoot;
                }

                if (root == null)
                {
                    logger.Write("ABORT",
                        "reason=" + OperationLogger.Quote(rejection),
                        "argument=" + OperationLogger.Quote(argument));

                    logger.WriteSummary();
                    return ExitInvalidRoot;
                }

                bool createdNew;

                using (var mutex = new Mutex(true, MutexName, out createdNew))
                {
                    if (!createdNew)
                    {
                        logger.Write("ABORT",
                            "reason=\"another DeleteDuplicates run is already active; nothing was enumerated or deleted\"");

                        logger.WriteSummary();
                        return ExitAlreadyRunning;
                    }

                    try
                    {
                        Run(root, logger);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }

                logger.WriteSummary();
                return ExitSuccess;
            }
            catch (Exception exception)
            {
                // Never surface UI. The log is the only channel.
                try
                {
                    logger.WriteException("FATAL", string.Empty, "Run", exception);
                    logger.Write("FATAL", "stack=" + OperationLogger.Quote(exception.StackTrace));
                    logger.WriteSummary();
                }
                catch (Exception)
                {
                }

                return ExitUnhandled;
            }
            finally
            {
                logger.Dispose();
            }
        }

        private static void Run(ValidatedRoot root, OperationLogger logger)
        {
            if (NativeMethods.CloudApiUnavailable)
            {
                logger.Write("NOTE",
                    "detail=\"cldapi.dll is unavailable; cloud placeholder detection falls back to file attributes\"");
            }

            var scanner = new FileTreeScanner(root, logger);
            List<CandidateImage> candidates = scanner.Scan();

            if (candidates.Count == 0)
            {
                logger.Write("RESULT", "detail=\"no image candidates were found\"");
                return;
            }

            var detector = new DuplicateDetector(logger);
            List<DuplicateGroup> groups = detector.FindGroups(candidates);

            if (groups.Count == 0)
            {
                logger.Write("RESULT", "detail=\"no exact duplicate groups were found\"");
                return;
            }

            foreach (DuplicateGroup group in groups)
                ProcessGroup(group, logger);
        }

        private static void ProcessGroup(DuplicateGroup group, OperationLogger logger)
        {
            ProcessGroup(group, logger, RecycleBinMover.RecycleOne);
        }

        internal static void ProcessGroup(DuplicateGroup group, OperationLogger logger,
            Func<string, Func<bool>, RecycleResult> recycle)
        {
            List<CandidateImage> ordered = group.Ordered;

            // Never promote another survivor after the selected one changes.
            int survivorIndex = ordered.Count - 1;
            string survivorReason;
            if (survivorIndex < 1 ||
                !FileTreeScanner.StillEligible(ordered[survivorIndex], out survivorReason) ||
                !HashUnchanged(ordered[survivorIndex], logger))
            {
                logger.Counters.GroupsSkipped++;
                logger.Write("SKIP", "reason=\"selected survivor cannot be validated\"");
                return;
            }

            CandidateImage survivor = ordered[survivorIndex];
            var removable = new List<CandidateImage>();

            for (int i = 0; i < survivorIndex; i++)
                removable.Add(ordered[i]);

            logger.Counters.ExactDuplicateGroups++;
            logger.Counters.RemovableDuplicateInstances += removable.Count;
            logger.Counters.Survivors++;

            logger.Write("GROUP",
                "sha256=" + group.Sha256Hex,
                "length=" + group.Length,
                "instances=" + ordered.Count,
                "survivor=" + OperationLogger.Quote(survivor.FullPath));

            // Conservatively leave hard-linked survivors untouched: removing
            // aliases would require releasing the survivor's no-delete lock.
            if (!survivor.Identity.IsValid || survivor.Identity.LinkCount != 1)
            {
                logger.Counters.GroupsSkipped++;
                logger.Write("SKIP", "reason=\"survivor identity unavailable or hard-linked\"");
                return;
            }

            FileStream protectiveHandle;
            try
            {
                protectiveHandle = new FileStream(PathHelper.Extended(survivor.FullPath),
                    FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            }
            catch (Exception exception)
            {
                logger.Counters.GroupsSkipped++;
                logger.WriteException("SKIP", survivor.FullPath, "SurvivorLock", exception);
                return;
            }

            try
            {
                // Validate again AFTER acquiring the lock, closing the earlier
                // validation-to-open window for the selected survivor.
                string lockedReason;
                if (!FileTreeScanner.StillEligible(survivor, out lockedReason) ||
                    !HashUnchanged(survivor, logger))
                {
                    logger.Counters.GroupsSkipped++;
                    return;
                }
                foreach (CandidateImage candidate in removable)
                {
                    if (!SurvivorStillValid(survivor, protectiveHandle, logger))
                    {
                        logger.Write("SKIP",
                            "sha256=" + group.Sha256Hex,
                            "reason=\"survivor is no longer valid; remaining members of this group were left untouched\"");

                        return;
                    }

                    string reason;

                    if (!FileTreeScanner.StillEligible(candidate, out reason))
                    {
                        logger.Counters.RevalidationSkips++;

                        logger.Write("SKIP",
                            "path=" + OperationLogger.Quote(candidate.FullPath),
                            "reason=" + OperationLogger.Quote(reason));

                        continue;
                    }

                    // Byte equality against the survivor is strictly stronger
                    // than re-hashing, so it is the final gate.
                    bool identical;

                    try
                    {
                        identical = CandidateMatches(candidate, protectiveHandle);
                    }
                    catch (Exception exception)
                    {
                        logger.Counters.RevalidationSkips++;
                        logger.WriteException("SKIP", candidate.FullPath, "ByteCompare", exception);
                        continue;
                    }

                    if (!identical)
                    {
                        logger.Counters.RevalidationSkips++;

                        logger.Write("SKIP",
                            "path=" + OperationLogger.Quote(candidate.FullPath),
                            "reason=\"contents no longer match the survivor\"");

                        continue;
                    }

                    RecycleResult result = recycle(candidate.FullPath, () =>
                    {
                        string currentReason;
                        return SurvivorStillValid(survivor, protectiveHandle, logger) &&
                            FileTreeScanner.StillEligible(survivor, out currentReason) &&
                            FileTreeScanner.StillEligible(candidate, out currentReason) &&
                            CandidateMatches(candidate, protectiveHandle);
                    });

                    if (result.Succeeded)
                    {
                        logger.Counters.MovedToRecycleBin++;

                        logger.Write("MOVED",
                            "source=" + OperationLogger.Quote(candidate.FullPath),
                            "destination=" + OperationLogger.Quote(result.Destination));
                    }
                    else
                    {
                        logger.Counters.RecycleFailures++;

                        logger.Write("ERROR",
                            "path=" + OperationLogger.Quote(candidate.FullPath),
                            "operation=\"Recycle\"",
                            "hresult=" + OperationLogger.FormatHResult(result.HResult),
                            "message=" + OperationLogger.Quote(result.Message));
                    }
                }
            }
            finally
            {
                if (protectiveHandle != null)
                    protectiveHandle.Dispose();
            }

            logger.Write("SURVIVOR",
                "path=" + OperationLogger.Quote(survivor.FullPath),
                "sha256=" + group.Sha256Hex);
        }

        /// <summary>
        /// With a protective handle held the survivor cannot be written to or
        /// deleted, so a length check is sufficient. Without one, the full
        /// SHA-256 is recomputed before every deletion.
        /// </summary>
        private static bool SurvivorStillValid(
            CandidateImage survivor,
            FileStream protectiveHandle,
            OperationLogger logger)
        {
            if (protectiveHandle != null)
            {
                try
                {
                    return protectiveHandle.Length == survivor.Length;
                }
                catch (Exception exception)
                {
                    logger.WriteException("ERROR", survivor.FullPath, "SurvivorCheck", exception);
                    return false;
                }
            }

            string reason;

            if (!FileTreeScanner.StillEligible(survivor, out reason))
            {
                logger.Write("SKIP",
                    "path=" + OperationLogger.Quote(survivor.FullPath),
                    "role=\"survivor\"",
                    "reason=" + OperationLogger.Quote(reason));

                return false;
            }

            return HashUnchanged(survivor, logger);
        }

        private static bool CandidateMatches(CandidateImage candidate, FileStream survivor)
        {
            using (var stream = new FileStream(PathHelper.Extended(candidate.FullPath),
                FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                DuplicateDetector.BufferSize, FileOptions.SequentialScan))
            {
                int error;
                FileIdentity current = FileIdentity.Read(stream.SafeFileHandle.DangerousGetHandle(), out error);
                return current.Equals(candidate.Identity) && DuplicateDetector.ByteEqual(stream, survivor);
            }
        }

        private static bool HashUnchanged(CandidateImage candidate, OperationLogger logger)
        {
            byte[] current;

            try
            {
                current = DuplicateDetector.ComputeSha256(candidate.FullPath);
            }
            catch (Exception exception)
            {
                logger.WriteException("SKIP", candidate.FullPath, "Rehash", exception);
                return false;
            }

            if (candidate.Sha256 == null || current.Length != candidate.Sha256.Length)
                return false;

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != candidate.Sha256[i])
                {
                    logger.Write("SKIP",
                        "path=" + OperationLogger.Quote(candidate.FullPath),
                        "reason=\"contents changed since the scan\"");

                    return false;
                }
            }

            return true;
        }
    }
}
