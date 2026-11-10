using System;
using System.Linq;

namespace LoMSaveManager
{
    /// <summary>
    /// What the category sidebar, the name dialog and the move dialog do, as catalog operations. The UI only calls
    /// this: every successful change is persisted (the <c>persist</c> callback) and announced with <see cref="Changed"/>.
    /// Validation failures throw <see cref="ArgumentException"/> whose message is a <see cref="SaveCatalog.ErrorKeys"/>
    /// key (shown as localized text, nothing is persisted); operations the UI never offers on the fixed categories
    /// throw <see cref="InvalidOperationException"/>.
    ///
    /// Sidebar ids: a user category's id, or one of the fixed <see cref="Recent"/>, <see cref="Auto"/>,
    /// <see cref="Uncategorized"/>. Only the uncategorized and user categories list manual saves.
    /// </summary>
    public sealed class CategoryController
    {
        public const string Recent = "@recent";
        public const string Auto = "@auto";
        public const string Uncategorized = "@none";

        /// <summary>
        /// The <see cref="ArgumentException.Message"/> (and <see cref="Strings"/> key) of every edit refused because
        /// this session cannot save names and categories (see <see cref="CanEdit"/>).
        /// </summary>
        public const string ReadOnlyErrorKey = "Error.ReadOnly";

        private readonly SaveCatalog _catalog;
        private readonly Action _persist;
        private readonly Func<bool> _canEdit;

        public CategoryController(SaveCatalog catalog, Action persist)
            : this(catalog, persist, () => true)
        {
        }

        /// <param name="canEdit">Whether edits can be saved right now (the session's <see cref="SaveSession.CanPersist"/>).</param>
        public CategoryController(SaveCatalog catalog, Action persist, Func<bool> canEdit)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (persist == null) throw new ArgumentNullException(nameof(persist));
            if (canEdit == null) throw new ArgumentNullException(nameof(canEdit));
            _catalog = catalog;
            _persist = persist;
            _canEdit = canEdit;
            Selected = Recent;
        }

        /// <summary>
        /// False while names and categories cannot be saved this session (the file is from a newer version, unreadable
        /// or too large, a v1 migration is pending, ...). Every edit is then refused before it changes anything, with
        /// <see cref="ReadOnlyErrorKey"/>, so nothing is shown that would vanish at the next start; browsing still works.
        /// </summary>
        public bool CanEdit
        {
            get { return _canEdit(); }
        }

        /// <summary>Raised after every change of the selection or of the catalog made through this controller.</summary>
        public event Action Changed;

        public SaveCatalog Catalog
        {
            get { return _catalog; }
        }

        /// <summary>The sidebar's selected id (starts on <see cref="Recent"/>, like the game's load panel).</summary>
        public string Selected { get; private set; }

        /// <summary>True when the right column lists manual saves (uncategorized or a user category), false for recent/auto.</summary>
        public bool ShowsSlotList
        {
            get { return Selected != Recent && Selected != Auto; }
        }

        /// <summary>The selection as a catalog category id: null for uncategorized (and for recent/auto).</summary>
        public string SelectedCatalogId
        {
            get { return IsFixed(Selected) ? null : Selected; }
        }

        /// <summary>Where a brand-new save goes: the selected user category, otherwise uncategorized (null).</summary>
        public string PendingCategoryId
        {
            get { return SelectedCatalogId; }
        }

        public bool NewestFirst
        {
            get { return _catalog.NewestFirst; }
        }

        public static bool IsFixed(string id)
        {
            return id == Recent || id == Auto || id == Uncategorized;
        }

        /// <summary>Selects a sidebar entry. An id that is no longer a category falls back to <see cref="Uncategorized"/>.</summary>
        public void Select(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            var target = IsFixed(id) || Exists(id) ? id : Uncategorized;
            if (target == Selected) return;
            Selected = target;
            Raise();
        }

        /// <summary>Selects the category a save is in (uncategorized for an unknown, uncategorized or non-manual key).</summary>
        public void SelectCategoryOf(string key)
        {
            var id = key == null ? null : _catalog.CategoryOf(key);
            Select(id ?? Uncategorized);
        }

        /// <summary>Call before drawing: a selected category that was deleted elsewhere becomes <see cref="Uncategorized"/>.</summary>
        public void Validate()
        {
            if (!IsFixed(Selected) && !Exists(Selected)) Select(Uncategorized);
        }

        public string Create(string name)
        {
            RequireEditable();
            var id = _catalog.CreateCategory(name);
            Commit();
            return id;
        }

        public void Rename(string id, string name)
        {
            RequireUser(id, "renamed");
            RequireEditable();
            _catalog.RenameCategory(id, name);
            Commit();
        }

        /// <summary>Deletes a user category; its saves become uncategorized and keep their names. A selected one selects 未分類.</summary>
        public void Delete(string id)
        {
            RequireUser(id, "deleted");
            RequireEditable();
            _catalog.DeleteCategory(id);
            if (Selected == id) Selected = Uncategorized;
            Commit();
        }

        public bool CanMoveUp(string id)
        {
            return !IsFixed(id) && IndexOf(id) > 0;
        }

        public bool CanMoveDown(string id)
        {
            if (IsFixed(id)) return false;
            var index = IndexOf(id);
            return index >= 0 && index < _catalog.UserCategories().Count - 1;
        }

        public void MoveUp(string id)
        {
            RequireUser(id, "reordered");
            RequireEditable();
            if (!CanMoveUp(id)) return;
            _catalog.MoveCategory(id, IndexOf(id) - 1);
            Commit();
        }

        public void MoveDown(string id)
        {
            RequireUser(id, "reordered");
            RequireEditable();
            if (!CanMoveDown(id)) return;
            _catalog.MoveCategory(id, IndexOf(id) + 1);
            Commit();
        }

        /// <summary>Puts a save in a user category or in <see cref="Uncategorized"/>; never in recent/auto.</summary>
        public void Move(string key, string targetId)
        {
            if (targetId == Recent || targetId == Auto)
                throw new InvalidOperationException("Saves cannot be moved into " + targetId);
            RequireEditable();
            _catalog.Assign(key, targetId == Uncategorized ? null : targetId);
            Commit();
        }

        /// <summary>Names a save; empty or whitespace clears the name.</summary>
        public void Name(string key, string name)
        {
            RequireEditable();
            _catalog.SetName(key, name);
            Commit();
        }

        /// <summary>Flips the slot list between newest-first and oldest-first (stored in the index).</summary>
        public void ToggleSort()
        {
            RequireEditable();
            _catalog.NewestFirst = !_catalog.NewestFirst;
            Commit();
        }

        private void RequireUser(string id, string what)
        {
            if (IsFixed(id)) throw new InvalidOperationException("The fixed category " + id + " cannot be " + what + ".");
        }

        private void RequireEditable()
        {
            if (!CanEdit) throw new ArgumentException(ReadOnlyErrorKey);
        }

        private bool Exists(string id)
        {
            return _catalog.UserCategories().Any(c => c.Id == id);
        }

        private int IndexOf(string id)
        {
            var categories = _catalog.UserCategories();
            for (var i = 0; i < categories.Count; i++)
                if (categories[i].Id == id) return i;
            return -1;
        }

        private void Commit()
        {
            _persist();
            Raise();
            // Editable a moment ago (RequireEditable ran), so this very write made the session read-only (it was refused,
            // for example for size): say so instead of letting the edit look saved. It stays visible only until restart.
            if (!CanEdit) throw new ArgumentException(ReadOnlyErrorKey);
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
