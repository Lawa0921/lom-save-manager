using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoMSaveManager
{
    public static class SlotAllocator
    {
        /// <summary>Smallest manual key not in occupiedKeys, or null when 001..9999 are all taken.</summary>
        public static string LowestFree(ISet<string> occupiedKeys)
        {
            if (occupiedKeys == null) throw new ArgumentNullException("occupiedKeys");
            for (var n = 1; n <= SlotKey.MaxManualSlot; n++)
            {
                var key = SlotKey.Format(n);
                if (!occupiedKeys.Contains(key)) return key;
            }
            return null;
        }
    }

    public static class SlotFiles
    {
        private const string Prefix = "Save_";
        private const string DatSuffix = ".dat";
        private static readonly string[] Suffixes = { DatSuffix, ".dat.tmp", ".dat.bak" };

        /// <summary>
        /// Manual keys taken by Save_&lt;key&gt;.dat, .dat.tmp or .dat.bak (any one counts; names are case-insensitive).
        /// Takes bare file names, not paths.
        /// </summary>
        public static ISet<string> OccupiedKeys(IEnumerable<string> fileNames)
        {
            if (fileNames == null) throw new ArgumentNullException("fileNames");
            var keys = new HashSet<string>();
            foreach (var name in fileNames)
            {
                if (name == null || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var suffix in Suffixes)
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = name.Substring(Prefix.Length, name.Length - Prefix.Length - suffix.Length);
                    if (SlotKey.IsManual(key)) keys.Add(key);
                    break;
                }
            }
            return keys;
        }

        /// <summary>
        /// <see cref="OccupiedKeys"/> for a save folder on disk. Directory.GetFiles returns full paths, so this
        /// reduces them to bare names first. A folder that does not exist yet has no keys. Subfolders are not searched.
        /// </summary>
        public static ISet<string> OccupiedKeysInDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory is required", "directory");
            if (!Directory.Exists(directory)) return new HashSet<string>();
            return OccupiedKeys(Directory.GetFiles(directory, Prefix + "*").Select(Path.GetFileName));
        }

        /// <summary>
        /// The manual saves in a save folder: key -> stamp (length, write ticks, SHA-256) of Save_&lt;key&gt;.dat. Only finished
        /// .dat files count (.tmp and .bak are leftovers, see <see cref="OccupiedKeys"/>); auto, universe and other non-manual
        /// names are skipped. With a <paramref name="cache"/>, only files whose length or write time changed are hashed, and the
        /// cache forgets keys that are gone (the caller saves it). A file that disappears while scanning is skipped; any other
        /// I/O error is thrown, because a partial answer would make <see cref="SaveCatalog.Reconcile"/> drop the names of saves
        /// that are really there.
        /// </summary>
        public static IDictionary<string, SaveStamp> ScanManualSaves(string directory, LocalStampCache cache = null)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory is required", "directory");
            var saves = new Dictionary<string, SaveStamp>(StringComparer.Ordinal);
            if (!Directory.Exists(directory))
            {
                if (cache != null) cache.Retain(saves.Keys);
                return saves;
            }

            foreach (var path in Directory.GetFiles(directory, Prefix + "*"))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(DatSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                var key = name.Substring(Prefix.Length, name.Length - Prefix.Length - DatSuffix.Length);
                if (!SlotKey.IsManual(key)) continue;

                var file = new FileInfo(path);
                try { saves[key] = cache != null ? cache.Resolve(key, file) : SaveStamp.Compute(file); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            if (cache != null) cache.Retain(saves.Keys);
            return saves;
        }
    }
}
