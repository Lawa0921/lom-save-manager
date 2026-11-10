using System;
using System.Collections.Generic;
using System.Linq;

namespace LoMSaveManager
{
    /// <summary>One user category as the sidebar shows it. <see cref="Count"/> is the number of saves currently assigned to it.</summary>
    public sealed class CategoryView
    {
        public CategoryView(string id, string name, int count)
        {
            Id = id;
            Name = name;
            Count = count;
        }

        public string Id { get; }

        public string Name { get; }

        public int Count { get; }
    }

    /// <summary>
    /// What <see cref="SaveCatalog.BeginOwnWrite"/> captured about a slot just before the game writes it,
    /// to be handed to <see cref="SaveCatalog.CompleteOwnWrite"/>. It carries nothing for a slot whose old file
    /// was not the one the index described.
    /// </summary>
    public sealed class OwnWriteTicket
    {
        internal OwnWriteTicket(string key, SaveStamp? stampBefore, bool hadRecord, string name, string categoryId)
        {
            Key = key;
            StampBefore = stampBefore;
            HadRecord = hadRecord;
            Name = name;
            CategoryId = categoryId;
        }

        public string Key { get; }

        /// <summary>The slot's file as it was before the write; null when there was none.</summary>
        internal SaveStamp? StampBefore { get; }

        /// <summary>True when the index had a record that matched the old file (so Name and CategoryId are carried over).</summary>
        internal bool HadRecord { get; }

        internal string Name { get; }

        internal string CategoryId { get; }
    }

    /// <summary>
    /// User categories, save names and stamp checks over a <see cref="SaveIndex"/>.
    /// It mutates the index it was given (the caller persists that same object with
    /// <see cref="SaveIndexStore"/>) and does no file I/O of its own.
    /// Call <see cref="Reconcile"/> with the current files before naming or assigning saves,
    /// so a record created for a save that already exists is stamped with that file's state.
    /// </summary>
    public sealed class SaveCatalog
    {
        public const int MaxCategories = 200;
        public const int MaxCategoryNameLength = 24;
        public const int MaxSlotNameLength = 40;

        /// <summary>
        /// Stable keys carried in the <see cref="ArgumentException.Message"/> of every validation failure.
        /// The UI maps them to localized text; the message is never shown as-is.
        /// </summary>
        public static class ErrorKeys
        {
            public const string CategoryNameEmpty = "CategoryName.Empty";
            public const string CategoryNameTooLong = "CategoryName.TooLong";
            public const string SlotNameTooLong = "SlotName.TooLong";
            public const string CategoryLimit = "Category.Limit";
            public const string CategoryNotFound = "Category.NotFound";
            public const string SaveNotScanned = "Save.NotScanned";
        }

        private readonly SaveIndex _index;

        // File stamps from the latest Reconcile / RecordOwnWrite. A record that is created later for a save
        // that already exists (the user names an old save) is stamped from here, so the next Reconcile keeps it.
        private readonly Dictionary<string, SaveStamp> _knownStamps = new Dictionary<string, SaveStamp>();

        public SaveCatalog(SaveIndex index)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            index.Normalize();
            _index = index;
        }

        /// <summary>Sort preference of the slot list (stored in the index): true = newest save first.</summary>
        public bool NewestFirst
        {
            get { return _index.NewestFirst; }
            set { _index.NewestFirst = value; }
        }

        /// <summary>
        /// Drops the records of saves whose file is gone or whose length or content hash no longer matches (the file was
        /// replaced or rewritten outside this mod). A different write time alone never drops a record: Steam Cloud
        /// restores files on another machine with the same bytes but a different time. Returns whether any record was removed.
        /// </summary>
        public bool Reconcile(IDictionary<string, SaveStamp> existingManualSaves)
        {
            if (existingManualSaves == null) throw new ArgumentNullException(nameof(existingManualSaves));

            var stale = new List<string>();
            foreach (var pair in _index.Slots)
            {
                SaveStamp stamp;
                if (!existingManualSaves.TryGetValue(pair.Key, out stamp) || !stamp.Matches(pair.Value))
                    stale.Add(pair.Key);
            }
            foreach (var key in stale) _index.Slots.Remove(key);

            _knownStamps.Clear();
            foreach (var pair in existingManualSaves)
                if (SlotKey.IsManual(pair.Key)) _knownStamps[pair.Key] = pair.Value;

            return stale.Count > 0;
        }

        /// <summary>Categories in Order. Count is the number of recorded saves assigned to each (exact after <see cref="Reconcile"/>).</summary>
        public IReadOnlyList<CategoryView> UserCategories()
        {
            var counts = new Dictionary<string, int>();
            foreach (var record in _index.Slots.Values)
            {
                if (record.CategoryId == null) continue;
                int n;
                counts.TryGetValue(record.CategoryId, out n);
                counts[record.CategoryId] = n + 1;
            }

            return Ordered().Select(c =>
            {
                int n = 0;
                if (c.Id != null) counts.TryGetValue(c.Id, out n);
                return new CategoryView(c.Id, c.Name, n);
            }).ToList();
        }

        public int UncategorizedCount(IEnumerable<string> existingKeys)
        {
            if (existingKeys == null) throw new ArgumentNullException(nameof(existingKeys));
            var categoryIds = CategoryIds();
            return existingKeys.Count(key => SlotKey.IsManual(key) && EffectiveCategory(key, categoryIds) == null);
        }

        /// <summary>
        /// The existing manual keys in a category, sorted by <paramref name="sortTicks"/>; equal ticks fall back to
        /// <see cref="SlotKey.NumericOrder"/>, and <paramref name="newestFirst"/> reverses the whole order.
        /// <paramref name="categoryId"/> null means uncategorized: no record, no category, or a category that no longer exists.
        /// Keys that are not manual slot keys (auto, universe, ...) are never listed.
        /// </summary>
        public IReadOnlyList<string> KeysIn(string categoryId, IEnumerable<string> existingKeys, bool newestFirst, Func<string, long> sortTicks)
        {
            if (existingKeys == null) throw new ArgumentNullException(nameof(existingKeys));
            if (sortTicks == null) throw new ArgumentNullException(nameof(sortTicks));

            var categoryIds = CategoryIds();
            var entries = new List<KeyValuePair<string, long>>();
            foreach (var key in existingKeys)
            {
                if (!SlotKey.IsManual(key) || EffectiveCategory(key, categoryIds) != categoryId) continue;
                entries.Add(new KeyValuePair<string, long>(key, sortTicks(key)));
            }

            entries.Sort((x, y) =>
            {
                var byTicks = x.Value.CompareTo(y.Value);
                var order = byTicks != 0 ? byTicks : SlotKey.NumericOrder.Compare(x.Key, y.Key);
                return newestFirst ? -order : order;
            });
            return entries.Select(e => e.Key).ToList();
        }

        /// <summary>Adds a category at the end and returns its id.</summary>
        public string CreateCategory(string name)
        {
            var clean = CleanCategoryName(name);
            if (_index.Categories.Count >= MaxCategories) throw new ArgumentException(ErrorKeys.CategoryLimit);

            var record = new CategoryRecord { Id = Guid.NewGuid().ToString("N"), Name = clean };
            var ordered = Ordered();
            ordered.Add(record);
            SetOrder(ordered);
            return record.Id;
        }

        public void RenameCategory(string id, string name)
        {
            var clean = CleanCategoryName(name);
            FindCategory(id).Name = clean;
        }

        /// <summary>Removes a category; its saves become uncategorized and keep their names.</summary>
        public void DeleteCategory(string id)
        {
            var category = FindCategory(id);
            foreach (var record in _index.Slots.Values)
                if (record.CategoryId == id) record.CategoryId = null;

            var ordered = Ordered();
            ordered.Remove(category);
            SetOrder(ordered);
        }

        /// <summary>Moves a category to position <paramref name="newOrder"/> (clamped to 0..n-1) and renumbers Order as 0..n-1.</summary>
        public void MoveCategory(string id, int newOrder)
        {
            var category = FindCategory(id);
            var ordered = Ordered();
            ordered.Remove(category);
            ordered.Insert(Math.Max(0, Math.Min(newOrder, ordered.Count)), category);
            SetOrder(ordered);
        }

        /// <summary>Puts a save in a category (null = uncategorized).</summary>
        public void Assign(string key, string categoryId)
        {
            RequireManual(key);
            if (categoryId == null)
            {
                SlotRecord existing;
                if (_index.Slots.TryGetValue(key, out existing)) existing.CategoryId = null;
                return;
            }

            FindCategory(categoryId);
            RecordFor(key).CategoryId = categoryId;
        }

        /// <summary>Names a save. The name is trimmed; empty or whitespace clears it.</summary>
        public void SetName(string key, string name)
        {
            RequireManual(key);
            var clean = name == null ? null : name.Trim();
            if (string.IsNullOrEmpty(clean)) clean = null;
            if (clean != null && clean.Length > MaxSlotNameLength) throw new ArgumentException(ErrorKeys.SlotNameTooLong);

            if (clean == null)
            {
                SlotRecord existing;
                if (_index.Slots.TryGetValue(key, out existing)) existing.Name = null;
                return;
            }

            RecordFor(key).Name = clean;
        }

        /// <summary>The save's name, or null when it has none (or is not a manual key).</summary>
        public string NameOf(string key)
        {
            SlotRecord record;
            if (key == null || !_index.Slots.TryGetValue(key, out record)) return null;
            return string.IsNullOrEmpty(record.Name) ? null : record.Name;
        }

        /// <summary>The save's category id, or null when it is uncategorized (no record, or its category no longer exists).</summary>
        public string CategoryOf(string key)
        {
            return key == null ? null : EffectiveCategory(key, CategoryIds());
        }

        /// <summary>
        /// Call after this mod writes a slot. Keeps the existing name and category and only refreshes the stamp,
        /// so the overwrite is not mistaken for a replaced file. A key without a record gets one in
        /// <paramref name="categoryIdIfNew"/> (null, or an id that no longer exists, means uncategorized).
        /// </summary>
        public void RecordOwnWrite(string key, SaveStamp stamp, string categoryIdIfNew)
        {
            RequireManual(key);
            _knownStamps[key] = stamp;

            SlotRecord record;
            if (_index.Slots.TryGetValue(key, out record))
            {
                record.Length = stamp.Length;
                record.Sha256 = stamp.Sha256;
                return;
            }

            var categoryId = categoryIdIfNew != null && _index.Categories.Any(c => c.Id == categoryIdIfNew) ? categoryIdIfNew : null;
            _index.Slots[key] = new SlotRecord { CategoryId = categoryId, Length = stamp.Length, Sha256 = stamp.Sha256 };
        }

        /// <summary>
        /// Call just before the game writes a manual slot, with a scan of the save folder taken at that moment.
        /// Drops the records that no longer describe their files (so a slot whose file was deleted or replaced outside
        /// the game does not hand its old name to the new save), then captures this slot's name and category.
        /// </summary>
        public OwnWriteTicket BeginOwnWrite(string key, IDictionary<string, SaveStamp> savesBeforeWrite)
        {
            RequireManual(key);
            if (savesBeforeWrite == null) throw new ArgumentNullException(nameof(savesBeforeWrite));

            Reconcile(savesBeforeWrite);

            SaveStamp before;
            SaveStamp? stampBefore = savesBeforeWrite.TryGetValue(key, out before) ? before : (SaveStamp?)null;
            SlotRecord record;
            if (_index.Slots.TryGetValue(key, out record))
                return new OwnWriteTicket(key, stampBefore, true, record.Name, record.CategoryId);
            return new OwnWriteTicket(key, stampBefore, false, null, null);
        }

        /// <summary>
        /// Call right after the game wrote the slot, with a fresh scan. The slot's captured name and category stay, its
        /// stamp is refreshed, and a slot without a captured record goes to <paramref name="categoryIdIfNew"/>.
        /// The other saves are reconciled as usual. If the file is unchanged (the game swallows write errors) or
        /// missing, nothing is recorded for the slot. "Unchanged" is judged on this machine: same length, same write
        /// time and same content hash as captured by <see cref="BeginOwnWrite"/>.
        /// </summary>
        public void CompleteOwnWrite(OwnWriteTicket ticket, IDictionary<string, SaveStamp> savesAfterWrite, string categoryIdIfNew)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            if (savesAfterWrite == null) throw new ArgumentNullException(nameof(savesAfterWrite));

            SaveStamp stamp;
            var written = savesAfterWrite.TryGetValue(ticket.Key, out stamp)
                && !(ticket.StampBefore.HasValue
                    && ticket.StampBefore.Value.SameLocalState(stamp)
                    && string.Equals(ticket.StampBefore.Value.Sha256, stamp.Sha256, StringComparison.OrdinalIgnoreCase));
            if (!written)
            {
                Reconcile(savesAfterWrite);
                return;
            }

            // This slot's record still carries the old file's stamp, so reconcile everything else and rebuild it.
            var others = new Dictionary<string, SaveStamp>(savesAfterWrite);
            others.Remove(ticket.Key);
            Reconcile(others);

            RecordOwnWrite(ticket.Key, stamp, ticket.HadRecord ? ticket.CategoryId : categoryIdIfNew);
            if (ticket.HadRecord && ticket.Name != null) _index.Slots[ticket.Key].Name = ticket.Name;
        }

        /// <summary>Drops a save's record (after the game deleted the slot). Unknown or non-manual keys are ignored.</summary>
        public void Forget(string key)
        {
            if (key == null) return;
            _index.Slots.Remove(key);
            _knownStamps.Remove(key);
        }

        private static string CleanCategoryName(string name)
        {
            var clean = name == null ? string.Empty : name.Trim();
            if (clean.Length == 0) throw new ArgumentException(ErrorKeys.CategoryNameEmpty);
            if (clean.Length > MaxCategoryNameLength) throw new ArgumentException(ErrorKeys.CategoryNameTooLong);
            return clean;
        }

        private static void RequireManual(string key)
        {
            if (!SlotKey.IsManual(key))
                throw new ArgumentException("Not a manual slot key: " + (key ?? "<null>"), nameof(key));
        }

        private CategoryRecord FindCategory(string id)
        {
            var category = id == null ? null : _index.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null) throw new ArgumentException(ErrorKeys.CategoryNotFound);
            return category;
        }

        /// <summary>Categories by Order; equal Orders (a hand-edited file) keep their list order.</summary>
        private List<CategoryRecord> Ordered()
        {
            return _index.Categories.OrderBy(c => c.Order).ToList();
        }

        /// <summary>Makes the list and the Order values match <paramref name="ordered"/>, numbered 0..n-1.</summary>
        private void SetOrder(List<CategoryRecord> ordered)
        {
            for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            _index.Categories.Clear();
            _index.Categories.AddRange(ordered);
        }

        private HashSet<string> CategoryIds()
        {
            return new HashSet<string>(_index.Categories.Where(c => c.Id != null).Select(c => c.Id));
        }

        private string EffectiveCategory(string key, HashSet<string> categoryIds)
        {
            SlotRecord record;
            if (!_index.Slots.TryGetValue(key, out record)) return null;
            return record.CategoryId != null && categoryIds.Contains(record.CategoryId) ? record.CategoryId : null;
        }

        /// <summary>
        /// The record for a key, created when missing and stamped with the file state seen at the last Reconcile.
        /// Refused (<see cref="ErrorKeys.SaveNotScanned"/>) when that state is unknown (no successful scan saw the file):
        /// a record without a hash would be dropped silently by the next Reconcile, losing the edit.
        /// </summary>
        private SlotRecord RecordFor(string key)
        {
            SlotRecord record;
            if (_index.Slots.TryGetValue(key, out record)) return record;

            SaveStamp stamp;
            if (!_knownStamps.TryGetValue(key, out stamp) || stamp.Sha256 == null) throw new ArgumentException(ErrorKeys.SaveNotScanned);
            record = new SlotRecord { Length = stamp.Length, Sha256 = stamp.Sha256 };
            _index.Slots[key] = record;
            return record;
        }
    }
}
