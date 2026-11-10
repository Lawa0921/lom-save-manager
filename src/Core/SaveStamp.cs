using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LoMSaveManager
{
    /// <summary>
    /// A slot's .dat file: length, last-write UTC ticks and the SHA-256 of its bytes (lowercase hex).
    /// Identity across machines is length + SHA-256 (<see cref="Matches"/>): Steam Cloud keeps the bytes but not the
    /// write time. <see cref="WriteTicks"/> is only meaningful on this machine (sorting, the local hash cache, and
    /// telling whether the game just wrote the file).
    /// </summary>
    public struct SaveStamp
    {
        public SaveStamp(long length, long writeTicks, string sha256)
        {
            Length = length;
            WriteTicks = writeTicks;
            Sha256 = sha256;
        }

        public long Length { get; }

        public long WriteTicks { get; }

        public string Sha256 { get; }

        /// <summary>Reads the file's current state and hashes it (no cache). Throws if the file does not exist or cannot be read.</summary>
        public static SaveStamp Compute(FileInfo file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            file.Refresh();
            var length = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;
            return new SaveStamp(length, ticks, ContentHash.Sha256Hex(file.FullName));
        }

        /// <summary>True when the record describes this file: same length and same content hash. Write time is ignored.</summary>
        public bool Matches(SlotRecord record)
        {
            return record != null
                && record.Length == Length
                && Sha256 != null
                && string.Equals(record.Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Same length and write time on this machine (what the game changes when it writes the file).</summary>
        internal bool SameLocalState(SaveStamp other)
        {
            return Length == other.Length && WriteTicks == other.WriteTicks;
        }
    }

    public static class ContentHash
    {
        /// <summary>Attempts <see cref="Sha256Hex"/> makes when the file is briefly locked (another program writing or scanning it).</summary>
        public const int ReadAttempts = 3;

        /// <summary>Pause between those attempts.</summary>
        public const int RetryDelayMilliseconds = 50;

        /// <summary>
        /// SHA-256 of the file's bytes as 64 lowercase hex characters. Opens the file shared, read-only, and retries a
        /// sharing violation or other transient I/O error up to <see cref="ReadAttempts"/> times; a missing file is not retried.
        /// </summary>
        public static string Sha256Hex(string path)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sha = SHA256.Create())
                        return ToHex(sha.ComputeHash(stream));
                }
                catch (IOException ex) when (attempt < ReadAttempts && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException))
                {
                    System.Threading.Thread.Sleep(RetryDelayMilliseconds);
                }
            }
        }

        /// <summary>Whether <paramref name="value"/> looks like what <see cref="Sha256Hex"/> returns.</summary>
        public static bool IsSha256Hex(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
