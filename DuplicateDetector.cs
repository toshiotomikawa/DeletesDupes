using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace DeleteDuplicates
{
    /// <summary>
    /// An exact-duplicate group: identical length, identical SHA-256 and
    /// verified byte-for-byte equality, ordered deterministically.
    /// </summary>
    internal sealed class DuplicateGroup
    {
        public List<CandidateImage> Ordered;

        public CandidateImage Survivor
        {
            get { return Ordered[Ordered.Count - 1]; }
        }

        public IEnumerable<CandidateImage> Removable
        {
            get { return Ordered.Take(Ordered.Count - 1); }
        }

        public long Length
        {
            get { return Ordered[0].Length; }
        }

        public string Sha256Hex
        {
            get { return DuplicateDetector.ToHex(Ordered[0].Sha256); }
        }
    }

    internal sealed class DuplicateDetector
    {
        public const int BufferSize = 1024 * 1024;

        private readonly OperationLogger _logger;

        public DuplicateDetector(OperationLogger logger)
        {
            _logger = logger;
        }

        public List<DuplicateGroup> FindGroups(List<CandidateImage> candidates)
        {
            var groups = new List<DuplicateGroup>();

            // Stage 1: length. Files with a unique length cannot be identical.
            var bySize = new Dictionary<long, List<CandidateImage>>();

            foreach (CandidateImage candidate in candidates)
            {
                List<CandidateImage> bucket;

                if (!bySize.TryGetValue(candidate.Length, out bucket))
                {
                    bucket = new List<CandidateImage>();
                    bySize[candidate.Length] = bucket;
                }

                bucket.Add(candidate);
            }

            // Hashes are cached by file identity so hard-link aliases and the
            // same path reached twice are only ever read once.
            var hashCache = new Dictionary<FileIdentity, byte[]>();

            foreach (KeyValuePair<long, List<CandidateImage>> sizeGroup in bySize)
            {
                if (sizeGroup.Value.Count < 2)
                {
                    _logger.Counters.UniqueSizeFiles++;
                    continue;
                }

                var hashed = new List<CandidateImage>();

                foreach (CandidateImage candidate in sizeGroup.Value)
                {
                    if (TryHash(candidate, hashCache))
                        hashed.Add(candidate);
                }

                // Stage 2: SHA-256.
                var byHash = new Dictionary<string, List<CandidateImage>>(StringComparer.Ordinal);

                foreach (CandidateImage candidate in hashed)
                {
                    string key = ToHex(candidate.Sha256);

                    List<CandidateImage> bucket;

                    if (!byHash.TryGetValue(key, out bucket))
                    {
                        bucket = new List<CandidateImage>();
                        byHash[key] = bucket;
                    }

                    bucket.Add(candidate);
                }

                foreach (KeyValuePair<string, List<CandidateImage>> hashGroup in byHash)
                {
                    if (hashGroup.Value.Count < 2)
                        continue;

                    // Stage 3: byte-for-byte partitioning. A shared hash is
                    // treated as a strong grouping key, never as proof.
                    foreach (List<CandidateImage> exact in PartitionByContent(hashGroup.Value))
                    {
                        if (exact.Count < 2)
                            continue;

                        groups.Add(new DuplicateGroup { Ordered = Order(exact) });
                    }
                }
            }

            return groups;
        }

        /// <summary>
        /// Depth ascending, then relative path OrdinalIgnoreCase, then relative
        /// path Ordinal. The last element is the survivor: the deepest copy,
        /// and among equal depths the alphabetically last relative path.
        /// </summary>
        public static List<CandidateImage> Order(IEnumerable<CandidateImage> group)
        {
            return group
                .OrderBy(x => x.Depth)
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RelativePath, StringComparer.Ordinal)
                .ToList();
        }

        private bool TryHash(CandidateImage candidate, Dictionary<FileIdentity, byte[]> hashCache)
        {
            int win32Error;
            candidate.Identity = FileIdentity.Read(candidate.FullPath, out win32Error);

            if (!candidate.Identity.IsValid)
            {
                _logger.Counters.MetadataFailures++;

                _logger.Write("ERROR",
                    "path=" + OperationLogger.Quote(candidate.FullPath),
                    "operation=\"Identity\"",
                    "win32=" + win32Error,
                    "message=" + OperationLogger.Quote(NativeMethods.DescribeLastError(win32Error)));
            }
            else
            {
                byte[] cached;

                if (hashCache.TryGetValue(candidate.Identity, out cached))
                {
                    candidate.Sha256 = cached;
                    return true;
                }
            }

            try
            {
                candidate.Sha256 = ComputeSha256(candidate.FullPath);
                _logger.Counters.FilesHashed++;

                if (candidate.Identity.IsValid)
                    hashCache[candidate.Identity] = candidate.Sha256;

                return true;
            }
            catch (Exception exception)
            {
                _logger.Counters.HashFailures++;
                _logger.WriteException("ERROR", candidate.FullPath, "Hash", exception);
                return false;
            }
        }

        public static byte[] ComputeSha256(string path)
        {
            using (var stream = new FileStream(
                PathHelper.Extended(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(stream);
            }
        }

        /// <summary>
        /// Splits a same-hash bucket into clusters that are verified byte-equal.
        /// In practice each bucket produces exactly one cluster; the comparison
        /// exists so that a hash collision cannot cause a deletion.
        /// </summary>
        private List<List<CandidateImage>> PartitionByContent(List<CandidateImage> sameHash)
        {
            var clusters = new List<List<CandidateImage>>();

            foreach (CandidateImage candidate in sameHash)
            {
                bool placed = false;

                foreach (List<CandidateImage> cluster in clusters)
                {
                    bool equal;

                    try
                    {
                        equal = ByteEqual(cluster[0].FullPath, candidate.FullPath);
                    }
                    catch (Exception exception)
                    {
                        _logger.WriteException("ERROR", candidate.FullPath, "ByteCompare", exception);
                        placed = true; // Drop the candidate rather than risk it.
                        break;
                    }

                    if (equal)
                    {
                        cluster.Add(candidate);
                        placed = true;
                        break;
                    }

                    _logger.Write("COLLISION",
                        "first=" + OperationLogger.Quote(cluster[0].FullPath),
                        "second=" + OperationLogger.Quote(candidate.FullPath),
                        "sha256=" + ToHex(candidate.Sha256),
                        "note=\"identical length and SHA-256 but different bytes; not treated as duplicates\"");
                }

                if (!placed)
                    clusters.Add(new List<CandidateImage> { candidate });
            }

            return clusters;
        }

        public static bool ByteEqual(string first, string second)
        {
            using (var a = new FileStream(
                PathHelper.Extended(first), FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var b = new FileStream(
                PathHelper.Extended(second), FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, FileOptions.SequentialScan))
            {
                return ByteEqual(a, b);
            }
        }

        public static bool ByteEqual(Stream a, Stream b)
        {
                a.Position = 0;
                b.Position = 0;
                if (a.Length != b.Length)
                    return false;

                var ab = new byte[BufferSize];
                var bb = new byte[BufferSize];

                while (true)
                {
                    int ar = ReadBlock(a, ab);
                    int br = ReadBlock(b, bb);

                    if (ar != br)
                        return false;

                    if (ar == 0)
                        return true;

                    for (int i = 0; i < ar; i++)
                    {
                        if (ab[i] != bb[i])
                            return false;
                    }
                }
        }

        /// <summary>
        /// FileStream.Read may return fewer bytes than requested even before
        /// end of file, so blocks are filled explicitly before comparison.
        /// </summary>
        private static int ReadBlock(Stream stream, byte[] buffer)
        {
            int total = 0;

            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);

                if (read == 0)
                    break;

                total += read;
            }

            return total;
        }

        public static string ToHex(byte[] value)
        {
            if (value == null)
                return string.Empty;

            var chars = new char[value.Length * 2];

            for (int i = 0; i < value.Length; i++)
            {
                byte b = value[i];
                chars[i * 2] = HexDigit(b >> 4);
                chars[i * 2 + 1] = HexDigit(b & 0xF);
            }

            return new string(chars);
        }

        private static char HexDigit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }
    }
}
