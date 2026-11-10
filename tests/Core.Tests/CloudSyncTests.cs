using System;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    /// <summary>
    /// The synced metadata file (LoMSaveManager.json in the save folder root, uploaded by Steam Auto-Cloud) seen
    /// through SaveSession: identity by length + SHA-256, nothing else written to the root, newer versions left alone.
    /// </summary>
    internal static class CloudSyncTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("cross_machine_mtime_change_keeps_metadata", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 1000, seed: 1);
                var b = TestStamps.PutSave(folder, "002", 1200, seed: 2);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "告白前");
                session.Catalog.Assign("001", cat);
                session.Catalog.Assign("002", cat);
                Check.True(session.Persist());
                var synced = File.ReadAllBytes(SaveSession.IndexPathFor(folder));

                // A Steam Cloud download restores the mtime with whole-second precision (often one second off).
                ShiftToWholeSecondPlusOne(a);
                ShiftToWholeSecondPlusOne(b);

                // Same machine, stale cache entries.
                var again = SaveSession.Open(folder, warnings.Add);
                Check.Equal("告白前", again.Catalog.NameOf("001"));
                Check.Equal(cat, again.Catalog.CategoryOf("001"));
                Check.Equal(cat, again.Catalog.CategoryOf("002"));
                Check.True(synced.SequenceEqual(File.ReadAllBytes(SaveSession.IndexPathFor(folder))));

                // Another machine: no local cache at all.
                Directory.Delete(SaveSession.LocalFolderFor(folder), true);
                ShiftToWholeSecondPlusOne(a);
                var other = SaveSession.Open(folder, warnings.Add);
                Check.Equal("告白前", other.Catalog.NameOf("001"));
                Check.Equal(cat, other.Catalog.CategoryOf("001"));
                Check.Equal(cat, other.Catalog.CategoryOf("002"));
                Check.Equal(2, other.ListKeys(cat).Keys.Count);
                Check.True(synced.SequenceEqual(File.ReadAllBytes(SaveSession.IndexPathFor(folder))));
                Check.Equal(0, warnings.Count);
            }));

            test("startup_that_finds_every_named_save_stale_keeps_the_synced_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 1000, seed: 1);
                var b = TestStamps.PutSave(folder, "002", 1200, seed: 2);
                var session = SaveSession.Open(folder, warnings.Add);
                session.ListKeys(null);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "告白前");
                session.Catalog.Assign("002", cat);
                Check.True(session.Persist());
                var ticket = session.BeforeSave("003");
                TestStamps.PutSave(folder, "003", 800, seed: 3);
                session.AfterSave(ticket, null); // a stamp-only record: carries no user data
                var path = SaveSession.IndexPathFor(folder);
                var synced = File.ReadAllBytes(path);
                Check.True(Store(folder).Load().Slots.ContainsKey("003"));

                // Steam Cloud has delivered the json but not yet the saves: one .dat missing, one still the old content.
                var aBytes = File.ReadAllBytes(a);
                var bBytes = File.ReadAllBytes(b);
                File.Delete(a);
                TestStamps.PutSave(folder, "002", 1200, seed: 7);
                File.Delete(Path.Combine(folder, "Save_003.dat"));

                var early = SaveSession.Open(folder, warnings.Add);
                // Held for the whole run: nothing the game does in it may write the file without those names.
                Check.False(early.CanPersist);
                Check.True(warnings.Any(w => w.Contains("left as it is") && w.Contains("restart")));
                var controller = new CategoryController(early.Catalog, () => early.Persist(), () => early.CanPersist);
                Check.False(controller.CanEdit);
                Check.Throws<ArgumentException>(() => controller.Create("新分類"));
                early.ListKeys(null);
                early.ListKeys(cat);
                Check.True(synced.SequenceEqual(File.ReadAllBytes(path)));

                // A save through the game, a new game and a delete all work, and none of them writes the file.
                var saving = early.BeforeSave("004");
                TestStamps.PutSave(folder, "004", 900, seed: 4);
                early.AfterSave(saving, cat);
                Check.Null(early.Catalog.CategoryOf("004")); // not shown in a category that would vanish at restart
                TestStamps.PutSave(folder, "005", 900, seed: 5);
                early.AfterNewGame("005", cat);
                Check.Null(early.Catalog.CategoryOf("005"));
                File.Delete(Path.Combine(folder, "Save_004.dat"));
                early.AfterDelete("004");
                Check.True(synced.SequenceEqual(File.ReadAllBytes(path)));
                Check.True(File.Exists(Path.Combine(folder, "Save_005.dat")));

                // Once the saves arrive, the next start finds every name again and can write as usual.
                File.WriteAllBytes(a, aBytes);
                File.WriteAllBytes(b, bBytes);
                var later = SaveSession.Open(folder, warnings.Add);
                Check.True(later.CanPersist);
                Check.Equal("告白前", later.Catalog.NameOf("001"));
                Check.Equal(cat, later.Catalog.CategoryOf("002"));
                Check.False(Store(folder).Load().Slots.ContainsKey("003")); // its .dat really is gone: a normal reconcile
                Check.Equal("告白前", Store(folder).Load().Slots["001"].Name);
                Check.False(File.Exists(HoldPath(folder))); // the hold count is reset
            }));

            test("holding_the_same_file_three_starts_in_a_row_then_accepts_the_removal", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = NameOnlySaves(folder, "001", "002");
                File.Delete(Path.Combine(folder, "Save_001.dat"));
                File.Delete(Path.Combine(folder, "Save_002.dat"));
                var synced = File.ReadAllBytes(path);

                for (var start = 1; start <= 3; start++)
                {
                    Check.False(SaveSession.Open(folder, warnings.Add).CanPersist);
                    Check.True(synced.SequenceEqual(File.ReadAllBytes(path)));
                }

                // The player really deleted them outside the game: the fourth start in a row accepts it.
                var accepted = SaveSession.Open(folder, warnings.Add);
                Check.True(accepted.CanPersist);
                Check.True(warnings.Any(w => w.Contains("accepted")));
                Check.Equal(0, Store(folder).Load().Slots.Count);
                Check.False(File.Exists(HoldPath(folder)));
                Check.True(SaveSession.Open(folder, warnings.Add).CanPersist);
            }));

            test("a_changed_file_or_a_cleared_hold_restarts_the_hold_count", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = NameOnlySaves(folder, "001", "002");
                var oneBytes = File.ReadAllBytes(Path.Combine(folder, "Save_001.dat"));
                File.Delete(Path.Combine(folder, "Save_001.dat"));
                File.Delete(Path.Combine(folder, "Save_002.dat"));

                SaveSession.Open(folder, warnings.Add);
                SaveSession.Open(folder, warnings.Add);
                // Another PC uploaded a different file: counting starts over.
                var index = Store(folder).Load();
                index.NewestFirst = false;
                Store(folder).Save(index);
                for (var start = 1; start <= 3; start++) Check.False(SaveSession.Open(folder, warnings.Add).CanPersist);
                Check.Equal(2, Store(folder).Load().Slots.Count);

                // One save arrives: nothing is held any more (and the vanished one is dropped as usual), the count resets.
                File.WriteAllBytes(Path.Combine(folder, "Save_001.dat"), oneBytes);
                Check.True(SaveSession.Open(folder, warnings.Add).CanPersist);
                Check.False(File.Exists(HoldPath(folder)));
                Check.SetEqual(new[] { "001" }, Store(folder).Load().Slots.Keys);

                File.Delete(Path.Combine(folder, "Save_001.dat"));
                for (var start = 1; start <= 3; start++) Check.False(SaveSession.Open(folder, warnings.Add).CanPersist);
                Check.Equal(1, Store(folder).Load().Slots.Count);
            }));

            test("startup_reconcile_still_writes_when_some_named_save_survives_or_none_was_named", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 1000, seed: 1);
                var b = TestStamps.PutSave(folder, "002", 1200, seed: 2);
                var session = SaveSession.Open(folder, warnings.Add);
                session.ListKeys(null);
                session.Catalog.SetName("001", "kept");
                session.Catalog.SetName("002", "gone");
                Check.True(session.Persist());

                File.Delete(b);
                SaveSession.Open(folder, warnings.Add);
                Check.False(Store(folder).Load().Slots.ContainsKey("002"));
                Check.Equal("kept", Store(folder).Load().Slots["001"].Name);

                // Only stamp-only records left and all stale: nothing a user made is at stake, so it is written as usual.
                var stampOnly = new SaveIndex();
                stampOnly.Categories.Add(new CategoryRecord { Id = "c", Name = "空的" });
                stampOnly.Slots["005"] = new SlotRecord { Length = 3, Sha256 = TestStamps.ShaA };
                Store(folder).Save(stampOnly);
                SaveSession.Open(folder, warnings.Add);
                Check.False(Store(folder).Load().Slots.ContainsKey("005"));
                Check.False(warnings.Any(w => w.Contains("left as it is")));
            }));

            test("length_same_content_changed_drops_record", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 500, seed: 1);
                TestStamps.PutSave(folder, "002", 500, seed: 4);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "old");
                session.Catalog.Assign("001", cat);
                session.Catalog.SetName("002", "untouched");
                session.Persist();

                // Replaced outside the game with a file of the same length.
                var when = File.GetLastWriteTimeUtc(a);
                TestStamps.PutSave(folder, "001", 500, seed: 2);
                File.SetLastWriteTimeUtc(a, when.AddSeconds(60));

                session.ListKeys(null);
                Check.Null(session.Catalog.NameOf("001"));
                Check.Null(session.Catalog.CategoryOf("001"));
                Check.Equal("untouched", session.Catalog.NameOf("002"));

                var onDisk = Store(folder).Load();
                Check.SetEqual(new[] { "002" }, onDisk.Slots.Keys);

                // A fresh machine (no cache) reaches the same verdict.
                Directory.Delete(SaveSession.LocalFolderFor(folder), true);
                var other = SaveSession.Open(folder, warnings.Add);
                Check.Null(other.Catalog.NameOf("001"));
                Check.Equal("untouched", other.Catalog.NameOf("002"));
            }));

            test("own_write_refreshes_hash_and_keeps_metadata", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 500, seed: 1);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "告白前");
                session.Catalog.Assign("001", cat);
                session.Persist();

                // The game rewrites the slot; even if length and write time come out identical, the new hash is taken.
                var when = File.GetLastWriteTimeUtc(a);
                var ticket = session.BeforeSave("001");
                TestStamps.PutSave(folder, "001", 500, seed: 2);
                File.SetLastWriteTimeUtc(a, when);
                session.AfterSave(ticket, null);

                var newSha = TestStamps.Sha256OfFile(a);
                Check.Equal("告白前", session.Catalog.NameOf("001"));
                Check.Equal(cat, session.Catalog.CategoryOf("001"));
                var onDisk = Store(folder).Load();
                Check.Equal(newSha, onDisk.Slots["001"].Sha256);
                Check.Equal(500L, onDisk.Slots["001"].Length);

                // The local cache carries the new hash for the current (length, ticks).
                var cache = new LocalStampCache(SaveSession.LocalCachePathFor(folder));
                Check.Equal(newSha, cache.Resolve("001", new FileInfo(a)).Sha256);
                Check.Equal(0, cache.Computed);

                var restarted = SaveSession.Open(folder, warnings.Add);
                Check.Equal("告白前", restarted.Catalog.NameOf("001"));
                Check.Equal(cat, restarted.Catalog.CategoryOf("001"));

                // A new game written into an empty slot is hashed and filed too.
                TestStamps.PutSave(folder, "002", 700, seed: 3);
                restarted.AfterNewGame("002", cat);
                Check.Equal(TestStamps.Sha256OfFile(Path.Combine(folder, "Save_002.dat")), Store(folder).Load().Slots["002"].Sha256);
                Check.Equal(0, warnings.Count);
            }));

            test("scan_failure_after_an_own_write_keeps_the_name", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 500, seed: 1);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "告白前");
                session.Catalog.Assign("001", cat);
                session.Persist();

                var ticket = session.BeforeSave("001");
                TestStamps.PutSave(folder, "001", 600, seed: 2);
                using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    session.AfterSave(ticket, null); // the scan cannot hash the locked file

                Check.Equal(1, warnings.Count(w => w.Contains("after saving slot 001")));
                Check.Equal("告白前", session.Catalog.NameOf("001"));

                // The next successful scan records it as our own write instead of a replaced file.
                Check.SetEqual(new[] { "001" }, session.ListKeys(cat).Keys);
                Check.Equal("告白前", session.Catalog.NameOf("001"));
                var onDisk = Store(folder).Load();
                Check.Equal(TestStamps.Sha256OfFile(a), onDisk.Slots["001"].Sha256);
                Check.Equal(600L, onDisk.Slots["001"].Length);
                Check.Equal("告白前", SaveSession.Open(folder, warnings.Add).Catalog.NameOf("001"));
            }));

            test("newer_version_is_read_only_and_untouched", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var a = TestStamps.PutSave(folder, "001", 500, seed: 1);
                var path = SaveSession.IndexPathFor(folder);
                var json = "{\"Version\":3,\"Categories\":[{\"Id\":\"c\",\"Name\":\"新版\",\"Order\":0}],\"NewestFirst\":false,"
                    + "\"Slots\":{\"001\":{\"Name\":\"from v3\",\"CategoryId\":\"c\",\"Length\":500,\"Sha256\":\"" + TestStamps.Sha256OfFile(a) + "\"}},"
                    + "\"Future\":{\"x\":1}}";
                File.WriteAllText(path, json);
                var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, marker);

                var session = SaveSession.Open(folder, warnings.Add);

                Check.False(session.CanPersist);
                Check.True(warnings.Any(w => w.Contains("newer version")));
                Check.False(session.Persist());
                Check.True(warnings.Any(w => w.Contains("written by a newer version of this mod") && w.Contains("will not be saved")));
                Check.False(warnings.Any(w => w.Contains("could not be read")));
                Check.Null(session.Catalog.NameOf("001"));
                Check.Equal(0, session.Catalog.UserCategories().Count);
                Check.SetEqual(new[] { "001" }, session.ListKeys(null).Keys);

                session.Catalog.CreateCategory("x");
                session.Catalog.SetName("001", "renamed");
                Check.False(session.Persist());
                var ticket = session.BeforeSave("002");
                TestStamps.PutSave(folder, "002", 300, seed: 2);
                session.AfterSave(ticket, null);
                TestStamps.PutSave(folder, "003", 300, seed: 3);
                session.AfterNewGame("003", null);
                File.Delete(Path.Combine(folder, "Save_002.dat"));
                session.AfterDelete("002");

                Check.Equal(json, File.ReadAllText(path));
                Check.Equal(marker, File.GetLastWriteTimeUtc(path));
                Check.Equal(0, Directory.GetFiles(folder, "*corrupt*", SearchOption.AllDirectories).Length);
                Check.Equal(0, Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories).Length);
                Check.Equal("LoMSaveManager.json,Save_001.dat,Save_003.dat", string.Join(",", TestStamps.RootFileNames(folder)));
            }));

            test("no_stray_files_in_synced_root", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 500, seed: 1);
                TestStamps.PutSave(folder, "002", 500, seed: 2);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "n");
                session.Persist();

                var ticket = session.BeforeSave("003");
                TestStamps.PutSave(folder, "003", 400, seed: 3);
                session.AfterSave(ticket, cat);
                TestStamps.PutSave(folder, "004", 400, seed: 4);
                session.AfterNewGame("004", cat);
                File.Delete(Path.Combine(folder, "Save_002.dat"));
                session.AfterDelete("002");

                // A write that fails half-way leaves its temp file in the local folder, not the root.
                var tmp = Path.Combine(SaveSession.LocalFolderFor(folder), "LoMSaveManager.json.tmp");
                Directory.CreateDirectory(tmp);
                session.Catalog.SetName("003", "fails");
                Check.False(session.Persist());
                Directory.Delete(tmp);

                // A corrupt synced file is moved into the local folder.
                File.WriteAllText(SaveSession.IndexPathFor(folder), "{not json");
                var reopened = SaveSession.Open(folder, warnings.Add);
                Check.True(reopened.CanPersist);
                reopened.Catalog.SetName("001", "again");
                Check.True(reopened.Persist());

                Check.Equal("LoMSaveManager.json,Save_001.dat,Save_003.dat,Save_004.dat", string.Join(",", TestStamps.RootFileNames(folder)));
                Check.SetEqual(new[] { "LoMSaveManager" }, Directory.GetDirectories(folder).Select(Path.GetFileName));
                var local = SaveSession.LocalFolderFor(folder);
                Check.True(File.Exists(SaveSession.LocalCachePathFor(folder)));
                Check.Equal(1, Directory.GetFiles(local, "LoMSaveManager.json.corrupt-*").Length);
                Check.Equal(0, Directory.GetFiles(local, "*.tmp").Length);
            }));

            test("corrupt_local_cache_recomputes_hashes", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 500, seed: 1);
                TestStamps.PutSave(folder, "002", 600, seed: 2);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "one");
                session.Catalog.Assign("002", cat);
                session.Persist();
                var path = SaveSession.IndexPathFor(folder);
                var synced = File.ReadAllBytes(path);
                var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, marker);

                var cachePath = SaveSession.LocalCachePathFor(folder);
                foreach (var garbage in new[] { "{garbage", "[]", "{\"Version\":1,\"Entries\":{\"001\":{\"Length\":500,\"WriteTicks\":0,\"Sha256\":\"zz\"}}}" })
                {
                    File.WriteAllText(cachePath, garbage);
                    var reopened = SaveSession.Open(folder, warnings.Add);
                    Check.Equal("one", reopened.Catalog.NameOf("001"));
                    Check.Equal(cat, reopened.Catalog.CategoryOf("002"));
                    Check.True(synced.SequenceEqual(File.ReadAllBytes(path)));
                    Check.Equal(marker, File.GetLastWriteTimeUtc(path));
                    // The cache was rebuilt.
                    Check.Null(new LocalStampCache(cachePath).LoadProblem);
                }
            }));
        }

        private static string HoldPath(string folder)
        {
            return Path.Combine(SaveSession.LocalFolderFor(folder), SaveSession.HeldNamesFileName);
        }

        /// <summary>Puts a save per key, names each one through a session, and returns the synced file's path.</summary>
        private static string NameOnlySaves(string folder, params string[] keys)
        {
            for (var i = 0; i < keys.Length; i++) TestStamps.PutSave(folder, keys[i], 500 + i, seed: i + 1);
            var session = SaveSession.Open(folder, w => { });
            session.ListKeys(null);
            foreach (var key in keys) session.Catalog.SetName(key, "name " + key);
            Check.True(session.Persist());
            return SaveSession.IndexPathFor(folder);
        }

        private static SaveIndexStore Store(string folder)
        {
            return new SaveIndexStore(SaveSession.IndexPathFor(folder), SaveSession.LocalFolderFor(folder));
        }

        private static void ShiftToWholeSecondPlusOne(string file)
        {
            var ticks = File.GetLastWriteTimeUtc(file).Ticks;
            var whole = ticks - ticks % TimeSpan.TicksPerSecond;
            File.SetLastWriteTimeUtc(file, new DateTime(whole, DateTimeKind.Utc).AddSeconds(1));
        }
    }
}
