using System;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class SaveIndexStoreTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("missing_index_loads_empty", () => WithTempDir(root =>
            {
                var store = new SaveIndexStore(Path.Combine(root, "LoMSaveManager", "index.json"));
                var index = store.Load();
                Check.Equal(2, index.Version);
                Check.Equal(0, index.Categories.Count);
                Check.Equal(0, index.Slots.Count);
                Check.True(index.NewestFirst);
                Check.Null(store.LastLoadProblem);
            }));

            test("round_trip_preserves_categories_names_and_stamps", () => WithTempDir(root =>
            {
                var path = Path.Combine(root, "LoMSaveManager", "index.json");
                var cat = new CategoryRecord { Id = Guid.NewGuid().ToString("N"), Name = "主線", Order = 3 };
                var cat2 = new CategoryRecord { Id = Guid.NewGuid().ToString("N"), Name = "Side \"quests\"", Order = 1 };
                var index = new SaveIndex { NewestFirst = false };
                index.Categories.Add(cat);
                index.Categories.Add(cat2);
                index.Slots["001"] = new SlotRecord { Name = "第三年春・告白前", CategoryId = cat.Id, Length = 123456789012L, Sha256 = TestStamps.ShaA };
                index.Slots["1000"] = new SlotRecord { Name = "", CategoryId = null, Length = 7, Sha256 = TestStamps.ShaB };
                new SaveIndexStore(path).Save(index);

                var store = new SaveIndexStore(path);
                var loaded = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.Equal(2, loaded.Version);
                Check.False(loaded.NewestFirst);
                Check.Equal(2, loaded.Categories.Count);
                Check.Equal(cat.Id, loaded.Categories[0].Id);
                Check.Equal("主線", loaded.Categories[0].Name);
                Check.Equal(3, loaded.Categories[0].Order);
                Check.Equal(cat2.Id, loaded.Categories[1].Id);
                Check.Equal("Side \"quests\"", loaded.Categories[1].Name);
                Check.Equal(1, loaded.Categories[1].Order);
                Check.SetEqual(new[] { "001", "1000" }, loaded.Slots.Keys);
                var a = loaded.Slots["001"];
                Check.Equal("第三年春・告白前", a.Name);
                Check.Equal(cat.Id, a.CategoryId);
                Check.Equal(123456789012L, a.Length);
                Check.Equal(TestStamps.ShaA, a.Sha256);
                var b = loaded.Slots["1000"];
                Check.Equal("", b.Name);
                Check.Null(b.CategoryId);
                Check.Equal(7L, b.Length);
                Check.Equal(TestStamps.ShaB, b.Sha256);
            }));

            test("save_creates_directory", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                Check.False(Directory.Exists(dir));
                new SaveIndexStore(Path.Combine(dir, "index.json")).Save(new SaveIndex());
                Check.True(File.Exists(Path.Combine(dir, "index.json")));
            }));

            test("save_is_atomic_and_leaves_no_tmp", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                File.WriteAllText(path + ".tmp", "stale garbage from a crashed save");

                var store = new SaveIndexStore(path);
                var first = new SaveIndex();
                first.Slots["001"] = new SlotRecord { Name = "one", Length = 1, Sha256 = TestStamps.ShaA };
                store.Save(first); // target missing: Move path
                Check.SetEqual(new[] { "index.json" }, Directory.GetFiles(dir).Select(Path.GetFileName));
                Check.Equal("one", store.Load().Slots["001"].Name);

                File.WriteAllText(path + ".tmp", "stale garbage again");
                var second = new SaveIndex();
                second.Slots["001"] = new SlotRecord { Name = "two", Length = 2, Sha256 = TestStamps.ShaB };
                store.Save(second); // target exists: Replace path
                Check.SetEqual(new[] { "index.json" }, Directory.GetFiles(dir).Select(Path.GetFileName));
                Check.Equal("two", store.Load().Slots["001"].Name);
                Check.Null(store.LastLoadProblem);
            }));

            test("corrupt_index_is_quarantined_and_starts_empty", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{not json");

                var store = new SaveIndexStore(path);
                var index = store.Load();
                Check.Equal(2, index.Version);
                Check.Equal(0, index.Categories.Count);
                Check.Equal(0, index.Slots.Count);
                Check.True(store.LastLoadProblem != null);
                Check.False(File.Exists(path));
                var quarantined = Directory.GetFiles(dir, "index.json.corrupt-*");
                Check.Equal(1, quarantined.Length);
                Check.Equal("{not json", File.ReadAllText(quarantined[0]));

                // the store keeps working afterwards and a clean load clears the problem
                store.Save(index);
                store.Load();
                Check.Null(store.LastLoadProblem);
            }));

            test("empty_or_non_object_index_file_is_quarantined", () => WithTempDir(root =>
            {
                var n = 0;
                foreach (var content in new[] { "", "null", "[]", "42" })
                {
                    var dir = Path.Combine(root, "case" + (n++), "LoMSaveManager");
                    var path = Path.Combine(dir, "index.json");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, content);
                    var store = new SaveIndexStore(path);
                    var index = store.Load();
                    Check.Equal(0, index.Slots.Count);
                    Check.True(store.LastLoadProblem != null);
                    Check.False(File.Exists(path));
                    Check.Equal(1, Directory.GetFiles(dir, "index.json.corrupt-*").Length);
                }
            }));

            test("future_version_is_read_only_and_not_quarantined", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                var json = "{\"Version\":99,\"NewestFirst\":true,\"Categories\":[],\"Slots\":{\"001\":{\"Name\":\"x\",\"CategoryId\":null,\"Length\":1,\"Sha256\":\"ab\"}}}";
                File.WriteAllText(path, json);

                var store = new SaveIndexStore(path);
                var index = store.Load();
                Check.Equal(2, index.Version);
                Check.Equal(0, index.Slots.Count);
                Check.True(store.LastLoadProblem != null && store.LastLoadProblem.Contains("newer version"));
                Check.False(store.CanSave);
                Check.Throws<InvalidOperationException>(() => store.Save(new SaveIndex()));
                Check.Equal(json, File.ReadAllText(path));
                Check.Equal(0, Directory.GetFiles(dir, "index.json.corrupt-*").Length);
            }));

            test("future_version_with_a_changed_schema_is_read_only_and_not_quarantined", () => WithTempDir(root =>
            {
                // A v3 that reshapes a member (Slots as an array) cannot be read as v2; it must still be
                // recognized as newer by its Version alone, never treated as corrupt and replaced.
                var work = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(root, "LoMSaveManager.json");
                var json = "{\"Version\":3,\"NewestFirst\":true,\"Categories\":[],\"Slots\":[\"001\",\"002\"]}";
                File.WriteAllText(path, json);

                var store = new SaveIndexStore(path, work);
                var index = store.Load();
                Check.Equal(0, index.Slots.Count);
                Check.True(store.LastLoadProblem != null && store.LastLoadProblem.Contains("newer version"));
                Check.True(store.LoadedNewerVersion);
                Check.False(store.CanSave);
                Check.Throws<InvalidOperationException>(() => store.Save(new SaveIndex()));
                Check.Equal(json, File.ReadAllText(path));
                Check.False(Directory.Exists(work) && Directory.GetFiles(work, "LoMSaveManager.json.corrupt-*").Length > 0);
            }));

            test("unreadable_file_claiming_the_current_version_is_still_quarantined", () => WithTempDir(root =>
            {
                var work = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(root, "LoMSaveManager.json");
                File.WriteAllText(path, "{\"Version\":2,\"Categories\":[],\"Slots\":[\"001\"]}");

                var store = new SaveIndexStore(path, work);
                store.Load();
                Check.False(store.LoadedNewerVersion);
                Check.True(store.CanSave);
                Check.False(File.Exists(path));
                Check.Equal(1, Directory.GetFiles(work, "LoMSaveManager.json.corrupt-*").Length);
            }));

            test("tmp_and_quarantine_go_to_the_work_directory", () => WithTempDir(root =>
            {
                var work = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(root, "LoMSaveManager.json");
                File.WriteAllText(path, "{not json");

                var store = new SaveIndexStore(path, work);
                store.Load();
                Check.True(store.CanSave);
                Check.False(File.Exists(path));
                Check.Equal(1, Directory.GetFiles(work, "LoMSaveManager.json.corrupt-*").Length);

                File.WriteAllText(Path.Combine(work, "LoMSaveManager.json.tmp"), "stale");
                store.Save(new SaveIndex());
                Check.True(File.Exists(path));
                Check.SetEqual(new[] { "LoMSaveManager.json" }, Directory.GetFiles(root).Select(Path.GetFileName));
                Check.False(File.Exists(Path.Combine(work, "LoMSaveManager.json.tmp")));
            }));

            test("save_with_unchanged_content_does_not_rewrite_the_file", () => WithTempDir(root =>
            {
                var path = Path.Combine(root, "LoMSaveManager.json");
                var store = new SaveIndexStore(path, Path.Combine(root, "LoMSaveManager"));
                var index = new SaveIndex();
                index.Slots["001"] = new SlotRecord { Name = "n", Length = 1, Sha256 = TestStamps.ShaA };
                store.Save(index);
                var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, marker);

                store.Save(store.Load());
                Check.Equal(marker, File.GetLastWriteTimeUtc(path)); // nothing new to upload to Steam Cloud

                index.Slots["001"].Name = "changed";
                store.Save(index);
                Check.False(marker == File.GetLastWriteTimeUtc(path));
            }));

            test("locked_index_loads_empty_without_quarantine", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = "keep", Length = 1, Sha256 = TestStamps.ShaA };
                new SaveIndexStore(path).Save(seed);

                var store = new SaveIndexStore(path);
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var index = store.Load();
                    Check.Equal(0, index.Slots.Count);
                    Check.True(store.LastLoadProblem != null);
                }
                Check.True(File.Exists(path));
                Check.Equal(0, Directory.GetFiles(dir, "index.json.corrupt-*").Length);
                // lock released: the original data is still intact
                Check.Equal("keep", store.Load().Slots["001"].Name);
                Check.Null(store.LastLoadProblem);
            }));

            test("can_save_is_true_unless_the_original_file_was_left_in_place_unread", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                var store = new SaveIndexStore(path);
                Check.True(store.CanSave); // before any Load

                store.Load(); // missing file
                Check.True(store.CanSave);

                store.Save(new SaveIndex());
                store.Load();
                Check.True(store.CanSave);

                // A corrupt file that was moved aside leaves the way clear for a fresh index.
                File.WriteAllText(path, "{not json");
                store.Load();
                Check.True(store.LastLoadProblem != null);
                Check.True(store.CanSave);
            }));

            test("locked_index_cannot_be_saved_until_a_clean_load", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = "keep", Length = 1, Sha256 = TestStamps.ShaA };
                new SaveIndexStore(path).Save(seed);

                var store = new SaveIndexStore(path);
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    store.Load();
                    Check.True(store.LastLoadProblem != null);
                    Check.False(store.CanSave);
                }

                Check.False(store.CanSave); // still false: nothing has been read yet
                Check.Equal("keep", store.Load().Slots["001"].Name);
                Check.True(store.CanSave);
            }));

            test("save_refuses_while_can_save_is_false", () => WithTempDir(root =>
            {
                var path = Path.Combine(root, "LoMSaveManager", "index.json");
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = "keep", Length = 1, Sha256 = TestStamps.ShaA };
                new SaveIndexStore(path).Save(seed);
                var before = File.ReadAllBytes(path);

                var store = new SaveIndexStore(path);
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    store.Load();
                Check.False(store.CanSave);

                // The lock is gone, but this store never read the file: writing would drop metadata it never saw.
                Check.Throws<InvalidOperationException>(() => store.Save(new SaveIndex()));
                Check.True(before.SequenceEqual(File.ReadAllBytes(path)));
                Check.False(File.Exists(path + ".tmp"));

                // A clean load makes it writable again.
                store.Load();
                store.Save(new SaveIndex());
                Check.Equal(0, new SaveIndexStore(path).Load().Slots.Count);
            }));

            test("corrupt_index_that_cannot_be_moved_aside_cannot_be_saved", () => WithTempDir(root =>
            {
                // Sharing violations are a Windows behaviour (Mono on Windows is what the game runs).
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{not json");

                var store = new SaveIndexStore(path);
                // Readable by others, but not renamable, so the quarantine move fails.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var index = store.Load();
                    Check.Equal(0, index.Slots.Count);
                    Check.True(store.LastLoadProblem != null);
                    Check.True(File.Exists(path));
                    Check.False(store.CanSave);
                }
            }));

            test("members_missing_from_json_get_defaults", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                foreach (var json in new[] { "{}", "{\"Version\":2}", "{\"Categories\":null,\"Slots\":null}", "{\"Unknown\":5}" })
                {
                    File.WriteAllText(path, json);
                    var store = new SaveIndexStore(path);
                    var index = store.Load();
                    Check.Null(store.LastLoadProblem);
                    Check.Equal(2, index.Version);
                    Check.True(index.NewestFirst);
                    Check.True(index.Categories != null);
                    Check.True(index.Slots != null);
                    Check.Equal(0, index.Categories.Count);
                    Check.Equal(0, index.Slots.Count);
                    Check.True(File.Exists(path));
                }
            }));

            test("utf8_bom_and_leading_whitespace_are_tolerated", () => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "  \r\n{\"Slots\":{\"001\":{\"Name\":\"第三年\",\"Length\":1,\"WriteTicks\":2}}}", new System.Text.UTF8Encoding(true));
                var store = new SaveIndexStore(path);
                var index = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.Equal("第三年", index.Slots["001"].Name);
            }));

            test("null_entries_in_json_are_dropped",() => WithTempDir(root =>
            {
                var dir = Path.Combine(root, "LoMSaveManager");
                var path = Path.Combine(dir, "index.json");
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "{\"Categories\":[null,{\"Id\":\"a\",\"Name\":\"A\",\"Order\":0}],\"Slots\":{\"001\":null,\"002\":{\"Name\":\"n\",\"Length\":1,\"WriteTicks\":2}}}");
                var store = new SaveIndexStore(path);
                var index = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.Equal(1, index.Categories.Count);
                Check.Equal("a", index.Categories[0].Id);
                Check.SetEqual(new[] { "002" }, index.Slots.Keys);
                Check.Null(index.Slots["002"].CategoryId);
            }));

            test("save_stamp_matches_the_record_made_from_it", () => WithTempDir(root =>
            {
                var file = Path.Combine(root, "Save_001.dat");
                File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5 });
                var stamp = SaveStamp.Compute(new FileInfo(file));
                Check.Equal(5L, stamp.Length);
                Check.Equal(File.GetLastWriteTimeUtc(file).Ticks, stamp.WriteTicks);
                // SHA-256 of 01 02 03 04 05, lowercase hex.
                Check.Equal("74f81fe167d99b4cb41d6d0ccda82278caee9f3e2f25d5e5a3936ff3dcec60d0", stamp.Sha256);
                var record = new SlotRecord { Length = stamp.Length, Sha256 = stamp.Sha256 };
                Check.True(stamp.Matches(record));
                Check.True(SaveStamp.Compute(new FileInfo(file)).Matches(record));
            }));

            test("save_stamp_ignores_write_time_and_detects_changed_content", () => WithTempDir(root =>
            {
                var file = Path.Combine(root, "Save_001.dat");
                File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5 });
                var info = new FileInfo(file);
                var stamp = SaveStamp.Compute(info);
                var record = new SlotRecord { Length = stamp.Length, Sha256 = stamp.Sha256 };

                // same bytes, different time (what a Steam Cloud download looks like): still the same save
                File.SetLastWriteTimeUtc(file, new DateTime(stamp.WriteTicks, DateTimeKind.Utc).AddSeconds(5));
                var moved = SaveStamp.Compute(info);
                Check.False(moved.WriteTicks == stamp.WriteTicks);
                Check.True(moved.Matches(record));

                // same length, different bytes
                File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 6 });
                Check.False(SaveStamp.Compute(info).Matches(record));

                // different length
                File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
                Check.False(SaveStamp.Compute(info).Matches(record));

                Check.False(stamp.Matches(null));
            }));
        }

        private static void WithTempDir(Action<string> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { body(root); }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
