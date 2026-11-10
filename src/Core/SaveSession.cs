using System;
using System.Collections.Generic;
using System.IO;

namespace LoMSaveManager
{
    /// <summary>
    /// The mod's metadata for one game save folder (the per-Steam-account folder holding Save_*.dat):
    /// it owns LoMSaveManager.json (in the folder root, so Steam Cloud syncs it), the per-machine hash cache in the
    /// LoMSaveManager subfolder (not synced), the <see cref="SaveCatalog"/> over them, and the bookkeeping the SaveSystem
    /// hooks run around a save or a delete. Nothing but LoMSaveManager.json is ever written to the folder root.
    /// It has no Unity or game dependency, so it is tested with plain files.
    /// Methods may throw I/O exceptions while scanning the folder; the hook layer logs those and moves on.
    /// </summary>
    public sealed class SaveSession
    {
        /// <summary>The per-machine subfolder (Steam Auto-Cloud does not recurse, so nothing in it is synced).</summary>
        public const string FolderName = "LoMSaveManager";

        /// <summary>The synced metadata file in the save folder root. Deliberately not "Save_*": the game and other tools own those names.</summary>
        public const string IndexFileName = "LoMSaveManager.json";

        /// <summary>The v1 metadata file inside <see cref="FolderName"/>, read once by <see cref="MetadataMigration"/>.</summary>
        public const string LegacyIndexFileName = "index.json";

        public const string LocalCacheFileName = "local-cache.json";

        /// <summary>
        /// Per-machine record (in <see cref="FolderName"/>, not synced) of a held startup: the hash of the
        /// LoMSaveManager.json that was held and how many starts in a row held it.
        /// </summary>
        public const string HeldNamesFileName = "held-names.json";

        private readonly SaveIndexStore _store;
        private readonly SaveIndex _index;
        private readonly LocalStampCache _cache;
        private readonly Action<string> _warn;
        private bool _unwritableReported;
        private bool _writeFailureReported;
        private bool _cacheFailureReported;
        private bool _migrationPending;
        private bool _heldStaleNames;

        /// <summary>
        /// How many starts in a row may hold the same LoMSaveManager.json (every named save missing or changed) before
        /// the next one accepts that the player really removed them.
        /// </summary>
        internal const int HeldStartsBeforeAccepting = 3;

        // Own writes whose after-save scan failed: settled by the next successful scan instead of being judged as replaced files.
        private readonly Dictionary<string, KeyValuePair<OwnWriteTicket, string>> _pendingWrites =
            new Dictionary<string, KeyValuePair<OwnWriteTicket, string>>(StringComparer.Ordinal);

        private SaveSession(string userFolder, SaveIndexStore store, SaveIndex index, LocalStampCache cache, Action<string> warn)
        {
            UserFolder = userFolder;
            _store = store;
            _index = index;
            _cache = cache;
            _warn = warn;
            Catalog = new SaveCatalog(index);
        }

        public string UserFolder { get; }

        public SaveCatalog Catalog { get; }

        /// <summary>
        /// False when LoMSaveManager.json exists but could not be read at startup (or is from a newer version, or a v1
        /// migration is pending), or after a write was refused for being larger than the read cap: it is then never
        /// written again this session, so the UI must not offer edits. Also false for a whole run that started with every
        /// named or filed save missing or changed (see <see cref="Open"/>).
        /// </summary>
        public bool CanPersist
        {
            get { return _store.CanSave && !_migrationPending && !_heldStaleNames; }
        }

        /// <summary>What the v1 -> v2 migration run by <see cref="Open"/> did (for the log).</summary>
        public MigrationResult Migration { get; private set; }

        /// <summary>The synced metadata file: &lt;userFolder&gt;/LoMSaveManager.json.</summary>
        public static string IndexPathFor(string userFolder)
        {
            return Path.Combine(RequireFolder(userFolder), IndexFileName);
        }

        /// <summary>The per-machine folder: &lt;userFolder&gt;/LoMSaveManager (cache, temp file, quarantined and migrated files).</summary>
        public static string LocalFolderFor(string userFolder)
        {
            return Path.Combine(RequireFolder(userFolder), FolderName);
        }

        public static string LocalCachePathFor(string userFolder)
        {
            return Path.Combine(LocalFolderFor(userFolder), LocalCacheFileName);
        }

        public static string LegacyIndexPathFor(string userFolder)
        {
            return Path.Combine(LocalFolderFor(userFolder), LegacyIndexFileName);
        }

        private static string RequireFolder(string userFolder)
        {
            if (string.IsNullOrEmpty(userFolder)) throw new ArgumentException("userFolder is required", nameof(userFolder));
            return userFolder;
        }

        /// <summary>
        /// Reads LoMSaveManager.json (a bad one is quarantined into the local folder, a locked or newer one is left alone
        /// and reported), drops the records of saves whose length or content changed or that vanished, and writes it back
        /// if that changed anything (a missing file is not created while the index is empty). First runs <see cref="MetadataMigration.TryMigrate"/>; if a v1 index.json is still
        /// waiting to be migrated after that (it failed, for example a save was locked), the session is read-only
        /// (<see cref="CanPersist"/> false) and creates no LoMSaveManager.json, so the old names are not stranded and
        /// an empty file is never uploaded to Steam Cloud; the next start tries again. When this startup check would drop
        /// every record that holds a name or a category (typically Steam Cloud delivered the file before the saves), it is
        /// not written back and the whole run is held (<see cref="CanPersist"/> false until the game is restarted): the records
        /// are dropped in memory only and nothing writes the file this run. After <see cref="HeldStartsBeforeAccepting"/>
        /// such starts in a row with the same file, the next start accepts the removal and writes as usual.
        /// </summary>
        /// <param name="warn">Receives problems worth a log line; it must not throw.</param>
        public static SaveSession Open(string userFolder, Action<string> warn)
        {
            return Open(userFolder, warn, SaveIndexStore.MaxFileBytes);
        }

        /// <param name="maxIndexBytes">The read (and write) cap of LoMSaveManager.json; tests use a small one.</param>
        internal static SaveSession Open(string userFolder, Action<string> warn, int maxIndexBytes)
        {
            if (warn == null) throw new ArgumentNullException(nameof(warn));
            var migration = MetadataMigration.TryMigrate(userFolder);
            if (migration.Problem != null) warn(migration.Problem);
            if (migration.Note != null) warn(migration.Note);
            // A completed migration with nothing to carry over writes no synced file; a leftover index.json then is not pending.
            var migrationPending = !migration.Migrated
                && !File.Exists(IndexPathFor(userFolder)) && File.Exists(LegacyIndexPathFor(userFolder));

            var store = new SaveIndexStore(IndexPathFor(userFolder), LocalFolderFor(userFolder), maxIndexBytes);
            var index = store.Load();
            if (store.LastLoadProblem != null) warn(store.LastLoadProblem);
            var cache = new LocalStampCache(LocalCachePathFor(userFolder));
            if (cache.LoadProblem != null) warn(cache.LoadProblem);

            var session = new SaveSession(userFolder, store, index, cache, warn);
            session.Migration = migration;
            session._migrationPending = migrationPending;
            var changed = false;
            var withUserData = session.KeysWithUserData();
            try
            {
                changed = session.Catalog.Reconcile(session.ScanManualSaves());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Better to keep every record than to judge them against half a listing; the next save reconciles again.
                warn("could not list the save folder: " + ex.Message);
            }
            var allStale = changed && withUserData.Count > 0 && !withUserData.Overlaps(session._index.Slots.Keys);
            if (session.CanPersist) changed = session.SettleHold(allStale, withUserData.Count) && changed;
            // Write only when a record really went stale. Loading alone (the limits applied by SaveIndex.Normalize, or a
            // file formatted differently) must not rewrite the synced file. The next write of any kind (an edit, a save
            // through the game, a stale record) writes the whole normalized index, and what Normalize dropped or cut is
            // then gone from the file for good. Persist still runs on a read-only session, to say once why nothing will be saved.
            if (changed || !session.CanPersist) session.Persist();
            return session;
        }

        /// <summary>Writes LoMSaveManager.json (only if its content changed; never creates it to hold an empty index). Returns false (and says why, once) instead of throwing; never writes when <see cref="CanPersist"/> is false.</summary>
        public bool Persist()
        {
            if (!CanPersist)
            {
                if (!_unwritableReported)
                {
                    _unwritableReported = true;
                    _warn(ReadOnlyReason() + "; names and categories will not be saved this session.");
                }
                return false;
            }

            try
            {
                // Never create the synced file just to hold nothing: an empty LoMSaveManager.json (offline start, a failed
                // Steam download, Cloud enabled later) could win a Steam Cloud conflict and wipe the populated cloud copy.
                // The first real edit creates it; an existing file is still kept in sync, emptied or not.
                if (_index.IsEmpty && !File.Exists(IndexPathFor(UserFolder))) return true;
                _store.Save(_index);
                _writeFailureReported = false;
                return true;
            }
            catch (IndexTooLargeException ex)
            {
                // The store is read-only from now on; this is the one line that says why.
                _unwritableReported = true;
                _warn(ex.Message + "; names and categories will not be saved for the rest of this session.");
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (!_writeFailureReported)
                {
                    _writeFailureReported = true;
                    _warn("could not write " + IndexFileName + ": " + ex.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// Decides a startup whose reconcile would drop every named or filed record (<paramref name="allStale"/>): most
        /// likely Steam Cloud delivered LoMSaveManager.json before the saves. Such a run is held: nothing is written for its
        /// whole length (any write would upload the file without those names), the records are dropped in memory only,
        /// and the per-machine <see cref="HeldNamesFileName"/> counts the starts in a row that held the same file. After
        /// <see cref="HeldStartsBeforeAccepting"/> of them the next start accepts the removal (the player really deleted
        /// those saves). A start that holds nothing, or a different file, starts the count over. Returns whether the
        /// reconcile may be written.
        /// </summary>
        private bool SettleHold(bool allStale, int named)
        {
            var holdPath = Path.Combine(LocalFolderFor(UserFolder), HeldNamesFileName);
            if (!allStale)
            {
                TryDeleteHold(holdPath);
                return true;
            }

            string hash;
            try { hash = ContentHash.Sha256Hex(IndexPathFor(UserFolder)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { hash = null; }

            var record = ReadHold(holdPath);
            var count = record != null && hash != null && record.Sha256 == hash ? record.Count + 1 : 1;
            if (count > HeldStartsBeforeAccepting)
            {
                TryDeleteHold(holdPath);
                _warn("every named or filed save (" + named + ") has been missing or changed on disk for " + HeldStartsBeforeAccepting
                    + " starts in a row with the same " + IndexFileName + "; the removal is accepted and the file is updated.");
                return true;
            }

            _heldStaleNames = true;
            try
            {
                Directory.CreateDirectory(LocalFolderFor(UserFolder));
                File.WriteAllBytes(holdPath, JsonFile.Write(new HeldNamesRecord { Version = 1, Sha256 = hash, Count = count }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _warn("could not write " + HeldNamesFileName + ": " + ex.Message);
            }
            return false;
        }

        private static HeldNamesRecord ReadHold(string holdPath)
        {
            try
            {
                if (!File.Exists(holdPath)) return null;
                var bytes = JsonFile.ReadAllBytesUpTo(holdPath, 64 * 1024);
                if (bytes == null) return null;
                string problem;
                var record = JsonFile.TryRead<HeldNamesRecord>(bytes, HeldNamesFileName, out problem);
                return record != null && record.Version == 1 && record.Count > 0 ? record : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDeleteHold(string holdPath)
        {
            try { if (File.Exists(holdPath)) File.Delete(holdPath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        /// <summary>Slot keys whose record holds something the user made: a name or a category.</summary>
        private HashSet<string> KeysWithUserData()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in _index.Slots)
                if (!string.IsNullOrEmpty(pair.Value.Name) || pair.Value.CategoryId != null) keys.Add(pair.Key);
            return keys;
        }

        private string ReadOnlyReason()
        {
            if (_migrationPending && Migration != null && Migration.NeedsPlayer)
                return "the names and categories of the previous version (" + FolderName + "/" + LegacyIndexFileName
                    + ") could not be migrated and this is not retried automatically (see the warning above), so " + IndexFileName + " is not created";
            if (_migrationPending)
                return "the names and categories of the previous version (" + FolderName + "/" + LegacyIndexFileName
                    + ") are not migrated yet, so " + IndexFileName + " is not created (the migration is retried at the next start)";
            if (_heldStaleNames)
                return "every named or filed save is missing or changed on disk (Steam Cloud may still be downloading them), so "
                    + IndexFileName + " is left as it is for this whole run and their names are not shown; restart the game once "
                    + "the saves have finished syncing (if they were really deleted, the removal is accepted after "
                    + HeldStartsBeforeAccepting + " such starts in a row)";
            if (_store.RefusedOversizedWrite)
                return IndexFileName + " would have grown past the size this mod can read back, so the last good file is kept";
            if (_store.LoadedNewerVersion)
                return IndexFileName + " was written by a newer version of this mod and is left untouched";
            return IndexFileName + " was left untouched because it could not be read at startup";
        }

        /// <summary>
        /// The manual saves with their stamps; hashes come from the local cache unless a file changed. Saves the cache, and
        /// settles own writes whose earlier after-save scan failed (persisting them).
        /// </summary>
        public IDictionary<string, SaveStamp> ScanManualSaves()
        {
            var scan = SlotFiles.ScanManualSaves(UserFolder, _cache);
            SaveCache();
            if (_pendingWrites.Count > 0)
            {
                foreach (var pending in _pendingWrites.Values)
                    Catalog.CompleteOwnWrite(pending.Key, scan, CanPersist ? pending.Value : null);
                _pendingWrites.Clear();
                Persist();
            }
            return scan;
        }

        private void SaveCache()
        {
            try
            {
                _cache.Save();
                _cacheFailureReported = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Only costs re-hashing next time; never worth more than one line.
                if (!_cacheFailureReported)
                {
                    _cacheFailureReported = true;
                    _warn("could not write " + LocalCacheFileName + ": " + ex.Message);
                }
            }
        }

        /// <summary>Keys taken by a .dat, .dat.tmp or .dat.bak, for choosing the next free slot.</summary>
        public ISet<string> OccupiedKeys()
        {
            return SlotFiles.OccupiedKeysInDirectory(UserFolder);
        }

        /// <summary>Before SaveSystem.SaveGameData(slot): null for slots that are not manual (auto, universe, ...).</summary>
        public OwnWriteTicket BeforeSave(string slot)
        {
            if (!SlotKey.IsManual(slot)) return null;
            return Catalog.BeginOwnWrite(slot, ScanManualSaves());
        }

        /// <summary>After SaveSystem.SaveGameData(slot) returned: records the write and persists. A null ticket does nothing.</summary>
        /// <param name="categoryIdIfNew">The category a brand-new save goes to (the save panel's current selection), or null.</param>
        public void AfterSave(OwnWriteTicket ticket, string categoryIdIfNew)
        {
            if (ticket == null) return;
            _cache.Invalidate(ticket.Key); // hash what the game just wrote, whatever its length and time say
            IDictionary<string, SaveStamp> scan;
            try
            {
                scan = ScanManualSaves();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Judging the slot later as a replaced file would drop its name; keep the ticket until a scan succeeds.
                _pendingWrites[ticket.Key] = new KeyValuePair<OwnWriteTicket, string>(ticket, categoryIdIfNew);
                _warn("could not read the save folder after saving slot " + ticket.Key + " (" + ex.Message
                    + "); its name and category are kept and recorded at the next scan");
                return;
            }
            // While nothing can be saved, a category shown now would vanish at the next start: file it nowhere.
            Catalog.CompleteOwnWrite(ticket, scan, CanPersist ? categoryIdIfNew : null);
            Persist();
        }

        /// <summary>
        /// Scans the folder, drops records that no longer describe their file (persisting if anything changed) and
        /// returns the category's saves sorted by file write time (newest first unless the index says otherwise) plus the lowest free key.
        /// <paramref name="categoryId"/> null = uncategorized.
        /// </summary>
        public SlotListKeys ListKeys(string categoryId)
        {
            var scan = ScanManualSaves();
            if (Catalog.Reconcile(scan)) Persist();
            var keys = Catalog.KeysIn(categoryId, scan.Keys, Catalog.NewestFirst, key => scan[key].WriteTicks);
            return new SlotListKeys(keys, new HashSet<string>(scan.Keys), SlotAllocator.LowestFree(OccupiedKeys()));
        }

        /// <summary>
        /// After SaveSystem.NewGameData() wrote <paramref name="slot"/> (a new game started from an empty load-panel row):
        /// files it in <paramref name="categoryId"/> (null = uncategorized) and persists. The row was empty, so any record
        /// still held for that slot belongs to an earlier file and is dropped first, even if the new file happens to have
        /// the same bytes: the new game never inherits an old name.
        /// </summary>
        public void AfterNewGame(string slot, string categoryId)
        {
            if (!SlotKey.IsManual(slot)) return;
            _cache.Invalidate(slot);
            var scan = ScanManualSaves();
            Catalog.Reconcile(scan);
            Catalog.Forget(slot);
            SaveStamp stamp;
            if (scan.TryGetValue(slot, out stamp)) Catalog.RecordOwnWrite(slot, stamp, CanPersist ? categoryId : null);
            Persist();
        }

        /// <summary>
        /// After SaveSystem.DeleteSaveData(slot) returned: reconciles with a fresh scan, so the slot's name and category
        /// are forgotten only if its file is really gone (a delete that failed keeps them), and persists if anything changed.
        /// </summary>
        public void AfterDelete(string slot)
        {
            if (!SlotKey.IsManual(slot)) return;
            if (Catalog.Reconcile(ScanManualSaves())) Persist();
        }
    }

    /// <summary>The per-machine <see cref="SaveSession.HeldNamesFileName"/>: which synced file was held, and how many starts in a row.</summary>
    [System.Runtime.Serialization.DataContract]
    internal sealed class HeldNamesRecord
    {
        [System.Runtime.Serialization.DataMember] public int Version { get; set; }

        [System.Runtime.Serialization.DataMember] public string Sha256 { get; set; }

        [System.Runtime.Serialization.DataMember] public int Count { get; set; }
    }
}
