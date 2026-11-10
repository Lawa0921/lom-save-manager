using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace LoMSaveManager
{
    /// <summary>What <see cref="MetadataMigration.TryMigrate"/> did.</summary>
    public sealed class MigrationResult
    {
        internal MigrationResult(bool migrated, int kept, int dropped, string problem, string note = null, bool wroteSyncedFile = true)
        {
            Migrated = migrated;
            Kept = kept;
            Dropped = dropped;
            Problem = problem;
            Note = note;
            WroteSyncedFile = migrated && wroteSyncedFile;
        }

        /// <summary>True when a completed migration wrote LoMSaveManager.json (false when the old index held nothing to carry over).</summary>
        public bool WroteSyncedFile { get; }

        /// <summary>True when LoMSaveManager.json was written from the v1 index during this call.</summary>
        public bool Migrated { get; }

        /// <summary>v1 slot records carried over (their file still had the recorded length and write time).</summary>
        public int Kept { get; }

        /// <summary>v1 slot records dropped because their file was gone or had changed, as v1 itself would have done.</summary>
        public int Dropped { get; }

        /// <summary>Why the migration did not happen or was incomplete; null when there was nothing to do or it went fine.</summary>
        public string Problem { get; }

        /// <summary>
        /// Something worth one log line that is not a failure: set when an old index.json was left alone because
        /// LoMSaveManager.json already exists (the synced file wins; the old names on this PC are not merged in).
        /// </summary>
        public string Note { get; }

        /// <summary>One log line for a migration that completed during this call (it says so when the old file could not be renamed); null otherwise (a failure is described by <see cref="Problem"/>).</summary>
        public string Summary
        {
            get
            {
                if (!Migrated) return null;
                var line = WroteSyncedFile
                    ? "Migrated the names and categories from " + SaveSession.FolderName + "/" + SaveSession.LegacyIndexFileName
                        + " to " + SaveSession.IndexFileName + " (" + Kept + " kept, " + Dropped + " dropped); "
                    : "Migrated " + SaveSession.FolderName + "/" + SaveSession.LegacyIndexFileName + " (" + Kept + " kept, " + Dropped
                        + " dropped): nothing to carry over, so " + SaveSession.IndexFileName + " is not created until the first edit; ";
                // A Problem on a migration that did happen means the final rename failed (the details are in Problem).
                return Problem == null
                    ? line + "the old file was kept as " + SaveSession.LegacyIndexFileName + ".migrated-*."
                    : line + (WroteSyncedFile
                        ? "the old file could not be renamed and stays in place, ignored from now on."
                        : "the old file could not be renamed and stays in place; it is checked again at the next start.");
            }
        }
    }

    /// <summary>
    /// One-time move from v1 (LoMSaveManager/index.json, not synced, records stamped with write ticks) to v2
    /// (LoMSaveManager.json in the save folder root, synced, records stamped with SHA-256).
    /// Runs only when LoMSaveManager.json does not exist and index.json does. A record is carried over only if its
    /// file still has the recorded length and write ticks (the v1 rule); its content is hashed then.
    /// On any failure LoMSaveManager.json is not written, so the next start tries again (an index.json that can never be
    /// parsed is renamed to index.json.corrupt-&lt;UTC ticks&gt; instead). After a successful write the old file is
    /// renamed to index.json.migrated-&lt;UTC ticks&gt;, never deleted. <see cref="SaveSession.Open"/> calls this itself.
    /// </summary>
    public static class MetadataMigration
    {
        private static readonly MigrationResult NothingToDo = new MigrationResult(false, 0, 0, null);

        private static readonly MigrationResult LegacyIgnored = new MigrationResult(false, 0, 0, null,
            SaveSession.FolderName + "/" + SaveSession.LegacyIndexFileName + " (names and categories of the previous version on this PC) "
            + "was not migrated because " + SaveSession.IndexFileName + " already exists; " + SaveSession.IndexFileName
            + " is kept as it is and the old file is left untouched.");

        public static MigrationResult TryMigrate(string userFolder)
        {
            var syncPath = SaveSession.IndexPathFor(userFolder);
            var legacyPath = SaveSession.LegacyIndexPathFor(userFolder);
            var localFolder = SaveSession.LocalFolderFor(userFolder);

            if (!File.Exists(legacyPath)) return NothingToDo;

            // The synced file wins whenever it exists (it may have come from another machine); the old one stays as it is,
            // never merged, renamed or written. Say so, so a second PC's own old names are not lost without a word.
            if (File.Exists(syncPath)) return LegacyHoldsContent(legacyPath) ? LegacyIgnored : NothingToDo;

            byte[] bytes;
            try
            {
                bytes = JsonFile.ReadAllBytesUpTo(legacyPath, SaveIndexStore.MaxFileBytes);
            }
            catch (FileNotFoundException) { return NothingToDo; }
            catch (DirectoryNotFoundException) { return NothingToDo; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Failed(SaveSession.LegacyIndexFileName + " could not be read: " + ex.Message);
            }
            if (bytes == null)
            {
                // Left in place like an unreadable file (the session stays read-only), not moved aside: it may be fine.
                return Failed(SaveSession.LegacyIndexFileName + " is larger than " + JsonFile.Megabytes(SaveIndexStore.MaxFileBytes)
                    + " and was not read; left untouched");
            }

            string problem;
            var legacy = JsonFile.TryRead<LegacyIndexV1>(bytes, SaveSession.LegacyIndexFileName, out problem);
            if (legacy != null && legacy.Version != 1)
            {
                problem = SaveSession.LegacyIndexFileName + " has unexpected version " + legacy.Version;
                legacy = null;
            }
            if (legacy == null)
            {
                // It can never be migrated, and while it sits there the session stays read-only; so move it aside
                // (kept, never deleted) and let a fresh LoMSaveManager.json start.
                return Failed(problem + QuarantineLegacy(legacyPath));
            }
            legacy.Normalize();

            var cache = new LocalStampCache(SaveSession.LocalCachePathFor(userFolder));
            var index = new SaveIndex { NewestFirst = legacy.NewestFirst };
            index.Categories.AddRange(legacy.Categories);

            int kept = 0, dropped = 0;
            foreach (var pair in legacy.Slots)
            {
                var record = pair.Value;
                if (!SlotKey.IsManual(pair.Key))
                {
                    dropped++;
                    continue;
                }

                var file = new FileInfo(Path.Combine(userFolder, "Save_" + pair.Key + ".dat"));
                try
                {
                    file.Refresh();
                    if (!file.Exists || file.Length != record.Length || file.LastWriteTimeUtc.Ticks != record.WriteTicks)
                    {
                        dropped++;
                        continue;
                    }

                    var stamp = cache.Resolve(pair.Key, file);
                    if (stamp.Length != record.Length || stamp.WriteTicks != record.WriteTicks)
                    {
                        dropped++; // changed while we looked at it
                        continue;
                    }

                    index.Slots[pair.Key] = new SlotRecord
                    {
                        Name = record.Name,
                        CategoryId = record.CategoryId,
                        Length = stamp.Length,
                        Sha256 = stamp.Sha256,
                    };
                    kept++;
                }
                catch (FileNotFoundException) { dropped++; }
                catch (DirectoryNotFoundException) { dropped++; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Dropping it would lose a name for good; better to try again next start.
                    return Failed("could not read " + file.Name + " to hash it: " + ex.Message);
                }
            }

            if (File.Exists(syncPath)) return LegacyIgnored;
            // The same limits a load applies, so the file written here is not changed again by the next start.
            index.Normalize();
            // Nothing to carry over (v1 wrote index.json even when nothing was named): creating an empty synced file
            // could win a Steam Cloud conflict against a populated one, so leave it to the first edit.
            var writeSynced = !index.IsEmpty;
            try
            {
                if (writeSynced) new SaveIndexStore(syncPath, localFolder).Save(index);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Failed("could not write " + SaveSession.IndexFileName + ": " + ex.Message);
            }

            string renameProblem = null;
            try
            {
                var stem = legacyPath + ".migrated-" + DateTime.UtcNow.Ticks;
                var target = stem;
                for (var n = 1; File.Exists(target); n++)
                    target = stem + "-" + n;
                File.Move(legacyPath, target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Harmless: with LoMSaveManager.json in place the old file is never read again; without it (nothing to
                // carry over) the next start simply migrates the same empty index again.
                renameProblem = writeSynced
                    ? SaveSession.IndexFileName + " was written, but " + SaveSession.LegacyIndexFileName
                        + " could not be renamed (" + ex.Message + "); it is ignored from now on"
                    : "nothing needed migrating, but " + SaveSession.LegacyIndexFileName
                        + " could not be renamed (" + ex.Message + "); it is checked again at the next start";
            }

            try { cache.Save(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }

            return new MigrationResult(true, kept, dropped, renameProblem, null, writeSynced);
        }

        /// <summary>
        /// Whether an old index.json left beside LoMSaveManager.json may hold names, categories or a sort preference
        /// (true when it cannot be read or parsed: that cannot be ruled out). One whose rename failed after a
        /// migration with nothing to carry over holds none, and is then not worth a line at every start.
        /// </summary>
        private static bool LegacyHoldsContent(string legacyPath)
        {
            try
            {
                string problem;
                var bytes = JsonFile.ReadAllBytesUpTo(legacyPath, SaveIndexStore.MaxFileBytes);
                if (bytes == null) return true; // not read, so it cannot be ruled out
                var legacy = JsonFile.TryRead<LegacyIndexV1>(bytes, SaveSession.LegacyIndexFileName, out problem);
                if (legacy == null || legacy.Version != 1) return true;
                legacy.Normalize();
                if (legacy.Categories.Count > 0 || !legacy.NewestFirst) return true;
                foreach (var record in legacy.Slots.Values)
                    if (!string.IsNullOrEmpty(record.Name) || record.CategoryId != null) return true;
                return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return true; }
        }

        private static string QuarantineLegacy(string legacyPath)
        {
            try
            {
                var stem = legacyPath + ".corrupt-" + DateTime.UtcNow.Ticks;
                var target = stem;
                for (var n = 1; File.Exists(target); n++)
                    target = stem + "-" + n;
                File.Move(legacyPath, target);
                return "; moved to " + Path.GetFileName(target) + ", starting without the old names";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "; could not be moved aside (" + ex.Message + "), left in place";
            }
        }

        private static MigrationResult Failed(string problem)
        {
            return new MigrationResult(false, 0, 0, "metadata migration postponed: " + problem);
        }
    }

    /// <summary>The v1 index.json layout, read only by <see cref="MetadataMigration"/>.</summary>
    [DataContract]
    internal sealed class LegacyIndexV1
    {
        [DataMember] public int Version { get; set; }

        [DataMember] public List<CategoryRecord> Categories { get; set; }

        [DataMember] public Dictionary<string, LegacySlotRecordV1> Slots { get; set; }

        [DataMember] public bool NewestFirst { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Version = 1;
            Categories = new List<CategoryRecord>();
            Slots = new Dictionary<string, LegacySlotRecordV1>();
            NewestFirst = true;
        }

        internal void Normalize()
        {
            if (Categories == null) Categories = new List<CategoryRecord>();
            else Categories.RemoveAll(c => c == null);

            var clean = new Dictionary<string, LegacySlotRecordV1>();
            if (Slots != null)
                foreach (var pair in Slots)
                    if (pair.Key != null && pair.Value != null) clean[pair.Key] = pair.Value;
            Slots = clean;
        }
    }

    [DataContract]
    internal sealed class LegacySlotRecordV1
    {
        [DataMember] public string Name { get; set; }

        [DataMember] public string CategoryId { get; set; }

        [DataMember] public long Length { get; set; }

        [DataMember] public long WriteTicks { get; set; }
    }
}
