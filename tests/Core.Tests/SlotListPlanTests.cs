using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class SlotListPlanTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("load_list_contains_only_existing_saves_plus_one_new_game_slot", () =>
            {
                var existing = new HashSet<string> { "001", "003", "021" };

                // The new-game slot comes first, then the category's saves in the order given. A key without a .dat
                // (stale category list, file deleted in between) must never appear: clicking it would start a new game.
                var list = SlotListPlan.ForLoad(new[] { "021", "002", "001", "003" }, existing, "002");
                Check.Equal("002,021,001,003", string.Join(",", list));

                // No other empty key, ever: here 004 is free but is not the new-game key.
                list = SlotListPlan.ForLoad(new[] { "004", "003" }, existing, "002");
                Check.Equal("002,003", string.Join(",", list));

                // 001..9999 all taken: no empty row at all.
                list = SlotListPlan.ForLoad(new[] { "003", "001" }, existing, null);
                Check.Equal("003,001", string.Join(",", list));

                // An empty category still offers the new-game slot.
                Check.Equal("002", string.Join(",", SlotListPlan.ForLoad(new string[0], existing, "002")));

                // A new-game key that turns out to exist is listed once, as the save it is (not as an empty row).
                list = SlotListPlan.ForLoad(new[] { "001", "003" }, existing, "003");
                Check.Equal("001,003", string.Join(",", list));

                Check.Throws<ArgumentNullException>(() => SlotListPlan.ForLoad(null, existing, "002"));
                Check.Throws<ArgumentNullException>(() => SlotListPlan.ForLoad(new string[0], null, "002"));
            });

            test("save_list_puts_new_slot_first_and_omits_when_full", () =>
            {
                Check.Equal("021,005,001", string.Join(",", SlotListPlan.ForSave(new[] { "005", "001" }, "021")));
                Check.Equal("005,001", string.Join(",", SlotListPlan.ForSave(new[] { "005", "001" }, null)));
                Check.Equal("021", string.Join(",", SlotListPlan.ForSave(new string[0], "021")));
                Check.Equal(0, SlotListPlan.ForSave(new string[0], null).Count);

                // Duplicates and non-manual keys never become rows.
                Check.Equal("021,005", string.Join(",", SlotListPlan.ForSave(new[] { "005", "021", "005", "auto" }, "021")));
                Check.Throws<ArgumentNullException>(() => SlotListPlan.ForSave(null, "021"));
                Check.Throws<ArgumentException>(() => SlotListPlan.ForSave(new string[0], "auto"));
            });

            test("save_panel_shows_the_full_hint_only_when_no_slot_is_free", () =>
            {
                // Design frame 11: the save panel's first row becomes a disabled 「存檔數已達上限 9999」 hint.
                Check.True(SlotListPlan.ShowsFullHint(true, true, null));
                Check.False(SlotListPlan.ShowsFullHint(true, true, "022"));
                // The load panel never offers a new save; recent/auto lists no slots at all.
                Check.False(SlotListPlan.ShowsFullHint(false, true, null));
                Check.False(SlotListPlan.ShowsFullHint(true, false, null));

                Check.Equal("存檔數已達上限 9999", SlotListPlan.FullHintTitle(Strings.ChineseTraditional));
                Check.Equal("存档数已达上限 9999", SlotListPlan.FullHintTitle(Strings.ChineseSimplified));
            });

            test("navigation_plan_handles_0_1_2_slots", () =>
            {
                var none = SlotListPlan.Navigation(0);
                Check.Equal(0, none.Count);

                var one = SlotListPlan.Navigation(1);
                Check.Equal(1, one.Count);
                Check.Equal(-1, one.Up(0));
                Check.Equal(-1, one.Down(0));

                var two = SlotListPlan.Navigation(2);
                Check.Equal(-1, two.Up(0));
                Check.Equal(1, two.Down(0));
                Check.Equal(0, two.Up(1));
                Check.Equal(-1, two.Down(1));

                var many = SlotListPlan.Navigation(30);
                Check.Equal(13, many.Up(14));
                Check.Equal(15, many.Down(14));
                Check.Equal(-1, many.Down(29));

                Check.Throws<ArgumentOutOfRangeException>(() => SlotListPlan.Navigation(-1));
                Check.Throws<ArgumentOutOfRangeException>(() => two.Up(2));
            });

            test("one_failing_slot_does_not_abort_the_list", () =>
            {
                var seen = new List<string>();
                var failed = SlotListPlan.SetupAll(new[] { "001", "002", "003", "004" }, key =>
                {
                    seen.Add(key);
                    if (key == "002" || key == "004") throw new InvalidOperationException("bad save " + key);
                });

                Check.Equal("001,002,003,004", string.Join(",", seen));
                Check.Equal("002,004", string.Join(",", failed));
                Check.Equal(0, SlotListPlan.SetupAll(new string[0], key => { throw new Exception(); }).Count);
                Check.Throws<ArgumentNullException>(() => SlotListPlan.SetupAll(null, key => { }));
            });

            test("session_list_keys_reconcile_and_sort_newest_first", () => WithFolder(folder =>
            {
                Touch(folder, "Save_001.dat", 2020);
                Touch(folder, "Save_002.dat", 2024);
                Touch(folder, "Save_003.dat.tmp", 2023); // occupies 003 but is not a save
                Touch(folder, "Save_005.dat", 2022);
                Touch(folder, "Save_auto.dat", 2025);
                var session = SaveSession.Open(folder, w => { });

                var keys = session.ListKeys(null);
                Check.Equal("002,005,001", string.Join(",", keys.Keys));
                Check.Equal("004", keys.NewSlotKey);
                Check.Equal("004,002,005,001", string.Join(",", SlotListPlan.ForLoad(keys.Keys, keys.Existing, keys.NewSlotKey)));

                // A save that appeared outside the game since Open is listed; one that vanished is not, and its name is dropped.
                session.Catalog.SetName("001", "舊名字");
                session.Persist();
                File.Delete(Path.Combine(folder, "Save_001.dat"));
                Touch(folder, "Save_004.dat", 2026);
                keys = session.ListKeys(null);
                Check.Equal("004,002,005", string.Join(",", keys.Keys));
                Check.Equal("001", keys.NewSlotKey);
                Check.Null(session.Catalog.NameOf("001"));
                Check.False(File.ReadAllText(SaveSession.IndexPathFor(folder)).Contains("舊名字"));
            }));

            test("new_game_save_is_recorded_in_the_pending_category", () => WithFolder(folder =>
            {
                Touch(folder, "Save_001.dat", 2020);
                var session = SaveSession.Open(folder, w => { });
                var category = session.Catalog.CreateCategory("主線");

                // 002 had a record from a save that was deleted outside the game; the new game must not inherit its name.
                session.Catalog.SetName("001", "keep");
                Touch(folder, "Save_002.dat", 2019);
                session.Catalog.Reconcile(session.ScanManualSaves());
                session.Catalog.SetName("002", "stale");
                File.Delete(Path.Combine(folder, "Save_002.dat"));

                Touch(folder, "Save_002.dat", 2026); // what NewGameData wrote
                session.AfterNewGame("002", category);
                Check.Equal(category, session.Catalog.CategoryOf("002"));
                Check.Null(session.Catalog.NameOf("002"));
                Check.Equal("keep", session.Catalog.NameOf("001"));

                // Survives a restart (persisted, and the stamp matches the file).
                var reopened = SaveSession.Open(folder, w => { });
                Check.Equal(category, reopened.Catalog.CategoryOf("002"));

                // Null pending category = uncategorized; non-manual or missing slots record nothing.
                Touch(folder, "Save_003.dat", 2026);
                reopened.AfterNewGame("003", null);
                Check.Null(reopened.Catalog.CategoryOf("003"));
                reopened.AfterNewGame("act1", category);
                reopened.AfterNewGame("004", category);
                Check.Null(reopened.Catalog.CategoryOf("004"));
                Check.Equal("002", string.Join(",", reopened.ListKeys(category).Keys));
            }));
        }

        private static void Touch(string folder, string name, int year)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, name);
            File.SetLastWriteTimeUtc(path, new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        private static void WithFolder(Action<string> body)
        {
            var folder = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-list-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try { body(folder); }
            finally { Directory.Delete(folder, true); }
        }
    }
}
