using System;
using System.Collections.Generic;
using System.Linq;

namespace LoMSaveManager.Tests
{
    internal static class SlotKeyTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("manual_keys_accept_canonical_forms", () =>
            {
                foreach (var key in new[] { "001", "020", "021", "999", "1000", "9999" })
                    Check.True(SlotKey.IsManual(key));
            });

            test("manual_keys_reject_others", () =>
            {
                foreach (var key in new[] { "000", "1", "01", "0001", "10000", "auto", "auto_free", "universe", "act1", "", null, "02a" })
                    Check.False(SlotKey.IsManual(key));
            });

            test("manual_keys_reject_non_ascii_digits_and_trailing_newline", () =>
            {
                Check.False(SlotKey.IsManual("١٢٣"));
                Check.False(SlotKey.IsManual("001\n"));
                Check.False(SlotKey.IsManual(" 001"));
            });

            test("format_pads_like_the_game", () =>
            {
                Check.Equal("007", SlotKey.Format(7));
                Check.Equal("001", SlotKey.Format(1));
                Check.Equal("1000", SlotKey.Format(1000));
                Check.Equal("9999", SlotKey.Format(9999));
                Check.Throws<ArgumentOutOfRangeException>(() => SlotKey.Format(0));
                Check.Throws<ArgumentOutOfRangeException>(() => SlotKey.Format(10000));
                Check.Throws<ArgumentOutOfRangeException>(() => SlotKey.Format(-5));
            });

            test("format_round_trips_through_number_for_every_manual_slot", () =>
            {
                for (var n = 1; n <= SlotKey.MaxManualSlot; n++)
                {
                    var key = SlotKey.Format(n);
                    Check.True(SlotKey.IsManual(key));
                    Check.Equal(n, SlotKey.Number(key));
                }
            });

            test("number_rejects_non_manual_keys", () =>
            {
                Check.Throws<ArgumentException>(() => SlotKey.Number("auto"));
                Check.Throws<ArgumentException>(() => SlotKey.Number("000"));
                Check.Throws<ArgumentException>(() => SlotKey.Number(null));
            });

            test("numeric_order_sorts_1000_after_999", () =>
            {
                var keys = new List<string> { "1000", "999", "002", "9999", "021", "001" };
                keys.Sort(SlotKey.NumericOrder);
                Check.Equal("001,002,021,999,1000,9999", string.Join(",", keys));
                Check.True(SlotKey.NumericOrder.Compare("999", "1000") < 0);
                Check.True(SlotKey.NumericOrder.Compare("1000", "999") > 0);
                Check.Equal(0, SlotKey.NumericOrder.Compare("007", "007"));
            });

            test("numeric_order_puts_non_manual_keys_after_manual_ones", () =>
            {
                var keys = new List<string> { "auto", "9999", "001", "act1", "1000" };
                keys.Sort(SlotKey.NumericOrder);
                Check.Equal("001,1000,9999,act1,auto", string.Join(",", keys));
            });

            test("lowest_free_fills_gaps_from_001", () =>
            {
                var gap = Range(1, 20);
                gap.Remove("007");
                Check.Equal("007", SlotAllocator.LowestFree(gap));
                Check.Equal("021", SlotAllocator.LowestFree(Range(1, 20)));
                Check.Equal("001", SlotAllocator.LowestFree(new HashSet<string>()));
                Check.Null(SlotAllocator.LowestFree(Range(1, 9999)));
            });

            test("lowest_free_uses_the_last_slot_when_only_9999_is_left", () =>
            {
                var almostFull = Range(1, 9998);
                Check.Equal("9999", SlotAllocator.LowestFree(almostFull));
            });

            test("lowest_free_ignores_non_manual_occupied_keys", () =>
            {
                var occupied = new HashSet<string> { "auto", "000", "universe" };
                Check.Equal("001", SlotAllocator.LowestFree(occupied));
            });

            test("occupied_counts_tmp_and_bak", () =>
            {
                var names = new[] { "Save_005.dat.tmp", "Save_006.dat.bak", "Save_auto.dat", "Save_1000.dat", "notes.txt" };
                Check.SetEqual(new[] { "005", "006", "1000" }, SlotFiles.OccupiedKeys(names));
            });

            test("occupied_is_case_insensitive_and_skips_non_canonical_names", () =>
            {
                var names = new[]
                {
                    "SAVE_005.DAT", "save_006.Dat.Bak", "Save_007.DAT.TMP",
                    "Save_000.dat", "Save_5.dat", "Save_0005.dat", "Save_10000.dat",
                    "Save_008.dat.old", "Save_009.txt", "Save_.dat", "Save_", "Xsave_010.dat", "", null
                };
                Check.SetEqual(new[] { "005", "006", "007" }, SlotFiles.OccupiedKeys(names));
            });

            test("occupied_feeds_lowest_free", () =>
            {
                var names = new[] { "Save_001.dat", "Save_002.dat.bak", "Save_004.dat", "Save_003.dat.tmp", "Save_auto.dat" };
                Check.Equal("005", SlotAllocator.LowestFree(SlotFiles.OccupiedKeys(names)));
            });
        }

        private static ISet<string> Range(int from, int to)
        {
            return new HashSet<string>(Enumerable.Range(from, to - from + 1).Select(SlotKey.Format));
        }
    }
}
