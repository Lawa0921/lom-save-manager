using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LoMSaveManager
{
    /// <summary>
    /// Reads and writes the metadata file. Load never throws for a bad file: it quarantines it
    /// (&lt;name&gt;.corrupt-&lt;UTC ticks&gt; in the work directory) and starts empty, so the game itself is never affected.
    /// A file from a newer version, or one larger than <see cref="MaxFileBytes"/>, is left exactly as it is and never overwritten
    /// by this store (moving it aside would propagate a delete through Steam Cloud). The store never writes a file larger
    /// than it would read back either, so the mod cannot lock itself out with data it wrote.
    /// The temp file of an atomic write also lives in the work directory, so the synced folder only ever holds the file itself.
    /// </summary>
    public sealed class SaveIndexStore
    {
        /// <summary>
        /// Larger files are neither read nor written. The largest file the mod's own rules allow (all 9999 manual slots
        /// named to the 40-character limit and filed, 200 categories with 24-character names, every character at its
        /// costliest JSON escape) is about 4.2 MB, while a typical named save takes about 200 bytes; so a bigger file is
        /// damaged or hostile, and parsing it could stall the game. A test keeps that worst case under three quarters of this.
        /// </summary>
        public const int MaxFileBytes = 8 * 1024 * 1024;

        private readonly string _path;
        private readonly string _workDirectory;
        private readonly string _fileName;
        private readonly int _maxFileBytes;

        /// <param name="path">Full path to the metadata file.</param>
        /// <param name="workDirectory">Where the temp file and quarantined copies go; null means the file's own directory.
        /// Must be on the same volume as <paramref name="path"/>.</param>
        public SaveIndexStore(string path, string workDirectory = null)
            : this(path, workDirectory, MaxFileBytes)
        {
        }

        /// <param name="maxFileBytes">The read cap (tests use a small one); the mod itself always uses <see cref="MaxFileBytes"/>.</param>
        internal SaveIndexStore(string path, string workDirectory, int maxFileBytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            if (maxFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            _maxFileBytes = maxFileBytes;
            _path = path;
            _fileName = Path.GetFileName(path);
            _workDirectory = string.IsNullOrEmpty(workDirectory) ? Path.GetDirectoryName(path) : workDirectory;
        }

        /// <summary>Why the last <see cref="Load"/> started empty instead of reading the file; null when it read fine (or the file simply did not exist).</summary>
        public string LastLoadProblem { get; private set; }

        /// <summary>
        /// False after a <see cref="Load"/> that started empty while the original file is still in place (it was
        /// locked or unreadable, it is from a newer version, or a bad file could not be moved aside). Saving then would
        /// overwrite metadata this session never saw, so callers must not call <see cref="Save"/> until a later Load reads cleanly.
        /// </summary>
        public bool CanSave { get; private set; } = true;

        /// <summary>True when the last <see cref="Load"/> left the file alone because a newer version of the mod wrote it.</summary>
        public bool LoadedNewerVersion { get; private set; }

        /// <summary>
        /// True after a <see cref="Save"/> was refused because the file would have been larger than this store reads back.
        /// The previous file is kept and <see cref="CanSave"/> stays false until the next <see cref="Load"/>.
        /// </summary>
        public bool RefusedOversizedWrite { get; private set; }

        public SaveIndex Load()
        {
            LastLoadProblem = null;
            CanSave = true;
            LoadedNewerVersion = false;
            RefusedOversizedWrite = false;

            byte[] bytes;
            try
            {
                if (!File.Exists(_path)) return new SaveIndex();
                bytes = JsonFile.ReadAllBytesUpTo(_path, _maxFileBytes);
            }
            catch (FileNotFoundException) { return new SaveIndex(); }
            catch (DirectoryNotFoundException) { return new SaveIndex(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Locked or unreadable: leave the file alone (it may be fine) and run without metadata.
                LastLoadProblem = _fileName + " could not be read: " + ex.Message;
                CanSave = false;
                return new SaveIndex();
            }

            if (bytes == null)
            {
                // Not quarantined: moving the synced file would delete it from Steam Cloud and every other machine.
                LastLoadProblem = _fileName + " is larger than " + JsonFile.Megabytes(_maxFileBytes) + " and was not read; left untouched, "
                    + "names and categories are not shown or saved this session (check the file, or move it away to start fresh)";
                CanSave = false;
                return new SaveIndex();
            }

            string problem;
            bool newer;
            var index = TryParse(bytes, out problem, out newer);
            if (index != null) return index;

            if (newer)
            {
                // A newer mod wrote it (maybe on another machine). Overwriting or moving it would erase that data
                // here and, through Steam Cloud, everywhere else; so run without metadata and leave it alone.
                LastLoadProblem = problem + "; left untouched, names and categories are not shown or saved this session";
                CanSave = false;
                LoadedNewerVersion = true;
                return new SaveIndex();
            }

            string note;
            CanSave = Quarantine(out note);
            LastLoadProblem = problem + note;
            return new SaveIndex();
        }

        /// <summary>
        /// Writes the file atomically (temp file in the work directory, then replace). Does nothing when the file already
        /// holds exactly these bytes, so an unchanged index is not re-uploaded by Steam Cloud.
        /// Throws <see cref="InvalidOperationException"/> while <see cref="CanSave"/> is false. Content larger than the read cap
        /// is not written at all: the previous file stays, <see cref="CanSave"/> becomes false and
        /// <see cref="IndexTooLargeException"/> (an <see cref="IOException"/>) says why.
        /// </summary>
        public void Save(SaveIndex index)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            // Defense in depth: SaveSession checks CanSave first; this store must never overwrite a file it could not read.
            if (!CanSave) throw new InvalidOperationException(_fileName + " was not read cleanly by this store; refusing to overwrite it.");

            index.Version = SaveIndex.CurrentVersion;
            var bytes = JsonFile.Write(index);

            if (bytes.Length > _maxFileBytes)
            {
                // Written, it could never be read back: every later start would be read-only. Keep the last good file.
                CanSave = false;
                RefusedOversizedWrite = true;
                throw new IndexTooLargeException(_fileName + " would be " + bytes.Length + " bytes, more than the "
                    + JsonFile.Megabytes(_maxFileBytes) + " (" + _maxFileBytes + " bytes) this mod reads back; it was not written and the previous file is kept");
            }

            if (SameAsOnDisk(bytes)) return;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Directory.CreateDirectory(_workDirectory);

            var tmp = Path.Combine(_workDirectory, _fileName + ".tmp");
            try
            {
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        private bool SameAsOnDisk(byte[] bytes)
        {
            try
            {
                var existing = new FileInfo(_path);
                if (!existing.Exists || existing.Length != bytes.Length) return false;
                return File.ReadAllBytes(_path).SequenceEqual(bytes);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false; // let the write itself report the problem
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private SaveIndex TryParse(byte[] bytes, out string problem, out bool newer)
        {
            newer = false;
            var index = JsonFile.TryRead<SaveIndex>(bytes, _fileName, out problem);
            if (index == null)
            {
                // A newer version may have reshaped other members so that the v2 contract cannot read them;
                // its Version alone still says it is newer, and then it must be left alone, not quarantined.
                string ignored;
                var probe = JsonFile.TryRead<VersionProbe>(bytes, _fileName, out ignored);
                if (probe != null && probe.Version > SaveIndex.CurrentVersion)
                {
                    problem = _fileName + " is from a newer version (" + probe.Version + ")";
                    newer = true;
                }
                return null;
            }

            if (index.Version > SaveIndex.CurrentVersion)
            {
                problem = _fileName + " is from a newer version (" + index.Version + ")";
                newer = true;
                return null;
            }

            index.Normalize();
            return index;
        }

        /// <summary>Renames the bad file into the work directory. Returns whether it is gone; <paramref name="note"/> is appended to the problem text.</summary>
        private bool Quarantine(out string note)
        {
            try
            {
                Directory.CreateDirectory(_workDirectory);
                var stem = Path.Combine(_workDirectory, _fileName + ".corrupt-" + DateTime.UtcNow.Ticks);
                var target = stem;
                for (var n = 1; File.Exists(target); n++)
                    target = stem + "-" + n;
                File.Move(_path, target);
                note = "; moved to " + Path.GetFileName(target);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                note = "; could not be moved aside (" + ex.Message + ")";
                return false;
            }
        }
    }

    /// <summary>Thrown by <see cref="SaveIndexStore.Save"/> instead of writing a file larger than the store would read back.</summary>
    public sealed class IndexTooLargeException : IOException
    {
        public IndexTooLargeException(string message) : base(message)
        {
        }
    }

    /// <summary>Only the Version of a metadata file, read when the full v2 contract cannot parse it.</summary>
    [DataContract]
    internal sealed class VersionProbe
    {
        [DataMember] public int Version { get; set; }
    }

    /// <summary>The JSON reading rules shared by the metadata file, the v1 index and the local cache.</summary>
    internal static class JsonFile
    {
        private static readonly DataContractJsonSerializerSettings Settings =
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        /// <summary>Parses a JSON object of type T; null (with <paramref name="problem"/>) for anything else.</summary>
        public static T TryRead<T>(byte[] bytes, string fileName, out string problem) where T : class
        {
            problem = null;

            // The serializer rejects a UTF-8 BOM (Notepad adds one) and quietly accepts a JSON array
            // as an empty object, so strip the BOM and require a real object ourselves.
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (FirstSignificantByte(bytes, start) != (byte)'{')
            {
                problem = fileName + " is corrupt (not a JSON object)";
                return null;
            }

            T value;
            try
            {
                using (var stream = new MemoryStream(bytes, start, bytes.Length - start))
                    value = new DataContractJsonSerializer(typeof(T), Settings).ReadObject(stream) as T;
            }
            catch (Exception ex)
            {
                // Deserialization surfaces many exception types (SerializationException, XmlException, ...).
                problem = fileName + " is corrupt (" + ex.GetType().Name + ")";
                return null;
            }

            if (value == null) problem = fileName + " is corrupt (no object)";
            return value;
        }

        /// <summary>
        /// The file's bytes, or null when it is longer than <paramref name="maxBytes"/> (then nothing is read).
        /// Throws like <see cref="File.ReadAllBytes"/> when it is missing or unreadable.
        /// </summary>
        public static byte[] ReadAllBytesUpTo(string path, int maxBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = stream.Length;
                if (length > maxBytes) return null;
                var bytes = new byte[length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = stream.Read(bytes, read, bytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read == bytes.Length) return bytes;
                var shorter = new byte[read];
                Array.Copy(bytes, shorter, read);
                return shorter;
            }
        }

        /// <summary>"1 MB" for 1048576: limits as the log states them.</summary>
        public static string Megabytes(int bytes)
        {
            return (bytes / (1024 * 1024)) + " MB";
        }

        public static byte[] Write<T>(T value)
        {
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T), Settings).WriteObject(buffer, value);
                return buffer.ToArray();
            }
        }

        private static int FirstSignificantByte(byte[] bytes, int start)
        {
            for (var i = start; i < bytes.Length; i++)
            {
                var b = bytes[i];
                if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n') return b;
            }
            return -1;
        }
    }
}
