using System;

namespace DeleteDuplicates
{
    internal sealed class CandidateImage
    {
        /// <summary>Absolute, normalised path. Never carries a \\?\ prefix.</summary>
        public string FullPath;

        /// <summary>Path relative to the scan root. Used for deterministic ordering.</summary>
        public string RelativePath;

        /// <summary>Depth of the containing directory; files directly in the root are depth 0.</summary>
        public int Depth;

        public long Length;

        public DateTime LastWriteTimeUtc;

        public FileIdentity Identity;

        public byte[] Sha256;

        public override string ToString()
        {
            return RelativePath + " (depth " + Depth + ", " + Length + " bytes)";
        }
    }
}
