using System;
using System.Collections.Generic;

namespace LoMSaveManager
{
    /// <summary>Up/down neighbours for a vertical list of <see cref="Count"/> slots; -1 means "no neighbour".</summary>
    public sealed class NavPlan
    {
        internal NavPlan(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public int Up(int index)
        {
            Require(index);
            return index == 0 ? -1 : index - 1;
        }

        public int Down(int index)
        {
            Require(index);
            return index == Count - 1 ? -1 : index + 1;
        }

        private void Require(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index), index, "0.." + (Count - 1));
        }
    }

    /// <summary>
    /// Which slot rows the game's load and save panels show, and in what order. Pure list logic; the plugin turns
    /// each key into one of the game's own slot objects.
    /// </summary>
    public static class SlotListPlan
    {
        /// <summary>
        /// Rows of the load panel. Clicking an empty row there starts a new game (that is the game's only "new game"
        /// entry), so the list holds exactly one empty row: <paramref name="newGameKeyOrNull"/> (the lowest free key),
        /// first; then the category's keys that really have a save, in the given order. Null = every slot is taken, no empty row.
        /// </summary>
        public static IReadOnlyList<string> ForLoad(IReadOnlyList<string> keysInCategory, ISet<string> existing, string newGameKeyOrNull)
        {
            if (keysInCategory == null) throw new ArgumentNullException(nameof(keysInCategory));
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            RequireManualOrNull(newGameKeyOrNull, nameof(newGameKeyOrNull));

            var rows = new List<string>();
            var seen = new HashSet<string>();
            // A "free" key that has a save after all is not an empty row; it is listed below if it belongs here.
            if (newGameKeyOrNull != null && !existing.Contains(newGameKeyOrNull))
            {
                rows.Add(newGameKeyOrNull);
                seen.Add(newGameKeyOrNull);
            }

            foreach (var key in keysInCategory)
                if (SlotKey.IsManual(key) && existing.Contains(key) && seen.Add(key)) rows.Add(key);
            return rows;
        }

        /// <summary>Rows of the save panel: the new slot first (none when every slot is taken), then the category's saves.</summary>
        public static IReadOnlyList<string> ForSave(IReadOnlyList<string> keysInCategory, string newSlotKeyOrNull)
        {
            if (keysInCategory == null) throw new ArgumentNullException(nameof(keysInCategory));
            RequireManualOrNull(newSlotKeyOrNull, nameof(newSlotKeyOrNull));

            var rows = new List<string>();
            var seen = new HashSet<string>();
            if (newSlotKeyOrNull != null)
            {
                rows.Add(newSlotKeyOrNull);
                seen.Add(newSlotKeyOrNull);
            }

            foreach (var key in keysInCategory)
                if (SlotKey.IsManual(key) && seen.Add(key)) rows.Add(key);
            return rows;
        }

        /// <summary>
        /// Whether the save panel's first row is the disabled 「存檔數已達上限」 hint: a save panel that
        /// lists slots and has no free key left (every slot 001..9999 is taken). Overwriting still works.
        /// </summary>
        public static bool ShowsFullHint(bool isSavePanel, bool showsSlotList, string newSlotKeyOrNull)
        {
            return isSavePanel && showsSlotList && newSlotKeyOrNull == null;
        }

        /// <summary>The full hint's title, 「存檔數已達上限 9999」, in <paramref name="leanLanguage"/>.</summary>
        public static string FullHintTitle(string leanLanguage)
        {
            return string.Format(Strings.Get("Slot.Full", leanLanguage), SlotKey.MaxManualSlot);
        }

        /// <summary>Navigation for any count, including 0 and 1 (the game's own code indexes [i+1] and [0] unguarded).</summary>
        public static NavPlan Navigation(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "must be >= 0");
            return new NavPlan(count);
        }

        /// <summary>
        /// Runs <paramref name="setup"/> for every key even when some throw (a corrupt save throws from the game's
        /// loader), and returns the keys that failed, in order.
        /// </summary>
        public static IReadOnlyList<string> SetupAll(IReadOnlyList<string> keys, Action<string> setup)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            var failed = new List<string>();
            foreach (var key in keys)
            {
                try { setup(key); }
                catch (Exception) { failed.Add(key); }
            }
            return failed;
        }

        private static void RequireManualOrNull(string key, string name)
        {
            if (key != null && !SlotKey.IsManual(key)) throw new ArgumentException("Not a manual slot key: " + key, name);
        }
    }

    /// <summary>What a panel needs to build its list: the category's saves (newest first), all existing saves, and the lowest free key.</summary>
    public sealed class SlotListKeys
    {
        public SlotListKeys(IReadOnlyList<string> keys, ISet<string> existing, string newSlotKey)
        {
            Keys = keys;
            Existing = existing;
            NewSlotKey = newSlotKey;
        }

        public IReadOnlyList<string> Keys { get; }

        public ISet<string> Existing { get; }

        /// <summary>The lowest key with no .dat/.tmp/.bak, or null when 001..9999 are all taken.</summary>
        public string NewSlotKey { get; }
    }
}
