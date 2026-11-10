using System;
using System.Collections.Generic;

namespace LoMSaveManager.Tests
{
    /// <summary>
    /// The BeginOwnWrite (before the game writes a slot) / CompleteOwnWrite (after) pair: the order in which a
    /// save made through the game is recorded without losing the slot's name, or wrongly keeping a stale one.
    /// </summary>
    internal static class OwnWriteTests
    {
        private static readonly SaveStamp Old = TestStamps.S(100, 10);
        private static readonly SaveStamp New = TestStamps.S(150, 20);
        private static readonly SaveStamp Other = TestStamps.S(7, 3);

        public static void Run(Action<string, Action> test)
        {
            test("own_write_keeps_name_and_category_and_refreshes_the_stamp", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var main = catalog.CreateCategory("主線");
                var pending = catalog.CreateCategory("支線");
                var before = Files(Pair("001", Old), Pair("002", Other));
                catalog.Reconcile(before);
                catalog.SetName("001", "告白前");
                catalog.Assign("001", main);
                catalog.SetName("002", "別的存檔");

                var ticket = catalog.BeginOwnWrite("001", before);
                var after = Files(Pair("001", New), Pair("002", Other));
                catalog.CompleteOwnWrite(ticket, after, pending);

                Check.Equal("告白前", catalog.NameOf("001"));
                Check.Equal(main, catalog.CategoryOf("001")); // the pending category only applies to new saves
                Check.Equal(150L, index.Slots["001"].Length);
                Check.Equal(New.Sha256, index.Slots["001"].Sha256);
                Check.Equal("別的存檔", catalog.NameOf("002"));
                Check.False(catalog.Reconcile(after));
            });

            test("own_write_keeps_an_uncategorized_save_uncategorized", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var pending = catalog.CreateCategory("支線");
                var before = Files(Pair("001", Old));
                catalog.Reconcile(before);
                catalog.SetName("001", "named, no category");

                var ticket = catalog.BeginOwnWrite("001", before);
                catalog.CompleteOwnWrite(ticket, Files(Pair("001", New)), pending);

                Check.Equal("named, no category", catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
            });

            test("own_write_to_a_new_slot_goes_to_the_pending_category", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var pending = catalog.CreateCategory("支線");
                var before = Files(Pair("001", Old));
                catalog.Reconcile(before);

                var ticket = catalog.BeginOwnWrite("002", before);
                var after = Files(Pair("001", Old), Pair("002", New));
                catalog.CompleteOwnWrite(ticket, after, pending);

                Check.Equal(pending, catalog.CategoryOf("002"));
                Check.Null(catalog.NameOf("002"));
                Check.Equal(150L, index.Slots["002"].Length);
                Check.False(catalog.Reconcile(after));
            });

            test("own_write_without_a_pending_category_is_uncategorized", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var ticket = catalog.BeginOwnWrite("003", Files());
                catalog.CompleteOwnWrite(ticket, Files(Pair("003", New)), null);
                Check.Null(catalog.CategoryOf("003"));
                Check.Equal(1, catalog.UncategorizedCount(new[] { "003" }));
            });

            test("slot_deleted_outside_then_reused_in_the_mod_does_not_keep_the_old_name", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var main = catalog.CreateCategory("主線");
                var pending = catalog.CreateCategory("支線");
                catalog.Reconcile(Files(Pair("001", Old)));
                catalog.SetName("001", "舊的名字");
                catalog.Assign("001", main);

                // Save_001.dat was deleted outside the game, so the slot is absent from the scan taken before the write.
                var ticket = catalog.BeginOwnWrite("001", Files());
                catalog.CompleteOwnWrite(ticket, Files(Pair("001", New)), pending);

                Check.Null(catalog.NameOf("001"));
                Check.Equal(pending, catalog.CategoryOf("001"));
                Check.Equal(150L, index.Slots["001"].Length);
            });

            test("slot_replaced_outside_then_overwritten_in_the_mod_does_not_keep_the_old_name", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var main = catalog.CreateCategory("主線");
                catalog.Reconcile(Files(Pair("001", Old)));
                catalog.SetName("001", "舊的名字");
                catalog.Assign("001", main);

                // Another file now sits in Save_001.dat (different stamp than the record).
                var ticket = catalog.BeginOwnWrite("001", Files(Pair("001", Other)));
                catalog.CompleteOwnWrite(ticket, Files(Pair("001", New)), null);

                Check.Null(catalog.NameOf("001"));
                Check.Null(catalog.CategoryOf("001"));
            });

            test("own_write_drops_records_of_other_saves_changed_outside", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                catalog.Reconcile(Files(Pair("001", Old), Pair("002", Old), Pair("003", Old)));
                catalog.SetName("001", "one");
                catalog.SetName("002", "two");
                catalog.SetName("003", "three");

                // 002 is gone before the write; 003 is replaced while the game is writing.
                var ticket = catalog.BeginOwnWrite("001", Files(Pair("001", Old), Pair("003", Old)));
                catalog.CompleteOwnWrite(ticket, Files(Pair("001", New), Pair("003", Other)), null);

                Check.Equal("one", catalog.NameOf("001"));
                Check.Null(catalog.NameOf("002"));
                Check.Null(catalog.NameOf("003"));
                Check.SetEqual(new[] { "001" }, index.Slots.Keys);
            });

            test("a_save_that_did_not_change_the_file_keeps_the_record_and_creates_none", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var pending = catalog.CreateCategory("支線");
                var files = Files(Pair("001", Old), Pair("002", Old));
                catalog.Reconcile(files);
                catalog.SetName("001", "kept");

                // The game swallows write errors: the old file is still there, byte for byte.
                catalog.CompleteOwnWrite(catalog.BeginOwnWrite("001", files), files, pending);
                catalog.CompleteOwnWrite(catalog.BeginOwnWrite("002", files), files, pending);

                Check.Equal("kept", catalog.NameOf("001"));
                Check.Equal(100L, index.Slots["001"].Length);
                Check.Equal(Old.Sha256, index.Slots["001"].Sha256);
                Check.False(index.Slots.ContainsKey("002"));
                Check.False(catalog.Reconcile(files));
            });

            test("a_save_that_left_no_file_leaves_no_record", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                catalog.Reconcile(Files(Pair("001", Old)));
                catalog.SetName("001", "gone");

                catalog.CompleteOwnWrite(catalog.BeginOwnWrite("001", Files(Pair("001", Old))), Files(), null);
                catalog.CompleteOwnWrite(catalog.BeginOwnWrite("002", Files()), Files(), null);

                Check.Equal(0, index.Slots.Count);
            });

            test("own_write_rejects_keys_that_are_not_manual_slots_and_null_arguments", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                Check.Throws<ArgumentException>(() => catalog.BeginOwnWrite("auto", Files()));
                Check.Throws<ArgumentException>(() => catalog.BeginOwnWrite(null, Files()));
                Check.Throws<ArgumentNullException>(() => catalog.BeginOwnWrite("001", null));
                var ticket = catalog.BeginOwnWrite("001", Files());
                Check.Throws<ArgumentNullException>(() => catalog.CompleteOwnWrite(null, Files(), null));
                Check.Throws<ArgumentNullException>(() => catalog.CompleteOwnWrite(ticket, null, null));
            });
        }

        private static KeyValuePair<string, SaveStamp> Pair(string key, SaveStamp stamp)
        {
            return new KeyValuePair<string, SaveStamp>(key, stamp);
        }

        private static Dictionary<string, SaveStamp> Files(params KeyValuePair<string, SaveStamp>[] entries)
        {
            var files = new Dictionary<string, SaveStamp>();
            foreach (var entry in entries) files[entry.Key] = entry.Value;
            return files;
        }
    }
}
