using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace LoMSaveManager
{
    /// <summary>
    /// Per-machine hash cache (LoMSaveManager/local-cache.json, in the subfolder Steam Cloud does not sync):
    /// slot key -> (length, write ticks, SHA-256). A hash is reused only while the file's length and write ticks are
    /// exactly what they were when it was computed; otherwise the file is hashed again. A missing, unreadable or corrupt
    /// cache simply means every file is hashed again: it never affects the synced metadata.
    /// </summary>
    public sealed class LocalStampCache
    {
        public const int CurrentVersion = 1;

        private readonly string _path;
        private readonly Dictionary<string, LocalCacheEntry> _entries = new Dictionary<string, LocalCacheEntry>(StringComparer.Ordinal);
        private bool _dirty;

        public LocalStampCache(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            _path = path;
            Load();
        }

        /// <summary>Why the cache file was ignored at construction; null when it read fine or did not exist.</summary>
        public string LoadProblem { get; private set; }

        /// <summary>How many files this instance has hashed (cache misses).</summary>
        public int Computed { get; private set; }

        /// <summary>
        /// The file's stamp, using the cached hash when length and write ticks are unchanged, hashing it otherwise.
        /// Throws like <see cref="SaveStamp.Compute"/> when the file is missing or unreadable.
        /// </summary>
        public SaveStamp Resolve(string key, FileInfo file)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (file == null) throw new ArgumentNullException(nameof(file));

            file.Refresh();
            var length = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;

            LocalCacheEntry entry;
            if (_entries.TryGetValue(key, out entry) && entry.Length == length && entry.WriteTicks == ticks)
                return new SaveStamp(length, ticks, entry.Sha256);

            var sha = ContentHash.Sha256Hex(file.FullName);
            Computed++;
            _entries[key] = new LocalCacheEntry { Length = length, WriteTicks = ticks, Sha256 = sha };
            _dirty = true;
            return new SaveStamp(length, ticks, sha);
        }

        /// <summary>Forgets the key's hash, so the next <see cref="Resolve"/> reads the file (used right after the game writes it).</summary>
        public void Invalidate(string key)
        {
            if (key != null && _entries.Remove(key)) _dirty = true;
        }

        /// <summary>Drops the entries of every key not in <paramref name="keys"/> (saves that no longer exist).</summary>
        public void Retain(IEnumerable<string> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var keep = new HashSet<string>(keys, StringComparer.Ordinal);
            var gone = new List<string>();
            foreach (var key in _entries.Keys)
                if (!keep.Contains(key)) gone.Add(key);
            foreach (var key in gone) _entries.Remove(key);
            if (gone.Count > 0) _dirty = true;
        }

        /// <summary>Writes the cache if anything changed since it was loaded or last saved. Throws I/O exceptions.</summary>
        public void Save()
        {
            if (!_dirty) return;

            var file = new LocalCacheFile { Version = CurrentVersion, Entries = new Dictionary<string, LocalCacheEntry>(_entries) };
            var bytes = JsonFile.Write(file);
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = _path + ".tmp";
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
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                throw;
            }
            _dirty = false;
        }

        private void Load()
        {
            byte[] bytes;
            try
            {
                if (!File.Exists(_path)) return;
                bytes = File.ReadAllBytes(_path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LoadProblem = Path.GetFileName(_path) + " could not be read (" + ex.Message + "); hashing every save again";
                _dirty = true;
                return;
            }

            string problem;
            var file = JsonFile.TryRead<LocalCacheFile>(bytes, Path.GetFileName(_path), out problem);
            if (file == null || file.Version != CurrentVersion || file.Entries == null)
            {
                LoadProblem = (problem ?? Path.GetFileName(_path) + " has an unknown layout") + "; hashing every save again";
                _dirty = true; // rewrite it on the next Save
                return;
            }

            foreach (var pair in file.Entries)
            {
                // An entry that is not a well-formed hash is useless; dropping it only costs a re-hash.
                if (pair.Key == null || pair.Value == null || !ContentHash.IsSha256Hex(pair.Value.Sha256))
                {
                    _dirty = true;
                    continue;
                }
                _entries[pair.Key] = pair.Value;
            }
        }
    }

    [DataContract]
    internal sealed class LocalCacheFile
    {
        [DataMember] public int Version { get; set; }

        [DataMember] public Dictionary<string, LocalCacheEntry> Entries { get; set; }
    }

    [DataContract]
    internal sealed class LocalCacheEntry
    {
        [DataMember] public long Length { get; set; }

        [DataMember] public long WriteTicks { get; set; }

        [DataMember] public string Sha256 { get; set; }
    }
}
