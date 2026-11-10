using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    /// <summary>
    /// SaveSession is the Unity-free half of the plugin runtime: it owns index.json for one save folder and
    /// holds the bookkeeping the SaveSystem hooks run. These tests play the game with plain files.
    /// </summary>
    internal static class SaveSessionTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("index_path_is_the_synced_root_and_local_files_live_in_the_mod_subfolder", () =>
            {
                var folder = Path.Combine(Path.GetTempPath(), "76561190000000000");
                Check.Equal(Path.Combine(folder, "LoMSaveManager.json"), SaveSession.IndexPathFor(folder));
                Check.Equal(Path.Combine(folder, "LoMSaveManager"), SaveSession.LocalFolderFor(folder));
                Check.Equal(Path.Combine(folder, "LoMSaveManager", "local-cache.json"), SaveSession.LocalCachePathFor(folder));
                Check.Equal(Path.Combine(folder, "LoMSaveManager", "index.json"), SaveSession.LegacyIndexPathFor(folder));
                Check.Throws<ArgumentException>(() => SaveSession.IndexPathFor(""));
                Check.Throws<ArgumentException>(() => SaveSession.LocalFolderFor(null));
            });

            test("open_reconciles_the_index_with_the_save_folder_and_writes_it_back", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                var stamp = Stamp(folder, "001");
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = "kept", Length = stamp.Length, Sha256 = stamp.Sha256 };
                seed.Slots["002"] = new SlotRecord { Name = "file is gone", Length = 5, Sha256 = TestStamps.ShaA };
                Store(folder).Save(seed);

                var session = SaveSession.Open(folder, warnings.Add);

                Check.Equal("kept", session.Catalog.NameOf("001"));
                Check.Null(session.Catalog.NameOf("002"));
                Check.Equal(0, warnings.Count);
                var onDisk = Store(folder).Load();
                Check.SetEqual(new[] { "001" }, onDisk.Slots.Keys);
            }));

            test("open_on_a_fresh_folder_creates_no_synced_file_until_the_first_real_edit", () => WithFolder((folder, warnings) =>
            {
                // An empty LoMSaveManager.json could win a Steam Cloud conflict and wipe the populated cloud copy.
                PutSave(folder, "001", 10);
                var session = SaveSession.Open(folder, warnings.Add);
                Check.True(session.CanPersist);
                Check.Equal(0, session.Catalog.UserCategories().Count);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, warnings.Count);

                session.ListKeys(null);
                Check.True(session.Persist());
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));

                session.Catalog.CreateCategory("Main");
                Check.True(session.Persist());
                Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
            }));

            test("an_existing_empty_index_is_still_kept_in_sync", () => WithFolder((folder, warnings) =>
            {
                Store(folder).Save(new SaveIndex());
                var session = SaveSession.Open(folder, warnings.Add);
                var id = session.Catalog.CreateCategory("Main");
                Check.True(session.Persist());
                session.Catalog.DeleteCategory(id);
                Check.True(session.Persist());
                Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, Store(folder).Load().Categories.Count); // the deletion itself reaches the file
            }));

            test("corrupt_index_is_quarantined_reported_and_the_session_can_still_save", () => WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "{not json");

                var session = SaveSession.Open(folder, warnings.Add);

                Check.True(session.CanPersist);
                Check.Equal(1, warnings.Count(w => w.Contains("corrupt")));
                Check.False(File.Exists(path)); // moved aside; an empty replacement is not written yet
                Check.Equal(1, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "LoMSaveManager.json.corrupt-*").Length);
                Check.True(session.Persist());
                session.Catalog.CreateCategory("Main");
                Check.True(session.Persist());
                Check.False(File.ReadAllText(path).Contains("not json")); // the first real edit writes the replacement
            }));

            test("unreadable_index_is_never_overwritten_and_the_warning_comes_once", () => WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = "keep me", Length = 1, Sha256 = TestStamps.ShaA };
                Store(folder).Save(seed);
                var before = File.ReadAllBytes(path);

                SaveSession session;
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    session = SaveSession.Open(folder, warnings.Add);
                    Check.False(session.CanPersist);
                }

                // The lock is gone, but this session started without the metadata: it must stay hands-off all session.
                session.Catalog.CreateCategory("A new category");
                Check.False(session.Persist());
                Check.False(session.Persist());
                var ticket = session.BeforeSave("002");
                File.WriteAllBytes(Path.Combine(folder, "Save_002.dat"), new byte[] { 1, 2 });
                session.AfterSave(ticket, null);
                session.AfterDelete("002");

                Check.Equal(1, warnings.Count(w => w.Contains("will not be saved")));
                Check.Equal(1, warnings.Count(w => w.StartsWith("LoMSaveManager.json could not be read")));
                Check.True(before.SequenceEqual(File.ReadAllBytes(path)));
                Check.Equal(0, Directory.GetFiles(folder, "*.tmp").Length);
                Check.Equal(0, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "*.tmp").Length);
                Check.Equal(0, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "LoMSaveManager.json.corrupt-*").Length);
                Check.Equal("keep me", Store(folder).Load().Slots["001"].Name);
            }));

            test("persist_reports_a_write_failure_once_and_never_throws", () => WithFolder((folder, warnings) =>
            {
                var session = SaveSession.Open(folder, warnings.Add);
                var tmp = Path.Combine(SaveSession.LocalFolderFor(folder), "LoMSaveManager.json.tmp");
                // A directory squatting on the temp file name makes every write fail.
                Directory.CreateDirectory(tmp);
                session.Catalog.CreateCategory("a"); // unchanged content is never rewritten, so change something

                Check.False(session.Persist());
                Check.False(session.Persist());
                Check.Equal(1, warnings.Count(w => w.Contains("could not write")));

                Directory.Delete(tmp);
                Check.True(session.Persist());
                Directory.CreateDirectory(tmp);
                session.Catalog.CreateCategory("b");
                Check.False(session.Persist());
                Check.Equal(2, warnings.Count(w => w.Contains("could not write")));
            }));

            test("an_overwrite_through_the_game_keeps_the_name_and_survives_a_restart", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                PutSave(folder, "002", 20);
                var session = SaveSession.Open(folder, warnings.Add);
                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.SetName("001", "告白前");
                session.Catalog.Assign("001", cat);
                session.Persist();

                var ticket = session.BeforeSave("001");
                Rewrite(folder, "001", 33); // what SaveSystem.SaveGameData("001") does
                session.AfterSave(ticket, null);

                Check.Equal("告白前", session.Catalog.NameOf("001"));
                Check.Equal(cat, session.Catalog.CategoryOf("001"));

                var restarted = SaveSession.Open(folder, warnings.Add);
                Check.Equal("告白前", restarted.Catalog.NameOf("001"));
                Check.Equal(cat, restarted.Catalog.CategoryOf("001"));
                Check.Equal(0, warnings.Count);
            }));

            test("a_slot_deleted_outside_and_reused_through_the_game_loses_the_old_name", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                var session = SaveSession.Open(folder, warnings.Add);
                var pending = session.Catalog.CreateCategory("支線");
                session.Catalog.SetName("001", "舊的名字");
                session.Persist();

                File.Delete(Path.Combine(folder, "Save_001.dat")); // outside the game
                var ticket = session.BeforeSave("001");
                PutSave(folder, "001", 40);
                session.AfterSave(ticket, pending);

                Check.Null(session.Catalog.NameOf("001"));
                Check.Equal(pending, session.Catalog.CategoryOf("001"));
                var restarted = SaveSession.Open(folder, warnings.Add);
                Check.Null(restarted.Catalog.NameOf("001"));
                Check.Equal(pending, restarted.Catalog.CategoryOf("001"));
            }));

            test("a_new_slot_saved_through_the_game_joins_the_pending_category", () => WithFolder((folder, warnings) =>
            {
                var session = SaveSession.Open(folder, warnings.Add);
                var pending = session.Catalog.CreateCategory("支線");

                var ticket = session.BeforeSave("001");
                PutSave(folder, "001", 12);
                session.AfterSave(ticket, pending);

                Check.Equal(pending, session.Catalog.CategoryOf("001"));
                var restarted = SaveSession.Open(folder, warnings.Add);
                Check.Equal(pending, restarted.Catalog.CategoryOf("001"));
            }));

            test("only_manual_slots_are_bookkept_around_a_save", () => WithFolder((folder, warnings) =>
            {
                Store(folder).Save(new SaveIndex());
                var session = SaveSession.Open(folder, warnings.Add);
                var path = SaveSession.IndexPathFor(folder);
                var written = File.GetLastWriteTimeUtc(path);
                File.SetLastWriteTimeUtc(path, written.AddMinutes(-5));
                var marker = File.GetLastWriteTimeUtc(path);

                Check.Null(session.BeforeSave("auto"));
                Check.Null(session.BeforeSave("universe"));
                Check.Null(session.BeforeSave(""));
                Check.Null(session.BeforeSave(null));
                session.AfterSave(null, null);
                session.AfterDelete("auto");
                session.AfterDelete(null);

                Check.Equal(marker, File.GetLastWriteTimeUtc(path)); // nothing was rewritten
            }));

            test("a_delete_through_the_game_forgets_the_name_and_persists", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                PutSave(folder, "002", 10);
                var session = SaveSession.Open(folder, warnings.Add);
                session.Catalog.SetName("001", "bye");
                session.Catalog.SetName("002", "stay");
                session.Persist();

                File.Delete(Path.Combine(folder, "Save_001.dat")); // SaveSystem.DeleteSaveData("001")
                session.AfterDelete("001");

                Check.Null(session.Catalog.NameOf("001"));
                var onDisk = Store(folder).Load();
                Check.SetEqual(new[] { "002" }, onDisk.Slots.Keys);
            }));

            test("a_delete_that_left_the_file_in_place_keeps_the_name_and_category", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                var session = SaveSession.Open(folder, warnings.Add);
                var category = session.Catalog.CreateCategory("keep");
                session.Catalog.SetName("001", "still here");
                session.Catalog.Assign("001", category);
                session.Persist();

                // SaveSystem.DeleteSaveData("001") failed (file locked, I/O error): the file is still there.
                session.AfterDelete("001");

                Check.Equal("still here", session.Catalog.NameOf("001"));
                Check.Equal(category, session.Catalog.CategoryOf("001"));
                var onDisk = Store(folder).Load();
                Check.Equal("still here", onDisk.Slots["001"].Name);
            }));

            test("index_is_empty_when_its_slot_records_carry_only_stamps", () =>
            {
                var index = new SaveIndex();
                Check.True(index.IsEmpty);
                index.Slots["001"] = new SlotRecord { Length = 10, Sha256 = TestStamps.ShaA };
                index.Slots["002"] = new SlotRecord { Name = "", Length = 5, Sha256 = TestStamps.ShaB };
                Check.True(index.IsEmpty); // stamps alone are bookkeeping, not something the user made

                index.Slots["001"].Name = "named";
                Check.False(index.IsEmpty);
                index.Slots["001"].Name = null;
                index.Slots["001"].CategoryId = "c";
                Check.False(index.IsEmpty);
                index.Slots["001"].CategoryId = null;
                index.NewestFirst = false;
                Check.False(index.IsEmpty);
                index.NewestFirst = true;
                index.Categories.Add(new CategoryRecord { Id = "c", Name = "Main" });
                Check.False(index.IsEmpty);
            });

            test("a_manual_save_into_a_fresh_folder_creates_no_synced_file", () => WithFolder((folder, warnings) =>
            {
                // Every in-game save records the slot's stamp; that alone must not create the file Steam Cloud syncs.
                PutSave(folder, "001", 10);
                var session = SaveSession.Open(folder, warnings.Add);

                var ticket = session.BeforeSave("002"); // a brand-new slot
                PutSave(folder, "002", 20);
                session.AfterSave(ticket, null);
                ticket = session.BeforeSave("001"); // an overwrite of an unnamed slot
                Rewrite(folder, "001", 30);
                session.AfterSave(ticket, null);

                Check.True(session.Persist());
                session.ListKeys(null);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, warnings.Count);
            }));

            test("a_new_game_into_a_fresh_folder_creates_no_synced_file", () => WithFolder((folder, warnings) =>
            {
                var session = SaveSession.Open(folder, warnings.Add);
                PutSave(folder, "001", 12); // what SaveSystem.NewGameData() writes
                session.AfterNewGame("001", null);
                session.AfterNewGame("002", "no-such-category"); // a stale pending category files nothing

                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.Equal(0, warnings.Count);
            }));

            test("naming_or_filing_after_an_unnamed_save_creates_the_file_with_the_saves_stamp", () => WithFolder((folder, warnings) =>
            {
                var session = SaveSession.Open(folder, warnings.Add);
                var ticket = session.BeforeSave("001");
                PutSave(folder, "001", 12);
                session.AfterSave(ticket, null);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));

                session.Catalog.SetName("001", "告白前");
                Check.True(session.Persist());
                Check.True(File.Exists(SaveSession.IndexPathFor(folder)));
                var stamp = Stamp(folder, "001");
                var record = Store(folder).Load().Slots["001"];
                Check.Equal("告白前", record.Name);
                Check.Equal(stamp.Length, record.Length);
                Check.Equal(stamp.Sha256, record.Sha256);

                var restarted = SaveSession.Open(folder, warnings.Add);
                Check.Equal("告白前", restarted.Catalog.NameOf("001"));
                Check.Equal(0, warnings.Count);
            }));

            test("creating_a_category_after_a_new_game_creates_the_file_with_the_saves_stamp", () => WithFolder((folder, warnings) =>
            {
                var session = SaveSession.Open(folder, warnings.Add);
                PutSave(folder, "001", 12);
                session.AfterNewGame("001", null);
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));

                var cat = session.Catalog.CreateCategory("主線");
                session.Catalog.Assign("001", cat);
                Check.True(session.Persist());
                var stamp = Stamp(folder, "001");
                var onDisk = Store(folder).Load();
                Check.Equal(1, onDisk.Categories.Count);
                Check.Equal(cat, onDisk.Slots["001"].CategoryId);
                Check.Equal(stamp.Length, onDisk.Slots["001"].Length);
                Check.Equal(stamp.Sha256, onDisk.Slots["001"].Sha256);
                Check.Equal(cat, SaveSession.Open(folder, warnings.Add).Catalog.CategoryOf("001"));
            }));

            test("an_existing_synced_file_is_still_updated_by_an_unnamed_save", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "002", 20);
                var stamp2 = Stamp(folder, "002");
                var seed = new SaveIndex();
                seed.Categories.Add(new CategoryRecord { Id = "c", Name = "Main" });
                seed.Slots["002"] = new SlotRecord { Name = "kept", CategoryId = "c", Length = stamp2.Length, Sha256 = stamp2.Sha256 };
                Store(folder).Save(seed);
                var session = SaveSession.Open(folder, warnings.Add);

                var ticket = session.BeforeSave("001");
                PutSave(folder, "001", 12);
                session.AfterSave(ticket, null);

                var stamp = Stamp(folder, "001");
                var onDisk = Store(folder).Load();
                Check.SetEqual(new[] { "001", "002" }, onDisk.Slots.Keys);
                Check.Null(onDisk.Slots["001"].Name);
                Check.Null(onDisk.Slots["001"].CategoryId);
                Check.Equal(stamp.Length, onDisk.Slots["001"].Length);
                Check.Equal(stamp.Sha256, onDisk.Slots["001"].Sha256);
                Check.Equal("kept", onDisk.Slots["002"].Name);
                Check.Equal(0, warnings.Count);
            }));

            test("an_existing_file_holding_only_stamps_is_still_kept_in_sync", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Length = 10, Sha256 = Stamp(folder, "001").Sha256 };
                Store(folder).Save(seed);
                var session = SaveSession.Open(folder, warnings.Add);

                var ticket = session.BeforeSave("001");
                Rewrite(folder, "001", 30);
                session.AfterSave(ticket, null);

                var stamp = Stamp(folder, "001");
                var record = Store(folder).Load().Slots["001"];
                Check.Equal(stamp.Length, record.Length);
                Check.Equal(stamp.Sha256, record.Sha256);
            }));

            test("scan_and_occupied_keys_read_the_save_folder", () => WithFolder((folder, warnings) =>
            {
                PutSave(folder, "001", 10);
                File.WriteAllBytes(Path.Combine(folder, "Save_002.dat.bak"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(folder, "Save_auto.dat"), new byte[] { 1 });
                var session = SaveSession.Open(folder, warnings.Add);

                Check.SetEqual(new[] { "001" }, session.ScanManualSaves().Keys);
                Check.SetEqual(new[] { "001", "002" }, session.OccupiedKeys());
                Check.Equal("003", SlotAllocator.LowestFree(session.OccupiedKeys()));
            }));
        }

        private static void WithFolder(Action<string, List<string>> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, "76561190000000000");
            Directory.CreateDirectory(folder);
            try { body(folder, new List<string>()); }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static SaveIndexStore Store(string folder)
        {
            return new SaveIndexStore(SaveSession.IndexPathFor(folder), SaveSession.LocalFolderFor(folder));
        }

        private static void PutSave(string folder, string key, int length)
        {
            File.WriteAllBytes(Path.Combine(folder, "Save_" + key + ".dat"), Enumerable.Repeat((byte)9, length).ToArray());
        }

        /// <summary>Overwrites a save with a different length and a later write time, like a real second save.</summary>
        private static void Rewrite(string folder, string key, int length)
        {
            var file = Path.Combine(folder, "Save_" + key + ".dat");
            var previous = File.GetLastWriteTimeUtc(file);
            PutSave(folder, key, length);
            File.SetLastWriteTimeUtc(file, previous.AddSeconds(60));
        }

        private static SaveStamp Stamp(string folder, string key)
        {
            return SaveStamp.Compute(new FileInfo(Path.Combine(folder, "Save_" + key + ".dat")));
        }
    }
}
