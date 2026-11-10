using System.Collections.Generic;
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

        /// <summary>Repairs explicit nulls ("Slots":null, null list entries) that a hand-edited or damaged file can contain.</summary>
        internal void Normalize()
        {
            if (Categories == null) Categories = new List<CategoryRecord>();
            else Categories.RemoveAll(c => c == null);

            if (Slots == null)
            {
                Slots = new Dictionary<string, SlotRecord>();
            }
            else
            {
                var nullKeys = new List<string>();
                foreach (var pair in Slots)
                    if (pair.Value == null) nullKeys.Add(pair.Key);
                foreach (var key in nullKeys) Slots.Remove(key);
            }
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
