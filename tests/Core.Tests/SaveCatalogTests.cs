using System;
using System.Collections.Generic;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class SaveCatalogTests
    {
        private static readonly SaveStamp DefaultStamp = TestStamps.S(100, 1);

        public static void Run(Action<string, Action> test)
        {
            test("new_saves_are_uncategorized", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var keys = new[] { "001", "002" };
                Check.Equal(2, catalog.UncategorizedCount(keys));
                Check.Null(catalog.CategoryOf("001"));
                Check.Null(catalog.NameOf("001"));
                Check.Equal("001,002", Join(catalog.KeysIn(null, keys, false, k => 0L)));
                Check.Equal(0, catalog.UserCategories().Count);
            });

            test("naming_or_filing_a_save_whose_file_state_is_unknown_is_refused", () =>
            {
                // Without a stamp the record would carry no hash and the next Reconcile would silently drop the edit.
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var main = catalog.CreateCategory("Main");
                ThrowsArgument(SaveCatalog.ErrorKeys.SaveNotScanned, () => catalog.SetName("001", "name"));
                ThrowsArgument(SaveCatalog.ErrorKeys.SaveNotScanned, () => catalog.Assign("001", main));
                Check.Equal(0, index.Slots.Count);

                // Clearing never needs a record, so it is not refused.
                catalog.SetName("001", null);
                catalog.Assign("001", null);

                catalog.Reconcile(Files(new[] { "001" }));
                catalog.SetName("001", "name");
                catalog.Assign("001", main);
                Check.Equal("name", catalog.NameOf("001"));
                Check.False(catalog.Reconcile(Files(new[] { "001" })));
                Check.Equal("name", catalog.NameOf("001"));
            });

            test("delete_category_moves_saves_to_uncategorized_and_keeps_names", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var keys = new[] { "001", "002", "003" };
                catalog.Reconcile(Files(keys));
                var main = catalog.CreateCategory("主線");
                var side = catalog.CreateCategory("支線");
                catalog.Assign("001", main);
                catalog.SetName("001", "告白前");
                catalog.Assign("002", main);
                catalog.Assign("003", side);
                Check.Equal(0, catalog.UncategorizedCount(keys));

                catalog.DeleteCategory(main);

                Check.Equal(2, catalog.UncategorizedCount(keys));
                Check.Equal("001,002", Join(catalog.KeysIn(null, keys, false, k => 0L)));
                Check.Equal("告白前", catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
                Check.Null(index.Slots["001"].CategoryId);
                Check.Equal(side, catalog.CategoryOf("003"));
                var categories = catalog.UserCategories();
                Check.Equal(1, categories.Count);
                Check.Equal(side, categories[0].Id);
                Check.Equal(1, categories[0].Count);
                Check.Equal(0, index.Categories.Single().Order);
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNotFound, () => catalog.DeleteCategory(main));
            });

            test("rename_and_reorder_categories", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var a = catalog.CreateCategory("A");
                var b = catalog.CreateCategory("B");
                var c = catalog.CreateCategory("C");
                Check.Equal("A,B,C", CategoryNames(catalog));
                AssertContiguous(catalog, index);

                catalog.RenameCategory(b, "  B2 ");
                Check.Equal("A,B2,C", CategoryNames(catalog));

                catalog.MoveCategory(c, 0);
                Check.Equal("C,A,B2", CategoryNames(catalog));
                AssertContiguous(catalog, index);

                catalog.MoveCategory(c, 99);
                Check.Equal("A,B2,C", CategoryNames(catalog));
                AssertContiguous(catalog, index);

                catalog.MoveCategory(b, -5);
                Check.Equal("B2,A,C", CategoryNames(catalog));
                AssertContiguous(catalog, index);

                catalog.MoveCategory(a, 1);
                Check.Equal("B2,A,C", CategoryNames(catalog));
                AssertContiguous(catalog, index);
                Check.Equal(b, catalog.UserCategories()[0].Id);

                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNotFound, () => catalog.RenameCategory("nope", "X"));
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNotFound, () => catalog.MoveCategory("nope", 0));
            });

            test("reorder_renumbers_a_loaded_index_with_gaps", () =>
            {
                var index = new SaveIndex();
                index.Categories.Add(new CategoryRecord { Id = "x", Name = "X", Order = 7 });
                index.Categories.Add(new CategoryRecord { Id = "y", Name = "Y", Order = 2 });
                var catalog = new SaveCatalog(index);
                Check.Equal("Y,X", CategoryNames(catalog));

                catalog.MoveCategory("x", 0);

                Check.Equal("X,Y", CategoryNames(catalog));
                AssertContiguous(catalog, index);

                var z = catalog.CreateCategory("Z");
                Check.Equal("X,Y,Z", CategoryNames(catalog));
                Check.Equal(z, catalog.UserCategories()[2].Id);
                AssertContiguous(catalog, index);
            });

            test("category_name_rules", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameEmpty, () => catalog.CreateCategory(""));
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameEmpty, () => catalog.CreateCategory("   　 "));
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameEmpty, () => catalog.CreateCategory(null));
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameTooLong, () => catalog.CreateCategory(new string('字', 25)));
                Check.Equal(0, catalog.UserCategories().Count);

                var ok = catalog.CreateCategory(new string('字', 24));
                Check.Equal(24, catalog.UserCategories()[0].Name.Length);

                // Length is checked after trimming.
                var padded = catalog.CreateCategory("  " + new string('x', 24) + "  ");
                Check.Equal(new string('x', 24), catalog.UserCategories()[1].Name);

                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameEmpty, () => catalog.RenameCategory(ok, " "));
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNameTooLong, () => catalog.RenameCategory(ok, new string('y', 25)));
                Check.Equal(new string('字', 24), catalog.UserCategories()[0].Name);
                catalog.RenameCategory(padded, "ok");
                Check.Equal("ok", catalog.UserCategories()[1].Name);

                // The error key is the whole message (no "Parameter name:" suffix).
                try { catalog.CreateCategory(""); }
                catch (ArgumentException ex) { Check.Equal("CategoryName.Empty", ex.Message); }
            });

            test("category_count_is_capped_at_200", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                string first = null;
                for (var i = 0; i < 200; i++)
                {
                    var id = catalog.CreateCategory("c" + i);
                    if (first == null) first = id;
                }
                Check.Equal(200, catalog.UserCategories().Count);

                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryLimit, () => catalog.CreateCategory("c200"));
                Check.Equal(200, catalog.UserCategories().Count);

                catalog.DeleteCategory(first);
                catalog.CreateCategory("again");
                Check.Equal(200, catalog.UserCategories().Count);
            });

            test("slot_name_rules", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(Files("001"));
                catalog.SetName("001", new string('字', 40));
                Check.Equal(40, catalog.NameOf("001").Length);

                ThrowsArgument(SaveCatalog.ErrorKeys.SlotNameTooLong, () => catalog.SetName("001", new string('字', 41)));
                Check.Equal(40, catalog.NameOf("001").Length);

                catalog.SetName("001", "  第三年  ");
                Check.Equal("第三年", catalog.NameOf("001"));

                // A name of 40 plus surrounding whitespace is fine: the limit applies after trimming.
                catalog.SetName("001", " " + new string('a', 40) + " ");
                Check.Equal(new string('a', 40), catalog.NameOf("001"));

                catalog.SetName("001", "   ");
                Check.Null(catalog.NameOf("001"));
                catalog.SetName("001", "back");
                catalog.SetName("001", null);
                Check.Null(catalog.NameOf("001"));
            });

            test("name_and_assign_reject_bad_keys_and_categories", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(Files("001", "002"));
                var cat = catalog.CreateCategory("A");
                foreach (var bad in new[] { "auto", "000", "1", "10000", "", null })
                {
                    var key = bad;
                    Check.Throws<ArgumentException>(() => catalog.SetName(key, "x"));
                    Check.Throws<ArgumentException>(() => catalog.Assign(key, cat));
                    Check.Throws<ArgumentException>(() => catalog.RecordOwnWrite(key, DefaultStamp, null));
                    Check.Null(catalog.NameOf(key));
                    Check.Null(catalog.CategoryOf(key));
                }
                ThrowsArgument(SaveCatalog.ErrorKeys.CategoryNotFound, () => catalog.Assign("001", "missing"));
                Check.Null(catalog.CategoryOf("001"));

                // Assigning null moves a save back to uncategorized, and does not invent a record.
                catalog.Assign("002", null);
                catalog.Assign("001", cat);
                Check.Equal(cat, catalog.CategoryOf("001"));
                catalog.Assign("001", null);
                Check.Null(catalog.CategoryOf("001"));
            });

            test("stale_stamp_drops_metadata", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var cat = catalog.CreateCategory("主線");
                catalog.RecordOwnWrite("001", TestStamps.S(100, 5), cat);
                catalog.SetName("001", "舊存檔");
                Check.Equal("舊存檔", catalog.NameOf("001"));
                Check.Equal(cat, catalog.CategoryOf("001"));

                var changed = catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", TestStamps.S(200, 5) } });

                Check.True(changed);
                Check.Null(catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
                Check.False(index.Slots.ContainsKey("001"));
                // The save is still listed, now as uncategorized.
                Check.Equal(1, catalog.UncategorizedCount(new[] { "001" }));
            });

            test("write_time_change_alone_keeps_metadata", () =>
            {
                // Another machine downloads the same bytes with a different mtime: same length and SHA-256, so keep.
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.RecordOwnWrite("001", new SaveStamp(100, 5, TestStamps.ShaA), null);
                catalog.SetName("001", "x");

                Check.False(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", new SaveStamp(100, 6, TestStamps.ShaA) } }));
                Check.Equal("x", catalog.NameOf("001"));
            });

            test("sha_mismatch_drops_record", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var cat = catalog.CreateCategory("主線");
                catalog.RecordOwnWrite("001", new SaveStamp(100, 5, TestStamps.ShaA), cat);
                catalog.SetName("001", "x");

                // Same length, same write time, different content.
                Check.True(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", new SaveStamp(100, 5, TestStamps.ShaB) } }));
                Check.Null(catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
                Check.False(index.Slots.ContainsKey("001"));
            });

            test("record_without_a_sha_never_matches", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                index.Slots["001"] = new SlotRecord { Name = "hand-edited", Length = 100, Sha256 = null };
                Check.True(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", new SaveStamp(100, 5, TestStamps.ShaA) } }));
                Check.Null(catalog.NameOf("001"));
            });

            test("missing_file_drops_metadata", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                catalog.RecordOwnWrite("001", TestStamps.S(100, 5), null);
                catalog.RecordOwnWrite("002", TestStamps.S(100, 6), null);
                catalog.SetName("001", "gone");
                catalog.SetName("002", "kept");

                var changed = catalog.Reconcile(new Dictionary<string, SaveStamp> { { "002", TestStamps.S(100, 6) } });

                Check.True(changed);
                Check.Null(catalog.NameOf("001"));
                Check.Equal("kept", catalog.NameOf("002"));
                Check.SetEqual(new[] { "002" }, index.Slots.Keys);
            });

            test("reconcile_reports_no_change_when_stamps_match", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.RecordOwnWrite("001", TestStamps.S(100, 5), null);
                catalog.SetName("001", "x");
                var files = new Dictionary<string, SaveStamp>
                {
                    { "001", TestStamps.S(100, 5) },
                    { "002", TestStamps.S(1, 1) },
                    { "auto", TestStamps.S(1, 1) },
                };

                Check.False(catalog.Reconcile(files));
                Check.Equal("x", catalog.NameOf("001"));

                // With every file gone the record is dropped once; after that there is nothing left to change.
                Check.True(catalog.Reconcile(new Dictionary<string, SaveStamp>()));
                Check.False(catalog.Reconcile(new Dictionary<string, SaveStamp>()));
            });

            test("naming_an_existing_unrecorded_save_survives_the_next_reconcile", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var cat = catalog.CreateCategory("A");
                var stamp = TestStamps.S(4096, 777);
                var files = new Dictionary<string, SaveStamp> { { "001", stamp }, { "002", stamp } };
                catalog.Reconcile(files);

                catalog.SetName("001", "pre-existing");
                catalog.Assign("002", cat);

                Check.False(catalog.Reconcile(files));
                Check.Equal("pre-existing", catalog.NameOf("001"));
                Check.Equal(cat, catalog.CategoryOf("002"));
                Check.Equal(4096L, index.Slots["001"].Length);
                Check.Equal(stamp.Sha256, index.Slots["001"].Sha256);

                // Replace the file outside the game: the name must not follow it.
                files["001"] = TestStamps.S(4096, 778);
                Check.True(catalog.Reconcile(files));
                Check.Null(catalog.NameOf("001"));
                Check.Equal(cat, catalog.CategoryOf("002"));
            });

            test("own_overwrite_refreshes_stamp_and_keeps_metadata", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var cat = catalog.CreateCategory("主線");
                var oldStamp = TestStamps.S(100, 10);
                var newStamp = TestStamps.S(150, 20);
                catalog.RecordOwnWrite("001", oldStamp, null);
                catalog.SetName("001", "舊名稱");
                catalog.Assign("001", cat);

                // The mod overwrites the slot; categoryIdIfNew is ignored because the key already has a record.
                catalog.RecordOwnWrite("001", newStamp, null);

                Check.False(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", newStamp } }));
                Check.Equal("舊名稱", catalog.NameOf("001"));
                Check.Equal(cat, catalog.CategoryOf("001"));
                Check.Equal(150L, index.Slots["001"].Length);
                Check.Equal(newStamp.Sha256, index.Slots["001"].Sha256);

                // The previous stamp no longer describes the file.
                Check.True(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "001", oldStamp } }));
                Check.Null(catalog.NameOf("001"));
            });

            test("record_own_write_assigns_category_for_new_key", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var a = catalog.CreateCategory("A");
                var b = catalog.CreateCategory("B");
                var stamp = TestStamps.S(10, 3);

                catalog.RecordOwnWrite("003", stamp, a);
                Check.Equal(a, catalog.CategoryOf("003"));
                Check.Null(catalog.NameOf("003"));
                Check.False(catalog.Reconcile(new Dictionary<string, SaveStamp> { { "003", stamp } }));
                Check.Equal(a, catalog.CategoryOf("003"));

                // An existing record keeps its category; categoryIdIfNew only applies to new keys.
                catalog.RecordOwnWrite("003", TestStamps.S(11, 4), b);
                Check.Equal(a, catalog.CategoryOf("003"));

                // A new key with no category, or with a category that has since been deleted, is uncategorized.
                catalog.RecordOwnWrite("004", stamp, null);
                Check.Null(catalog.CategoryOf("004"));
                catalog.RecordOwnWrite("005", stamp, "deleted-meanwhile");
                Check.Null(catalog.CategoryOf("005"));
                Check.Null(index.Slots["005"].CategoryId);
            });

            test("keys_in_sorts_by_ticks_both_directions", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var ticks = new Dictionary<string, long>
                {
                    { "001", 50 }, { "002", 100 }, { "999", 100 }, { "1000", 100 }, { "003", 200 },
                };
                var keys = new[] { "1000", "003", "999", "001", "002" };

                var oldestFirst = catalog.KeysIn(null, keys, false, k => ticks[k]);
                var newestFirst = catalog.KeysIn(null, keys, true, k => ticks[k]);

                // Equal ticks fall back to SlotKey.NumericOrder (999 before 1000), and newest-first is the exact reverse.
                Check.Equal("001,002,999,1000,003", Join(oldestFirst));
                Check.Equal("003,1000,999,002,001", Join(newestFirst));
            });

            test("keys_in_filters_by_category", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var a = catalog.CreateCategory("A");
                var b = catalog.CreateCategory("B");
                var keys = new[] { "001", "002", "003", "004" };
                catalog.Reconcile(Files(keys));
                catalog.Assign("001", a);
                catalog.Assign("003", a);
                catalog.Assign("004", b);

                Check.Equal("001,003", Join(catalog.KeysIn(a, keys, false, k => 0L)));
                Check.Equal("004", Join(catalog.KeysIn(b, keys, false, k => 0L)));
                Check.Equal("002", Join(catalog.KeysIn(null, keys, false, k => 0L)));
                Check.Equal(0, catalog.KeysIn("missing", keys, false, k => 0L).Count);
                Check.Equal(1, catalog.UncategorizedCount(keys));
            });

            test("keys_in_only_lists_existing_manual_keys", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var a = catalog.CreateCategory("A");
                catalog.RecordOwnWrite("001", DefaultStamp, a);
                catalog.RecordOwnWrite("002", DefaultStamp, a);

                // 002 has a record but its file is gone from the existing list; auto/universe are not manual slots.
                var existing = new[] { "001", "auto", "universe", "" };
                Check.Equal("001", Join(catalog.KeysIn(a, existing, false, k => 0L)));
                Check.Equal(0, catalog.UncategorizedCount(existing));
                Check.Equal(0, catalog.KeysIn(null, existing, false, k => 0L).Count);
            });

            test("uncategorized_includes_unknown_existing_keys", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var a = catalog.CreateCategory("A");
                catalog.RecordOwnWrite("002", DefaultStamp, a);
                var keys = new[] { "001", "002", "003" };

                // 001 and 003 were never recorded.
                Check.Equal("001,003", Join(catalog.KeysIn(null, keys, false, k => 0L)));
                Check.Equal(2, catalog.UncategorizedCount(keys));

                // A record that points at a category missing from the index also counts as uncategorized.
                index.Slots["003"] = new SlotRecord { CategoryId = "ghost", Length = 1, Sha256 = TestStamps.ShaA };
                Check.Null(catalog.CategoryOf("003"));
                Check.Equal("001,003", Join(catalog.KeysIn(null, keys, false, k => 0L)));
                Check.Equal(2, catalog.UncategorizedCount(keys));
            });

            test("user_categories_count_assigned_saves", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var a = catalog.CreateCategory("A");
                var b = catalog.CreateCategory("B");
                catalog.RecordOwnWrite("001", DefaultStamp, a);
                catalog.RecordOwnWrite("002", DefaultStamp, a);
                catalog.RecordOwnWrite("003", DefaultStamp, null);

                var categories = catalog.UserCategories();
                Check.Equal(a, categories[0].Id);
                Check.Equal("A", categories[0].Name);
                Check.Equal(2, categories[0].Count);
                Check.Equal(b, categories[1].Id);
                Check.Equal(0, categories[1].Count);
            });

            test("forget_removes_the_record", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var a = catalog.CreateCategory("A");
                catalog.RecordOwnWrite("001", DefaultStamp, a);
                catalog.SetName("001", "bye");

                catalog.Forget("001");

                Check.Null(catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
                Check.Equal(0, index.Slots.Count);
                Check.Equal(0, catalog.UserCategories()[0].Count);

                // Forgetting something unknown (or a non-manual key such as an auto slot) is harmless.
                catalog.Forget("001");
                catalog.Forget("auto");
                catalog.Forget(null);

                // A later save under the same key starts clean.
                catalog.RecordOwnWrite("001", DefaultStamp, null);
                Check.Null(catalog.NameOf("001"));
            });

            test("catalog_mutates_the_index_it_was_given", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var id = catalog.CreateCategory("A");
                catalog.RecordOwnWrite("001", DefaultStamp, id);
                catalog.SetName("001", "n");

                Check.Equal(1, index.Categories.Count);
                Check.Equal(id, index.Categories[0].Id);
                Check.Equal("A", index.Categories[0].Name);
                Check.Equal("n", index.Slots["001"].Name);
                Check.Equal(id, index.Slots["001"].CategoryId);
                Check.Equal(100L, index.Slots["001"].Length);
                Check.Equal(DefaultStamp.Sha256, index.Slots["001"].Sha256);
            });

            // ---- CategoryController: what the sidebar, the name dialog and the move dialog call ----

            test("sidebar_flow_create_assign_rename_delete", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                catalog.Reconcile(Files("001", "002"));
                var persisted = 0;
                var changed = 0;
                var controller = new CategoryController(catalog, () => persisted++);
                controller.Changed += () => changed++;

                var id = controller.Create("某某線");
                Check.Equal(1, persisted);
                Check.Equal(1, changed);
                Check.Equal("某某線", catalog.UserCategories().Single().Name);

                controller.Name("001", "告白前");
                controller.Move("001", id);
                Check.Equal(id, catalog.CategoryOf("001"));
                Check.Equal("001", Join(catalog.KeysIn(id, new[] { "001", "002" }, true, k => 0L)));

                controller.Rename(id, "某某線・二周目");
                Check.Equal("某某線・二周目", catalog.UserCategories().Single().Name);

                controller.Select(id);
                Check.Equal(id, controller.Selected);
                Check.Equal(id, controller.PendingCategoryId);

                controller.Delete(id);
                Check.Null(catalog.CategoryOf("001"));
                Check.Equal("告白前", catalog.NameOf("001"));
                Check.Equal(0, catalog.UserCategories().Count);
                // The deleted category was selected: the sidebar falls back to 未分類.
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
                Check.Null(controller.PendingCategoryId);
                Check.Equal(5, persisted);
                Check.Equal(6, changed); // create, name, move, rename, select, delete
            });

            test("fixed_categories_cannot_be_renamed_or_deleted", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(Files("001"));
                var persisted = 0;
                var controller = new CategoryController(catalog, () => persisted++);
                foreach (var fixedId in new[] { CategoryController.Recent, CategoryController.Auto, CategoryController.Uncategorized })
                {
                    Check.True(CategoryController.IsFixed(fixedId));
                    Check.Throws<InvalidOperationException>(() => controller.Rename(fixedId, "x"));
                    Check.Throws<InvalidOperationException>(() => controller.Delete(fixedId));
                    Check.Throws<InvalidOperationException>(() => controller.MoveUp(fixedId));
                    Check.Throws<InvalidOperationException>(() => controller.MoveDown(fixedId));
                }

                // Saves can be moved to 未分類 but never into 最近存檔 / 自動存檔.
                Check.Throws<InvalidOperationException>(() => controller.Move("001", CategoryController.Recent));
                Check.Throws<InvalidOperationException>(() => controller.Move("001", CategoryController.Auto));
                var id = controller.Create("A");
                controller.Move("001", id);
                controller.Move("001", CategoryController.Uncategorized);
                Check.Null(catalog.CategoryOf("001"));
                Check.False(CategoryController.IsFixed(id));
                Check.Equal(3, persisted);
            });
        }

        private static Dictionary<string, SaveStamp> Files(params string[] keys)
        {
            var files = new Dictionary<string, SaveStamp>();
            foreach (var key in keys) files[key] = DefaultStamp;
            return files;
        }

        private static string Join(IEnumerable<string> items)
        {
            return string.Join(",", items);
        }

        private static string CategoryNames(SaveCatalog catalog)
        {
            return string.Join(",", catalog.UserCategories().Select(c => c.Name));
        }

        /// <summary>Order values are exactly 0..n-1 and agree with the order UserCategories() reports.</summary>
        private static void AssertContiguous(SaveCatalog catalog, SaveIndex index)
        {
            var orderById = index.Categories.ToDictionary(c => c.Id, c => c.Order);
            var listed = catalog.UserCategories();
            Check.Equal(index.Categories.Count, listed.Count);
            for (var i = 0; i < listed.Count; i++) Check.Equal(i, orderById[listed[i].Id]);
        }

        private static void ThrowsArgument(string expectedKey, Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException ex)
            {
                Check.Equal(expectedKey, ex.Message);
                return;
            }
            throw new Exception("expected ArgumentException(" + expectedKey + ")");
        }
    }
}
