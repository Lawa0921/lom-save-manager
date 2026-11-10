using System;
using System.Collections.Generic;

namespace LoMSaveManager
{
    /// <summary>
    /// When keyboard/gamepad focus on a sidebar category turns into selecting it. The approved design wants the right
    /// column to follow ↑↓ in the sidebar "like tabs", but showing a category rebuilds its list, and every row reads its
    /// save file through the game's Setup() — too slow to repeat for every category the cursor passes over. So focus
    /// only switches once the cursor has rested on one category for <see cref="DefaultSeconds"/> (unscaled time);
    /// a click or the submit key selects at once (and cancels any pending switch).
    /// </summary>
    public sealed class CategoryFocusDwell
    {
        /// <summary>How long the cursor must rest on a category (seconds of unscaled time) before it is shown.</summary>
        public const double DefaultSeconds = 0.35;

        private readonly double _seconds;
        private string _candidate;
        private double _since;

        public CategoryFocusDwell(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "must be >= 0");
            _seconds = seconds;
        }

        /// <summary>True while a focused category waits for its dwell.</summary>
        public bool Pending
        {
            get { return _candidate != null; }
        }

        /// <summary>The player's navigation put the cursor on category <paramref name="id"/> at <paramref name="now"/>.</summary>
        public void Focused(string id, double now)
        {
            if (id == null)
            {
                Cancel();
                return;
            }
            // The same category again (a repeated select event) keeps its original start.
            if (id == _candidate) return;
            _candidate = id;
            _since = now;
        }

        /// <summary>Drops a pending switch (the category was clicked, manage mode, a dialog, the panel closed).</summary>
        public void Cancel()
        {
            _candidate = null;
        }

        /// <summary>
        /// Call every frame. <paramref name="focusedId"/> is the category whose button has the cursor now (null when
        /// the cursor is elsewhere). Returns the category to select now, or null; a returned switch fires once.
        /// </summary>
        public string Poll(string focusedId, string selectedId, double now)
        {
            if (_candidate == null) return null;
            if (focusedId != _candidate || _candidate == selectedId)
            {
                _candidate = null;
                return null;
            }
            // A tiny tolerance: frame times are floats (10.35 - 10.0 is 0.3499999…).
            if (now - _since + 1e-6 < _seconds) return null;
            var id = _candidate;
            _candidate = null;
            return id;
        }
    }

    /// <summary>
    /// Which right-column row gets the cursor without the player choosing it. On the load panel an empty row is
    /// 「＋ 開始新的旅程」: clicking it starts a new game at once (the game asks nothing), so the cursor never lands on
    /// it by itself — only on a real, readable save; the new-game row stays one ↑ away. -1 = no such row (the caller
    /// then puts the cursor on the sidebar's selected category).
    /// </summary>
    public static class FocusRules
    {
        /// <summary>The row the cursor starts on when the panel opens (or when → enters the right column).</summary>
        public static int InitialRow(bool isLoadPanel, IReadOnlyList<string> rows, ISet<string> existing, ISet<string> broken)
        {
            Require(rows, existing, broken);
            if (!isLoadPanel) return rows.Count > 0 ? 0 : -1;
            for (var i = 0; i < rows.Count; i++)
                if (IsLoadable(rows[i], existing, broken)) return i;
            return -1;
        }

        /// <summary>
        /// After the row at <paramref name="oldIndex"/> left the list (deleted, moved away), in the new
        /// <paramref name="rows"/>: the row that took its place, else the nearest one above; on the load panel only
        /// loadable saves count.
        /// </summary>
        public static int RowAfterRemoval(bool isLoadPanel, IReadOnlyList<string> rows, ISet<string> existing, ISet<string> broken, int oldIndex)
        {
            Require(rows, existing, broken);
            if (rows.Count == 0) return -1;
            var start = Math.Max(0, Math.Min(oldIndex, rows.Count - 1));
            if (!isLoadPanel) return start;
            for (var i = start; i < rows.Count; i++)
                if (IsLoadable(rows[i], existing, broken)) return i;
            for (var i = start - 1; i >= 0; i--)
                if (IsLoadable(rows[i], existing, broken)) return i;
            return -1;
        }

        private static bool IsLoadable(string key, ISet<string> existing, ISet<string> broken)
        {
            return key != null && existing.Contains(key) && !broken.Contains(key);
        }

        private static void Require(IReadOnlyList<string> rows, ISet<string> existing, ISet<string> broken)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (broken == null) throw new ArgumentNullException(nameof(broken));
        }
    }
}
