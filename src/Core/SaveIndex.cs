using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace LoMSaveManager
{
    /// <summary>
    /// The mod's own metadata file (LoMSaveManager.json in the save folder root, synced by Steam Cloud):
    /// user categories and per-slot names. The game's .dat files are never touched.
    /// Version 1 was LoMSaveManager/index.json with write-time stamps; see <see cref="MetadataMigration"/>.
    /// </summary>
    [DataContract]
    public sealed class SaveIndex
    {
        public const int CurrentVersion = 2;

        public SaveIndex()
        {
            SetDefaults();
        }

        [DataMember] public int Version { get; set; }

        [DataMember] public List<CategoryRecord> Categories { get; set; }

        /// <summary>Keyed by normalized slot key (for example "001").</summary>
        [DataMember] public Dictionary<string, SlotRecord> Slots { get; set; }

        /// <summary>Sort preference for the slot list; true means newest save first.</summary>
        [DataMember] public bool NewestFirst { get; set; }

        // DataContract deserialization skips constructors and field initializers, so members
        // missing from the JSON would otherwise stay 0 / null / false.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Version = CurrentVersion;
            Categories = new List<CategoryRecord>();
            Slots = new Dictionary<string, SlotRecord>();
            NewestFirst = true;
        }

        /// <summary>
        /// True when the index holds nothing a user made: no categories, the default sort, and no slot record with a name
        /// or a category. Records holding only a stamp (every save the game writes gets one) do not count: they carry
        /// nothing worth syncing, so they must not be the reason the synced file is created.
        /// </summary>
        internal bool IsEmpty
        {
            get
            {
                if (Categories != null && Categories.Count > 0) return false;
                if (!NewestFirst) return false;
                if (Slots == null) return true;
                foreach (var record in Slots.Values)
                    if (record != null && (!string.IsNullOrEmpty(record.Name) || record.CategoryId != null)) return false;
                return true;
            }
        }

        /// <summary>
        /// Makes loaded data obey the same rules the UI enforces, because the file may have been hand-edited, damaged, or
        /// written elsewhere and synced: explicit nulls are dropped; categories without an id or with a repeated id are
        /// dropped (the first wins); at most <see cref="SaveCatalog.MaxCategories"/> categories are kept (lowest Order
        /// first); category names are trimmed and cut to <see cref="SaveCatalog.MaxCategoryNameLength"/>; slot records whose
        /// key is not a manual slot are dropped; slot names are trimmed (blank means none) and cut to
        /// <see cref="SaveCatalog.MaxSlotNameLength"/>. Data that already obeys the rules is left exactly as it is.
        /// This only changes the object in memory; it never writes the file.
        /// </summary>
        internal void Normalize()
        {
            if (Categories == null) Categories = new List<CategoryRecord>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            Categories.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Id) || !seenIds.Add(c.Id));
            if (Categories.Count > SaveCatalog.MaxCategories)
            {
                // OrderBy is stable, so equal Orders keep their list order, as the catalog shows them.
                var kept = new HashSet<CategoryRecord>(Categories.OrderBy(c => c.Order).Take(SaveCatalog.MaxCategories));
                Categories.RemoveAll(c => !kept.Contains(c));
            }
            foreach (var category in Categories)
                category.Name = Cut(category.Name == null ? string.Empty : category.Name.Trim(), SaveCatalog.MaxCategoryNameLength);

            if (Slots == null)
            {
                Slots = new Dictionary<string, SlotRecord>();
                return;
            }

            var dropped = new List<string>();
            foreach (var pair in Slots)
            {
                if (pair.Value == null || !SlotKey.IsManual(pair.Key))
                {
                    dropped.Add(pair.Key);
                    continue;
                }
                var name = pair.Value.Name;
                if (name == null) continue;
                var clean = name.Trim();
                // Only touch a name that breaks a rule, so a valid record stays byte-for-byte the same.
                if (clean.Length == 0) pair.Value.Name = name.Length == 0 ? name : null;
                else if (clean.Length != name.Length || clean.Length > SaveCatalog.MaxSlotNameLength)
                    pair.Value.Name = Cut(clean, SaveCatalog.MaxSlotNameLength);
            }
            foreach (var key in dropped) Slots.Remove(key);
        }

        /// <summary>The first <paramref name="max"/> UTF-16 units of <paramref name="text"/>, never splitting a surrogate pair.</summary>
        private static string Cut(string text, int max)
        {
            if (text.Length <= max) return text;
            var length = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
            return text.Substring(0, length);
        }
    }

    [DataContract]
    public sealed class CategoryRecord
    {
        /// <summary>Stable identifier, <c>Guid.NewGuid().ToString("N")</c>.</summary>
        [DataMember] public string Id { get; set; }

        [DataMember] public string Name { get; set; }

        [DataMember] public int Order { get; set; }
    }

    [DataContract]
    public sealed class SlotRecord
    {
        [DataMember] public string Name { get; set; }

        /// <summary>null means uncategorized.</summary>
        [DataMember] public string CategoryId { get; set; }

        /// <summary>Length of the slot's .dat file when this record was last confirmed to belong to it.</summary>
        [DataMember] public long Length { get; set; }

        /// <summary>SHA-256 (lowercase hex) of the slot's .dat file at the same moment. Write times are not stored: they differ between machines.</summary>
        [DataMember] public string Sha256 { get; set; }
    }
}
