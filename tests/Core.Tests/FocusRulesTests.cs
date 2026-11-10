using System;
using System.Collections.Generic;

namespace LoMSaveManager.Tests
{
    internal static class FocusRulesTests
    {
        public static void Run(Action<string, Action> test)
        {
            // ---- category switching: focus-follows after a dwell, click/submit at once ----------------------------

            test("focusing_a_category_switches_only_after_the_dwell_with_the_same_category_focused", () =>
            {
                var dwell = new CategoryFocusDwell(0.35);
                dwell.Focused("A", 10.0);
                Check.Null(dwell.Poll("A", "@none", 10.0));
                Check.Null(dwell.Poll("A", "@none", 10.34));
                Check.Equal("A", dwell.Poll("A", "@none", 10.35));
                // Fired once: the next frame does not switch again.
                Check.Null(dwell.Poll("A", "A", 10.5));
                Check.False(dwell.Pending);
            });

            test("scrolling_through_categories_rebuilds_only_the_one_the_cursor_rests_on", () =>
            {
                var dwell = new CategoryFocusDwell(0.35);
                string selected = "@recent";
                var switches = new List<string>();
                // Holding the down key: a new category every 0.1 s, none of them is rebuilt.
                var ids = new[] { "@auto", "A", "B", "C" };
                var t = 0.0;
                foreach (var id in ids)
                {
                    dwell.Focused(id, t);
                    for (var frame = 0.0; frame < 0.1; frame += 0.02)
                    {
                        var hit = dwell.Poll(id, selected, t + frame);
                        if (hit != null) { switches.Add(hit); selected = hit; }
                    }
                    t += 0.1;
                }
                Check.Equal(0, switches.Count);
                // The cursor rests on C.
                for (var frame = 0.0; frame <= 0.4; frame += 0.02)
                {
                    var hit = dwell.Poll("C", selected, t + frame);
                    if (hit != null) { switches.Add(hit); selected = hit; }
                }
                Check.Equal("C", string.Join(",", switches));
            });

            test("moving_the_cursor_off_the_category_before_the_dwell_cancels_the_switch", () =>
            {
                var dwell = new CategoryFocusDwell(0.35);
                dwell.Focused("A", 1.0);
                // → into the right column (cursor no longer on a category), then the dwell would have elapsed.
                Check.Null(dwell.Poll(null, "@none", 1.1));
                Check.Null(dwell.Poll(null, "@none", 2.0));
                Check.False(dwell.Pending);

                // Cursor moved on to another category without its own Focused call (e.g. a quiet selection): no switch either.
                dwell.Focused("A", 3.0);
                Check.Null(dwell.Poll("B", "@none", 3.5));
                Check.False(dwell.Pending);
            });

            test("focusing_the_selected_category_or_cancelling_never_switches", () =>
            {
                var dwell = new CategoryFocusDwell(0.35);
                dwell.Focused("A", 0.0);
                Check.Null(dwell.Poll("A", "A", 1.0)); // already shown
                Check.False(dwell.Pending);

                dwell.Focused("B", 0.0);
                dwell.Cancel(); // clicked / submitted (selected at once), manage mode, panel closed
                Check.Null(dwell.Poll("B", "A", 1.0));

                // Re-focusing the pending category does not restart its timer.
                dwell.Focused("C", 0.0);
                dwell.Focused("C", 0.3);
                Check.Equal("C", dwell.Poll("C", "A", 0.36));

                Check.Throws<ArgumentOutOfRangeException>(() => new CategoryFocusDwell(-1));
                Check.True(CategoryFocusDwell.DefaultSeconds >= 0.35);
            });

            // ---- where the cursor lands (never on the load panel's new-game row by itself) -----------------------

            test("load_panel_initial_focus_is_the_first_real_save_not_the_new_game_row", () =>
            {
                var existing = new HashSet<string> { "001", "003", "005" };
                var none = new HashSet<string>();
                // Row 0 is 「＋ 開始新的旅程」 (002 has no file).
                Check.Equal(1, FocusRules.InitialRow(true, new[] { "002", "003", "001" }, existing, none));
                // A broken save (cannot be loaded) is skipped too.
                Check.Equal(2, FocusRules.InitialRow(true, new[] { "002", "003", "001" }, existing, new HashSet<string> { "003" }));
                // Only the new-game row (empty category) or only broken saves: nothing in the list; the caller uses the sidebar.
                Check.Equal(-1, FocusRules.InitialRow(true, new[] { "002" }, existing, none));
                Check.Equal(-1, FocusRules.InitialRow(true, new[] { "002", "003" }, existing, new HashSet<string> { "003" }));
                Check.Equal(-1, FocusRules.InitialRow(true, new string[0], existing, none));
                // Every slot taken: there is no new-game row, the first save is row 0.
                Check.Equal(0, FocusRules.InitialRow(true, new[] { "005", "001" }, existing, none));
            });

            test("save_panel_initial_focus_stays_on_the_first_row", () =>
            {
                var existing = new HashSet<string> { "001" };
                var none = new HashSet<string>();
                // 「＋ 存到…」 writes a new slot (nothing is lost), so the save panel keeps its first row.
                Check.Equal(0, FocusRules.InitialRow(false, new[] { "002", "001" }, existing, none));
                Check.Equal(0, FocusRules.InitialRow(false, new[] { "002" }, existing, none));
                Check.Equal(-1, FocusRules.InitialRow(false, new string[0], existing, none));
                Check.Throws<ArgumentNullException>(() => FocusRules.InitialRow(true, null, existing, none));
                Check.Throws<ArgumentNullException>(() => FocusRules.InitialRow(true, new string[0], null, none));
                Check.Throws<ArgumentNullException>(() => FocusRules.InitialRow(true, new string[0], existing, null));
            });

            test("after_a_removal_the_load_panel_cursor_does_not_fall_onto_the_new_game_row", () =>
            {
                var none = new HashSet<string>();
                // 002 = new-game row, then saves 004, 001. The deleted row was index 1 (004 is gone now).
                var existing = new HashSet<string> { "001" };
                Check.Equal(1, FocusRules.RowAfterRemoval(true, new[] { "002", "001" }, existing, none, 1));
                // The last save was deleted from the bottom: the one above it.
                existing = new HashSet<string> { "004" };
                Check.Equal(1, FocusRules.RowAfterRemoval(true, new[] { "002", "004" }, existing, none, 2));
                // Nothing loadable is left: the sidebar (-1), never the new-game row.
                Check.Equal(-1, FocusRules.RowAfterRemoval(true, new[] { "002" }, new HashSet<string>(), none, 1));
                // Save panel: the plain neighbour, as before (its first row only saves).
                Check.Equal(0, FocusRules.RowAfterRemoval(false, new[] { "002" }, new HashSet<string>(), none, 1));
                Check.Equal(-1, FocusRules.RowAfterRemoval(false, new string[0], new HashSet<string>(), none, 0));
            });
        }
    }
}
