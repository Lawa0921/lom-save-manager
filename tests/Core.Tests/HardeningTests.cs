using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LoMSaveManager.Tests
{
    /// <summary>
    /// Loaded metadata is untrusted (hand-edited, damaged, or written by someone else and synced by Steam Cloud):
    /// the UI limits are re-applied on load, oversized files are not read, and user strings never render as rich text.
    /// </summary>
    internal static class HardeningTests
    {
        private const int Cap = SaveIndexStore.MaxFileBytes;

        private static readonly string CapText = JsonFile.Megabytes(SaveIndexStore.MaxFileBytes);

        public static void Run(Action<string, Action> test)
        {
            NormalizeTests(test);
            SizeCapTests(test);
            DisplayTextTests(test);
        }

        private static void NormalizeTests(Action<string, Action> test)
        {
            test("normalize_drops_categories_with_missing_or_duplicate_ids_keeping_the_first", () =>
            {
                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "a", Name = "first a", Order = 0 });
                index.Categories.Add(new CategoryRecord { Id = null, Name = "no id", Order = 1 });
                index.Categories.Add(new CategoryRecord { Id = "", Name = "empty id", Order = 2 });
                index.Categories.Add(new CategoryRecord { Id = "a", Name = "second a", Order = 3 });
                index.Categories.Add(new CategoryRecord { Id = "b", Name = "b", Order = 4 });
                index.Normalize();
                Check.Equal("a,b", string.Join(",", index.Categories.Select(c => c.Id)));
                Check.Equal("first a", index.Categories[0].Name);
            });

            test("normalize_caps_categories_at_the_ui_limit_keeping_the_first_by_order", () =>
            {
                var index = new SaveIndex();
                // Listed in reverse Order, so "first in the list" and "first by Order" differ.
                for (var i = SaveCatalog.MaxCategories + 49; i >= 0; i--)
                    index.Categories.Add(new CategoryRecord { Id = "c" + i, Name = "n" + i, Order = i });
                index.Normalize();
                Check.Equal(SaveCatalog.MaxCategories, index.Categories.Count);
                Check.True(index.Categories.All(c => c.Order < SaveCatalog.MaxCategories));
                Check.Throws<ArgumentException>(() => new SaveCatalog(index).CreateCategory("one more"));
            });

            test("normalize_trims_and_truncates_category_names", () =>
            {
                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "a", Name = "  主線  " });
                index.Categories.Add(new CategoryRecord { Id = "b", Name = new string('長', SaveCatalog.MaxCategoryNameLength + 30) });
                // A surrogate pair straddling the limit is not cut in half.
                index.Categories.Add(new CategoryRecord { Id = "d", Name = new string('x', SaveCatalog.MaxCategoryNameLength - 1) + "😀tail" });
                index.Normalize();
                Check.Equal("主線", index.Categories[0].Name);
                Check.Equal(new string('長', SaveCatalog.MaxCategoryNameLength), index.Categories[1].Name);
                Check.Equal(new string('x', SaveCatalog.MaxCategoryNameLength - 1), index.Categories[2].Name);
            });

            test("normalize_drops_categories_without_a_name_and_uncategorizes_their_saves", () =>
            {
                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "a", Name = "主線", Order = 0 });
                index.Categories.Add(new CategoryRecord { Id = "null", Name = null, Order = 1 });
                index.Categories.Add(new CategoryRecord { Id = "empty", Name = "", Order = 2 });
                index.Categories.Add(new CategoryRecord { Id = "blank", Name = " 　\t ", Order = 3 });
                // A nameless entry does not shadow a named one with the same id that comes later.
                index.Categories.Add(new CategoryRecord { Id = "b", Name = "  ", Order = 4 });
                index.Categories.Add(new CategoryRecord { Id = "b", Name = "支線", Order = 5 });
                index.Slots["001"] = new SlotRecord { Name = "n1", CategoryId = "a", Length = 1, Sha256 = TestStamps.ShaA };
                index.Slots["002"] = new SlotRecord { Name = "n2", CategoryId = "null", Length = 1, Sha256 = TestStamps.ShaA };
                index.Slots["003"] = new SlotRecord { Name = null, CategoryId = "blank", Length = 1, Sha256 = TestStamps.ShaA };
                index.Slots["004"] = new SlotRecord { Name = "n4", CategoryId = "b", Length = 1, Sha256 = TestStamps.ShaA };
                index.Normalize();

                Check.Equal("a,b", string.Join(",", index.Categories.Select(c => c.Id)));
                Check.Equal("支線", index.Categories[1].Name);
                Check.Equal("a", index.Slots["001"].CategoryId);
                Check.Null(index.Slots["002"].CategoryId);
                Check.Equal("n2", index.Slots["002"].Name); // the save keeps its name
                Check.Null(index.Slots["003"].CategoryId);
                Check.Equal("b", index.Slots["004"].CategoryId);

                var catalog = new SaveCatalog(index);
                Check.Equal("主線,支線", string.Join(",", catalog.UserCategories().Select(c => c.Name)));
                Check.Equal(2, catalog.UncategorizedCount(new[] { "001", "002", "003", "004" }));
            });

            test("a_name_ending_in_a_surrogate_pair_exactly_at_the_limit_is_kept_whole", () =>
            {
                var category = new string('x', SaveCatalog.MaxCategoryNameLength - 2) + "😀";
                var slot = new string('名', SaveCatalog.MaxSlotNameLength - 2) + "😀";
                Check.Equal(SaveCatalog.MaxCategoryNameLength, category.Length);
                Check.Equal(SaveCatalog.MaxSlotNameLength, slot.Length);

                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "a", Name = category });
                index.Slots["001"] = new SlotRecord { Name = slot, CategoryId = "a", Length = 1, Sha256 = TestStamps.ShaA };
                index.Normalize();
                Check.Equal(category, index.Categories[0].Name);
                Check.Equal(slot, index.Slots["001"].Name);

                // The UI path accepts them too.
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(new Dictionary<string, SaveStamp> { { "002", new SaveStamp(1, 1, TestStamps.ShaA) } });
                var id = catalog.CreateCategory(category);
                catalog.SetName("002", slot);
                Check.Equal(category, catalog.UserCategories().Single(c => c.Id == id).Name);
                Check.Equal(slot, catalog.NameOf("002"));
            });

            test("normalize_drops_slot_records_whose_key_is_not_a_manual_slot", () =>
            {
                var index = new SaveIndex();
                foreach (var key in new[] { "001", "1000", "auto", "universe", "000", "01", "10000", "", "abc" })
                    index.Slots[key] = new SlotRecord { Name = "n", Length = 1, Sha256 = TestStamps.ShaA };
                index.Normalize();
                Check.SetEqual(new[] { "001", "1000" }, index.Slots.Keys);
            });

            test("normalize_trims_and_truncates_slot_names", () =>
            {
                var index = new SaveIndex();
                index.Slots["001"] = new SlotRecord { Name = new string('名', SaveCatalog.MaxSlotNameLength + 10), Length = 1, Sha256 = TestStamps.ShaA };
                index.Slots["002"] = new SlotRecord { Name = "  spaced  ", Length = 1, Sha256 = TestStamps.ShaA };
                index.Slots["003"] = new SlotRecord { Name = "   ", CategoryId = "x", Length = 1, Sha256 = TestStamps.ShaA };
                index.Normalize();
                Check.Equal(new string('名', SaveCatalog.MaxSlotNameLength), index.Slots["001"].Name);
                Check.Equal("spaced", index.Slots["002"].Name);
                Check.Null(index.Slots["003"].Name);
                Check.Equal("x", index.Slots["003"].CategoryId); // the record itself is kept
            });

            test("normalize_leaves_valid_data_exactly_as_it_was", () =>
            {
                var index = new SaveIndex { NewestFirst = false };
                for (var i = 0; i < SaveCatalog.MaxCategories; i++)
                    index.Categories.Add(new CategoryRecord { Id = "c" + i, Name = new string('分', SaveCatalog.MaxCategoryNameLength), Order = i });
                index.Slots["001"] = new SlotRecord { Name = new string('名', SaveCatalog.MaxSlotNameLength), CategoryId = "c0", Length = 5, Sha256 = TestStamps.ShaA };
                index.Slots["9999"] = new SlotRecord { Name = null, CategoryId = "gone", Length = 7, Sha256 = TestStamps.ShaB };
                index.Slots["002"] = new SlotRecord { Name = "a<b>c", Length = 9, Sha256 = TestStamps.ShaB };
                var before = JsonFile.Write(index);
                index.Normalize();
                Check.True(before.SequenceEqual(JsonFile.Write(index)));
            });

            test("store_load_applies_the_ui_limits_to_the_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                File.WriteAllText(path, "{\"Version\":2,\"NewestFirst\":true,"
                    + "\"Categories\":[{\"Id\":\"a\",\"Name\":\" " + new string('長', 60) + "\",\"Order\":0},{\"Id\":\"a\",\"Name\":\"dup\",\"Order\":1}],"
                    + "\"Slots\":{\"auto\":{\"Name\":\"x\",\"Length\":1,\"Sha256\":\"" + TestStamps.ShaA + "\"},"
                    + "\"001\":{\"Name\":\"" + new string('名', 100) + "\",\"Length\":1,\"Sha256\":\"" + TestStamps.ShaA + "\"}}}");
                var store = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder));
                var index = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.True(store.CanSave);
                Check.Equal(1, index.Categories.Count);
                Check.Equal(SaveCatalog.MaxCategoryNameLength, index.Categories[0].Name.Length);
                Check.SetEqual(new[] { "001" }, index.Slots.Keys);
                Check.Equal(SaveCatalog.MaxSlotNameLength, index.Slots["001"].Name.Length);
            }));

            test("open_does_not_rewrite_the_synced_file_just_because_loading_normalized_it", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var file = TestStamps.PutSave(folder, "001", 10);
                var sha = TestStamps.Sha256OfFile(file);
                var path = SaveSession.IndexPathFor(folder);
                var raw = "{\"Version\":2,\"NewestFirst\":true,\"Categories\":[{\"Id\":\"a\",\"Name\":\"  <b>Main</b>  \",\"Order\":0},"
                    + "{\"Id\":\"a\",\"Name\":\"dup\",\"Order\":1}],"
                    + "\"Slots\":{\"auto\":{\"Name\":\"x\",\"Length\":1,\"Sha256\":\"" + TestStamps.ShaA + "\"},"
                    + "\"001\":{\"Name\":\"" + new string('名', 100) + "\",\"CategoryId\":\"a\",\"Length\":10,\"Sha256\":\"" + sha + "\"}}}";
                File.WriteAllText(path, raw);
                var bytes = File.ReadAllBytes(path);

                var session = SaveSession.Open(folder, warnings.Add);
                Check.Equal(0, warnings.Count);
                Check.True(session.CanPersist);
                Check.Equal(new string('名', SaveCatalog.MaxSlotNameLength), session.Catalog.NameOf("001"));
                Check.Equal(1, session.Catalog.UserCategories().Count);
                session.ListKeys(null);
                session.ListKeys("a");
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)));

                // The next write (here an edit) stores the normalized data, values exactly as the user sees them; what was dropped is gone.
                session.Catalog.CreateCategory("Side");
                Check.True(session.Persist());
                var onDisk = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder)).Load();
                Check.Equal(2, onDisk.Categories.Count);
                Check.Equal("<b>Main</b>", onDisk.Categories[0].Name);
                Check.SetEqual(new[] { "001" }, onDisk.Slots.Keys);
                Check.Equal(SaveCatalog.MaxSlotNameLength, onDisk.Slots["001"].Name.Length);
            }));

            test("open_still_writes_back_when_a_record_really_went_stale", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var file = TestStamps.PutSave(folder, "001", 10);
                var seed = new SaveIndex();
                seed.Slots["001"] = new SlotRecord { Name = new string('名', 100), Length = 10, Sha256 = TestStamps.Sha256OfFile(file) };
                seed.Slots["002"] = new SlotRecord { Name = "gone", Length = 5, Sha256 = TestStamps.ShaA };
                File.WriteAllBytes(SaveSession.IndexPathFor(folder), JsonFile.Write(seed));

                SaveSession.Open(folder, warnings.Add);
                var onDisk = new SaveIndexStore(SaveSession.IndexPathFor(folder)).Load();
                Check.SetEqual(new[] { "001" }, onDisk.Slots.Keys);
            }));

            test("migration_applies_the_ui_limits_to_the_v1_index", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var file = TestStamps.PutSave(folder, "001", 10);
                var info = new FileInfo(file);
                Directory.CreateDirectory(SaveSession.LocalFolderFor(folder));
                File.WriteAllText(SaveSession.LegacyIndexPathFor(folder), "{\"Version\":1,\"NewestFirst\":true,"
                    + "\"Categories\":[{\"Id\":\"a\",\"Name\":\"" + new string('長', 60) + "\",\"Order\":0},{\"Id\":null,\"Name\":\"x\",\"Order\":1}],"
                    + "\"Slots\":{\"001\":{\"Name\":\"" + new string('名', 100) + "\",\"CategoryId\":\"a\",\"Length\":10,\"WriteTicks\":"
                    + info.LastWriteTimeUtc.Ticks + "}}}");

                var result = MetadataMigration.TryMigrate(folder);
                Check.True(result.Migrated);
                var onDisk = new SaveIndexStore(SaveSession.IndexPathFor(folder)).Load();
                Check.Equal(1, onDisk.Categories.Count);
                Check.Equal(SaveCatalog.MaxCategoryNameLength, onDisk.Categories[0].Name.Length);
                Check.Equal(SaveCatalog.MaxSlotNameLength, onDisk.Slots["001"].Name.Length);
                // Byte-identical to what a fresh load would normalize to: nothing is rewritten at the next start.
                var bytes = File.ReadAllBytes(SaveSession.IndexPathFor(folder));
                SaveSession.Open(folder, warnings.Add);
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(SaveSession.IndexPathFor(folder))));
            }));
        }

        private static void SizeCapTests(Action<string, Action> test)
        {
            test("store_reads_a_file_of_exactly_the_size_limit", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                File.WriteAllBytes(path, PaddedJson("{\"Version\":2,\"Categories\":[{\"Id\":\"a\",\"Name\":\"Main\",\"Order\":0}]}", Cap));
                var store = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder));
                var index = store.Load();
                Check.Null(store.LastLoadProblem);
                Check.True(store.CanSave);
                Check.Equal(1, index.Categories.Count);
            }));

            test("store_leaves_an_oversized_synced_file_in_place_and_goes_read_only", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                var bytes = PaddedJson("{\"Version\":2,\"Categories\":[{\"Id\":\"a\",\"Name\":\"Main\",\"Order\":0}]}", Cap + 1);
                File.WriteAllBytes(path, bytes);
                var store = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder));
                var index = store.Load();
                Check.Equal(0, index.Categories.Count);
                Check.False(store.CanSave);
                Check.True(store.LastLoadProblem != null && store.LastLoadProblem.Contains(CapText));
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)));
                Check.False(Directory.Exists(SaveSession.LocalFolderFor(folder))
                    && Directory.GetFiles(SaveSession.LocalFolderFor(folder), "*corrupt*").Length > 0);
                Check.Throws<InvalidOperationException>(() => store.Save(new SaveIndex()));
            }));

            test("session_with_an_oversized_synced_file_warns_and_never_writes_it", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 10);
                var path = SaveSession.IndexPathFor(folder);
                var bytes = PaddedJson("{\"Version\":2}", Cap + 100);
                File.WriteAllBytes(path, bytes);

                var session = SaveSession.Open(folder, warnings.Add);
                Check.False(session.CanPersist);
                Check.True(warnings.Any(w => w.Contains(CapText)));
                session.Catalog.CreateCategory("Main");
                Check.False(session.Persist());
                session.ListKeys(null);
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)));
                Check.SetEqual(new[] { "LoMSaveManager.json", "Save_001.dat" }, TestStamps.RootFileNames(folder));
            }));

            test("the_largest_index_this_mod_can_write_stays_well_under_the_read_cap", () =>
            {
                // Every manual slot named to the limit and filed, every category at its name limit, with the character
                // the serializer spends the most bytes on: the mod can never write a file it would refuse to read.
                var worst = 0;
                foreach (var unit in new[] { "\u0001", "\"", "\\", "/", "中", "😀", "\u2028", "<", "\u007f" })
                    worst = Math.Max(worst, JsonFile.Write(LargestIndex(unit)).Length);
                if (worst > Cap / 4 * 3)
                    throw new Exception("the largest index is " + worst + " bytes, too close to the " + Cap + "-byte read cap");
            });

            test("the_largest_local_cache_stays_under_its_read_cap", () =>
            {
                var file = new LocalCacheFile { Version = LocalStampCache.CurrentVersion, Entries = new Dictionary<string, LocalCacheEntry>() };
                for (var n = 1; n <= SlotKey.MaxManualSlot; n++)
                    file.Entries[SlotKey.Format(n)] = new LocalCacheEntry { Length = long.MinValue, WriteTicks = long.MinValue, Sha256 = TestStamps.ShaA };
                var size = JsonFile.Write(file).Length;
                if (size > LocalStampCache.MaxFileBytes / 4 * 3)
                    throw new Exception("the largest cache is " + size + " bytes, too close to its " + LocalStampCache.MaxFileBytes + "-byte cap");
            });

            test("store_refuses_to_write_past_its_read_cap_and_keeps_the_previous_file", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                var store = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder), 2048);
                var small = new SaveIndex();
                small.Categories.Add(new CategoryRecord { Id = "a", Name = "Main" });
                store.Save(small);
                var before = File.ReadAllBytes(path);

                var big = new SaveIndex();
                for (var i = 0; i < 100; i++) big.Categories.Add(new CategoryRecord { Id = "c" + i, Name = "category " + i, Order = i });
                Check.Throws<IOException>(() => store.Save(big));
                Check.False(store.CanSave);
                Check.True(before.SequenceEqual(File.ReadAllBytes(path)));
                Check.Equal(0, Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories).Length);
                Check.Throws<InvalidOperationException>(() => store.Save(small)); // read-only for the rest of the session

                // The next session reads the kept file normally.
                var reread = new SaveIndexStore(path, SaveSession.LocalFolderFor(folder), 2048);
                Check.Equal("Main", reread.Load().Categories.Single().Name);
                Check.True(reread.CanSave);
            }));

            test("store_writes_a_file_of_exactly_its_read_cap", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var path = SaveSession.IndexPathFor(folder);
                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "a", Name = "Main" });
                var exact = JsonFile.Write(index).Length;
                var store = new SaveIndexStore(path, null, exact);
                store.Save(index);
                Check.True(store.CanSave);
                Check.Equal((long)exact, new FileInfo(path).Length);
            }));

            test("session_whose_next_write_would_pass_the_cap_keeps_the_file_and_stops_editing", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 10);
                var first = SaveSession.Open(folder, warnings.Add, 2048);
                first.ListKeys(null);
                first.Catalog.SetName("001", "kept");
                Check.True(first.Persist());
                var path = SaveSession.IndexPathFor(folder);
                var before = File.ReadAllBytes(path);

                var session = SaveSession.Open(folder, warnings.Add, 2048);
                Check.True(session.CanPersist);
                for (var i = 0; i < 100; i++) session.Catalog.CreateCategory("category " + i);
                Check.False(session.Persist());
                Check.False(session.CanPersist);
                Check.Equal(1, warnings.Count(w => w.Contains("2048") || w.Contains("cap")));
                Check.False(session.Persist()); // no second warning
                Check.Equal(1, warnings.Count(w => w.Contains("2048") || w.Contains("cap")));
                Check.True(before.SequenceEqual(File.ReadAllBytes(path)));
                Check.Equal("kept", SaveSession.Open(folder, warnings.Add, 2048).Catalog.NameOf("001"));
            }));

            test("an_edit_whose_write_is_refused_for_size_reports_the_read_only_error", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 10);
                var session = SaveSession.Open(folder, warnings.Add, 2048);
                var controller = new CategoryController(session.Catalog, () => session.Persist(), () => session.CanPersist);
                string refused = null;
                var refusedWhileEditable = false;
                for (var i = 0; i < 100 && refused == null; i++)
                {
                    var editable = controller.CanEdit;
                    try { controller.Create("category " + i); }
                    catch (ArgumentException ex)
                    {
                        refused = ex.Message;
                        refusedWhileEditable = editable;
                    }
                }
                // The very edit whose write hit the cap says so (a toast in the panel) instead of looking saved.
                Check.Equal(CategoryController.ReadOnlyErrorKey, refused);
                Check.True(refusedWhileEditable);
                Check.False(controller.CanEdit);
                Check.True(new FileInfo(SaveSession.IndexPathFor(folder)).Length <= 2048);
            }));

            test("oversized_local_cache_is_ignored_and_rebuilt", () => TestStamps.WithFolder((folder, warnings) =>
            {
                var file = TestStamps.PutSave(folder, "001", 10);
                var cachePath = SaveSession.LocalCachePathFor(folder);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                var info = new FileInfo(file);
                // A well-formed entry, but the file is over the cap: it must not even be parsed.
                var json = "{\"Version\":1,\"Entries\":{\"001\":{\"Length\":10,\"WriteTicks\":" + info.LastWriteTimeUtc.Ticks
                    + ",\"Sha256\":\"" + TestStamps.ShaA + "\"}}}";
                File.WriteAllBytes(cachePath, PaddedJson(json, LocalStampCache.MaxFileBytes + 1));

                var cache = new LocalStampCache(cachePath);
                Check.True(cache.LoadProblem != null && cache.LoadProblem.Contains("MB"));
                var stamp = cache.Resolve("001", info);
                Check.Equal(TestStamps.Sha256OfFile(file), stamp.Sha256);
                Check.Equal(1, cache.Computed);
                cache.Save();
                Check.True(new FileInfo(cachePath).Length < 1024);
                Check.Null(new LocalStampCache(cachePath).LoadProblem);
            }));

            test("oversized_v1_index_postpones_migration_without_moving_it", () => TestStamps.WithFolder((folder, warnings) =>
            {
                TestStamps.PutSave(folder, "001", 10);
                Directory.CreateDirectory(SaveSession.LocalFolderFor(folder));
                var legacy = SaveSession.LegacyIndexPathFor(folder);
                var bytes = PaddedJson("{\"Version\":1}", Cap + 1);
                File.WriteAllBytes(legacy, bytes);

                var result = MetadataMigration.TryMigrate(folder);
                Check.False(result.Migrated);
                Check.True(result.Problem != null && result.Problem.Contains(CapText));
                Check.True(bytes.SequenceEqual(File.ReadAllBytes(legacy)));
                Check.Equal(0, Directory.GetFiles(SaveSession.LocalFolderFor(folder), "*corrupt*").Length);

                var session = SaveSession.Open(folder, warnings.Add);
                Check.False(session.CanPersist);
                session.Catalog.CreateCategory("Main");
                Check.False(session.Persist());
                Check.False(File.Exists(SaveSession.IndexPathFor(folder)));
                Check.True(File.Exists(legacy));
            }));

            test("oversized_v1_index_log_says_it_is_not_retried_and_what_to_do", () => TestStamps.WithFolder((folder, warnings) =>
            {
                Directory.CreateDirectory(SaveSession.LocalFolderFor(folder));
                File.WriteAllBytes(SaveSession.LegacyIndexPathFor(folder), PaddedJson("{\"Version\":1}", Cap + 1));

                var result = MetadataMigration.TryMigrate(folder);
                Check.True(result.Problem.Contains("not retried automatically"));
                Check.True(result.Problem.Contains("move " + SaveSession.FolderName + "/" + SaveSession.LegacyIndexFileName));

                var session = SaveSession.Open(folder, warnings.Add);
                session.Persist();
                Check.True(warnings.Any(w => w.Contains("not retried automatically")));
                Check.False(warnings.Any(w => w.Contains("retried at the next start")));
            }));

            test("oversized_v1_index_beside_the_synced_file_is_reported_not_read", () => TestStamps.WithFolder((folder, warnings) =>
            {
                Directory.CreateDirectory(SaveSession.LocalFolderFor(folder));
                File.WriteAllBytes(SaveSession.LegacyIndexPathFor(folder), PaddedJson("{\"Version\":1}", Cap + 1));
                var seed = new SaveIndex();
                seed.Categories.Add(new CategoryRecord { Id = "a", Name = "Main" });
                new SaveIndexStore(SaveSession.IndexPathFor(folder)).Save(seed);

                var result = MetadataMigration.TryMigrate(folder);
                Check.False(result.Migrated);
                Check.True(result.Note != null); // cannot rule out that it holds names: say it was left alone
            }));
        }

        private static void DisplayTextTests(Action<string, Action> test)
        {
            test("display_text_never_lets_a_user_string_open_a_rich_text_tag", () =>
            {
                Check.Equal("＜color=red>x＜/color>", DisplayText.Plain("<color=red>x</color>"));
                Check.Equal("＜size=300>大", DisplayText.Plain("<size=300>大"));
                Check.Equal("a ＜ b > c", DisplayText.Plain("a < b > c"));
            });

            test("display_text_keeps_ordinary_names_and_null", () =>
            {
                Check.Equal("第三年春・告白前", DisplayText.Plain("第三年春・告白前"));
                Check.Equal("", DisplayText.Plain(""));
                Check.Null(DisplayText.Plain(null));
            });
        }

        /// <summary>
        /// The biggest index the mod's own rules allow: every manual slot with a name at the limit (made of
        /// <paramref name="unit"/>, one or two UTF-16 units), filed in a category, with the widest numbers; and every
        /// category with a name at the limit. Ids are the mod's own 32-character GUIDs.
        /// </summary>
        private static SaveIndex LargestIndex(string unit)
        {
            var index = new SaveIndex { NewestFirst = false };
            var categoryName = Repeat(unit, SaveCatalog.MaxCategoryNameLength);
            for (var i = 0; i < SaveCatalog.MaxCategories; i++)
                index.Categories.Add(new CategoryRecord { Id = Guid.NewGuid().ToString("N"), Name = categoryName, Order = int.MinValue });
            var slotName = Repeat(unit, SaveCatalog.MaxSlotNameLength);
            var categoryId = index.Categories[0].Id;
            for (var n = 1; n <= SlotKey.MaxManualSlot; n++)
                index.Slots[SlotKey.Format(n)] = new SlotRecord { Name = slotName, CategoryId = categoryId, Length = long.MinValue, Sha256 = TestStamps.ShaA };
            return index;
        }

        private static string Repeat(string unit, int maxLength)
        {
            var text = new StringBuilder();
            while (text.Length + unit.Length <= maxLength) text.Append(unit);
            return text.ToString();
        }

        /// <summary>A JSON object padded with trailing spaces (still valid JSON) to exactly <paramref name="size"/> bytes.</summary>
        private static byte[] PaddedJson(string json, int size)
        {
            var head = Encoding.UTF8.GetBytes(json);
            var bytes = new byte[size];
            for (var i = 0; i < size; i++) bytes[i] = (byte)' ';
            Array.Copy(head, bytes, head.Length);
            return bytes;
        }
    }
}
