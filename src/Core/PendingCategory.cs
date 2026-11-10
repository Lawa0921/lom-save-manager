using System;

namespace LoMSaveManager
{
    /// <summary>
    /// The category a brand-new save is filed into (the one the open load/save panel has selected). It belongs to the
    /// panel that set it: when that panel closes it is cleared, so a new game started elsewhere (the title screen's own
    /// new game, a quick save outside the panels) is uncategorized instead of landing in the last-viewed category.
    /// </summary>
    public sealed class PendingCategory
    {
        private object _owner;

        /// <summary>The category id, or null for uncategorized.</summary>
        public string Id { get; private set; }

        public void Set(object owner, string categoryId)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
            Id = categoryId;
        }

        /// <summary>Clears it if <paramref name="owner"/> is the panel that set it.</summary>
        public void Clear(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, _owner)) return;
            _owner = null;
            Id = null;
        }
    }
}
