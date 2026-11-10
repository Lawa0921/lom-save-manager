using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// The save manager's layer on one game load or save panel (attached to the panel's GameObject): it rearranges the
    /// panel into a two-column layout (category sidebar left, the category's saves right), owns the
    /// panel's <see cref="CategoryController"/>, and runs the naming / moving / category dialogs. The game's own
    /// methods still load, save and delete; this only decides which rows the panel shows and decorates them.
    /// Built lazily the first time the panel is customized; if building fails the panel stays the game's own.
    /// </summary>
    internal sealed class ManagedPanel : MonoBehaviour
    {
        // The load panel keeps the last category the player looked at (first time: 最近存檔, like the game).
        private static string _lastLoadCategory = CategoryController.Recent;
        private static readonly HashSet<int> FailedPanels = new HashSet<int>();
        private static readonly List<ManagedPanel> Live = new List<ManagedPanel>();

        // Right column (canvas units, left edge at 905,262).
        private const float RightX = 905f;
        private const float RightTitleX = 919f;
        private const float RightTitleY = 262f;
        private const float ListY = 368f;
        private const float SortW = 236f;
        private const float RowStep = 115f;

        private LoadGamePanel _load;
        private SaveGamePanel _save;
        private Transform _container;
        private CategorySidebar _sidebar;
        private Text _leftTitle;
        private Text _rightTitle;
        private Text _rightCount;
        private Button _sort;
        private Text _emptyTitle;
        private Text _emptyHint;
        private Text _fullTitle;
        private Text _fullHint;
        private RectTransform _scrollView;
        private GameObject _recentBlock;
        private GameObject _autoBlock;
        private ConfirmDialog _confirm;
        private NameDialog _name;
        private MoveDialog _move;
        private Toast _toast;
        private readonly List<CanvasGroup> _rightGroups = new List<CanvasGroup>();

        private IReadOnlyList<string> _keys = new string[0];
        private readonly List<RowParts> _rows = new List<RowParts>();
        private ISet<string> _existing = new HashSet<string>();
        private ISet<string> _broken = new HashSet<string>();
        private string _newRowKey;
        private bool _refreshing;

        // Up/down in the sidebar shows a category only once the cursor rests on it (each switch rebuilds the list and
        // reads every save in it); a click or the submit key switches at once. See CategoryFocusDwell.
        private readonly CategoryFocusDwell _dwell = new CategoryFocusDwell(CategoryFocusDwell.DefaultSeconds);

        /// <summary>Milliseconds the last <see cref="Refresh"/> took (diagnostics only).</summary>
        public static double LastRefreshMs { get; private set; }

        public UiKit Kit { get; private set; }

        public CategoryController Controller { get; private set; }

        public bool IsLoad
        {
            get { return _load != null; }
        }

        public bool Managing { get; private set; }

        /// <summary>The selected category's display name (for 「＋ 存到「X」」 and the right column title).</summary>
        public string CategoryDisplayName { get; private set; }

        public string DeleteLabel
        {
            get { return Native("System/DeleteText") ?? S("Category.Delete"); }
        }

        public static IEnumerable<ManagedPanel> All
        {
            get { return Live.Where(p => p != null); }
        }

        // ---- creation -------------------------------------------------------------------------------------------

        public static ManagedPanel For(LoadGamePanel panel)
        {
            return For(panel, p => p._load = panel);
        }

        public static ManagedPanel For(SaveGamePanel panel)
        {
            return For(panel, p => p._save = panel);
        }

        private static ManagedPanel For(MonoBehaviour panel, Action<ManagedPanel> bind)
        {
            if (panel == null) return null;
            var existing = panel.GetComponent<ManagedPanel>();
            if (existing != null) return existing;
            if (FailedPanels.Contains(panel.GetInstanceID())) return null;

            ManagedPanel created = null;
            try
            {
                created = panel.gameObject.AddComponent<ManagedPanel>();
                bind(created);
                created.Build(panel);
                return created;
            }
            catch (Exception ex)
            {
                FailedPanels.Add(panel.GetInstanceID());
                SaveManagerPlugin.LogWarn("The save manager could not lay out " + panel.name + "; it keeps the game's own panel ("
                    + ex.GetType().Name + ": " + ex.Message + ")");
                if (created != null) Object.Destroy(created);
                return null;
            }
        }

        private void Build(MonoBehaviour panel)
        {
            var runtime = SaveManagerRuntime.Current;
            Kit = UiKit.For(panel);
            Controller = new CategoryController(runtime.Catalog, () => runtime.Persist(), () => runtime.CanPersist);
            // The save panel has no recent/auto list; it opens on the played save's category (see PanelHooks).
            Controller.Select(IsLoad ? _lastLoadCategory : CategoryController.Uncategorized);
            Controller.Changed += OnControllerChanged;

            _container = panel.transform.Find("Container");
            if (_container == null) throw new InvalidOperationException("the panel has no Container");
            var scroll = _container.Find("Scroll View") as RectTransform;
            if (scroll == null) throw new InvalidOperationException("the panel has no Scroll View");
            var header = _container.Find("Header");
            if (header == null || header.GetComponent<Text>() == null) throw new InvalidOperationException("the panel has no Header");
            Text leftTitle, rightTitle;
            if (IsLoad)
            {
                leftTitle = Need<Text>("Recent");
                rightTitle = Need<Text>("Manual");
                _recentBlock = Need<Transform>("RecentSave").gameObject;
                _autoBlock = Need<Transform>("AutoSave").gameObject;
            }
            else
            {
                leftTitle = UiClone.Text(header.GetComponent<Text>(), _container, "LSM.LeftTitle", "");
                rightTitle = UiClone.Text(header.GetComponent<Text>(), _container, "LSM.RightTitle", "");
            }

            // Everything new is created first; the game's own objects are only moved once that worked.
            var sidebarRoot = UiClone.CreateRect("LSM.Panel", _container);
            sidebarRoot.SetSiblingIndex(Kit.ConfirmTemplate.transform.GetSiblingIndex());
            UiClone.Fill(sidebarRoot);
            _sidebar = new CategorySidebar(this, sidebarRoot);

            _rightCount = UiClone.Text(Kit.SerifText, sidebarRoot, "LSM.RightCount", "");
            _rightCount.fontSize = 22;
            _rightCount.alignment = TextAnchor.MiddleLeft;
            _rightCount.horizontalOverflow = HorizontalWrapMode.Overflow;
            _sort = UiClone.FramedButton(Kit.DeleteButton, sidebarRoot, "LSM.Sort", "", 20f, ToggleSort);
            UiClone.PlaceCanvas((RectTransform)_sort.transform, RightX + 740f - 20f - SortW, RightTitleY + 10f, SortW, 46f);
            UiClone.SizeFrame(_sort, SortW, 46f, 20f);

            _emptyTitle = UiClone.Text(Kit.CalligraphyText, sidebarRoot, "LSM.Empty.Title", "");
            _emptyTitle.fontSize = 32;
            _emptyTitle.resizeTextForBestFit = false;
            _emptyTitle.color = UiClone.Hex("#A39A8A");
            _emptyTitle.alignment = TextAnchor.MiddleLeft;
            _emptyTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiClone.PlaceCanvas((RectTransform)_emptyTitle.transform, RightX + 46f, ListY + 130f, 640f, 48f);
            _emptyHint = UiClone.Text(Kit.SerifText, sidebarRoot, "LSM.Empty.Hint", "");
            _emptyHint.fontSize = 19;
            _emptyHint.color = UiClone.Muted;
            _emptyHint.alignment = TextAnchor.UpperLeft;
            _emptyHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiClone.PlaceCanvas((RectTransform)_emptyHint.transform, RightX + 46f, ListY + 186f, 600f, 70f);

            // Design frame 11: when every slot is taken the save panel's first row is a disabled hint (not a button).
            var fullColor = UiClone.Hex("#7C7365");
            _fullTitle = UiClone.Text(Kit.CalligraphyText, sidebarRoot, "LSM.Full.Title", "");
            _fullTitle.resizeTextForBestFit = false;
            _fullTitle.fontSize = 36;
            _fullTitle.color = fullColor;
            _fullTitle.alignment = TextAnchor.MiddleLeft;
            _fullTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiClone.PlaceCanvas((RectTransform)_fullTitle.transform, RightX + 44f, ListY + 14f, 660f, 50f);
            _fullHint = UiClone.Text(Kit.SerifText, sidebarRoot, "LSM.Full.Hint", "");
            _fullHint.fontSize = 19;
            _fullHint.color = fullColor;
            _fullHint.alignment = TextAnchor.MiddleLeft;
            _fullHint.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiClone.PlaceCanvas((RectTransform)_fullHint.transform, RightX + 46f, ListY + 64f, 640f, 30f);

            _confirm = ConfirmDialog.Create(this);
            _name = NameDialog.Create(this);
            _move = MoveDialog.Create(this);
            _toast = Toast.Create(this, _container);

            // Move the game's own objects into the two-column layout.
            header.gameObject.SetActive(true);
            UiClone.PlaceCanvas((RectTransform)header, 316f, 141.5f, 500f, 100f);
            UiClone.RemoveLocalizers(leftTitle.gameObject);
            UiClone.RemoveLocalizers(rightTitle.gameObject);
            StyleColumnTitle(leftTitle);
            UiClone.PlaceCanvas((RectTransform)leftTitle.transform, 321f, RightTitleY, 500f, 66f);
            _rightTitle = rightTitle;
            StyleColumnTitle(_rightTitle);
            _rightTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            _scrollView = scroll;
            UiClone.PlaceCanvas(_scrollView, RightX, ListY, 730f, 519f);
            if (IsLoad)
            {
                var auto = _container.Find("Auto");
                if (auto != null) auto.gameObject.SetActive(false);
                UiClone.PlaceCanvas((RectTransform)_recentBlock.transform, RightX + 15f, ListY, 600f, 500f);
                UiClone.PlaceCanvas((RectTransform)_autoBlock.transform, RightX + 15f, ListY, 600f, 500f);
            }
            else
            {
                foreach (var name in new[] { "Logo", "Line" })
                {
                    var t = _container.Find(name);
                    if (t != null) t.gameObject.SetActive(false);
                }
            }
            _leftTitle = leftTitle;

            foreach (var graphic in new Component[] { _scrollView, _rightTitle, _rightCount, _sort, _emptyTitle, _emptyHint, _fullTitle, _fullHint })
                _rightGroups.Add(graphic.GetComponent<CanvasGroup>() ?? graphic.gameObject.AddComponent<CanvasGroup>());

            LocalizationManager.OnLanguageChanged += OnLanguageChanged;
            Live.Add(this);
        }

        private T Need<T>(string name) where T : Component
        {
            var t = _container.Find(name);
            var component = t == null ? null : t.GetComponent<T>();
            if (component == null) throw new InvalidOperationException("the panel has no " + name);
            return component;
        }

        private static void StyleColumnTitle(Text text)
        {
            text.gameObject.SetActive(true);
            text.fontSize = 40;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = UiClone.White;
            text.resizeTextForBestFit = false;
        }

        private void OnDestroy()
        {
            LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
            Live.Remove(this);
        }

        private void OnDisable()
        {
            _dwell.Cancel();
            // The panel closed: close any dialog (it resumes the paused hotkeys) and leave manage mode.
            foreach (var dialog in Dialogs()) if (dialog != null) dialog.Close(false);
            Managing = false;
            // A new game or save made outside this panel (e.g. the title screen's own new game) is not filed into the
            // category this panel last showed. The load panel's own new-game row runs while the panel is still open.
            SaveManagerRuntime.Current.Pending.Clear(this);
        }

        // ---- strings --------------------------------------------------------------------------------------------

        public static string Language()
        {
            try
            {
                var manager = LocalizationManager.Instance;
                return manager == null || manager.LocaleResolver == null ? null : manager.LocaleResolver.GetCurrentLang();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string S(string key)
        {
            return Strings.Get(key, Language());
        }

        private static string Native(string key)
        {
            try
            {
                var manager = LocalizationManager.Instance;
                var text = manager == null ? null : manager.LocaleResolver.GetString(key);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnLanguageChanged()
        {
            if (!isActiveAndEnabled) return;
            SafeRefresh("Refreshing after a language change");
            var dialog = OpenDialog;
            if (dialog != null) dialog.Relocalize();
        }

        // ---- refresh --------------------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the right column for the selected category (the game's rows, via <see cref="SlotListBuilder"/>),
        /// decorates the rows, and redraws the sidebar, titles, counts and navigation.
        /// </summary>
        public void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var runtime = SaveManagerRuntime.Current;
                Controller.Validate();
                if (IsLoad) _lastLoadCategory = Controller.Selected;
                var listing = runtime.Session.ListKeys(Controller.SelectedCatalogId);
                _existing = listing.Existing;

                IReadOnlyList<string> keys = new string[0];
                if (Controller.ShowsSlotList)
                    keys = IsLoad
                        ? SlotListPlan.ForLoad(listing.Keys, listing.Existing, listing.NewSlotKey)
                        : SlotListPlan.ForSave(listing.Keys, listing.NewSlotKey);
                _newRowKey = keys.Count > 0 && !listing.Existing.Contains(keys[0]) ? keys[0] : null;

                CategoryDisplayName = DisplayName(Controller.Selected);
                IReadOnlyList<string> failed;
                _rows.Clear();
                if (IsLoad)
                {
                    failed = SlotListBuilder.Rebuild(_load, keys);
                    _rows.AddRange(SlotListBuilder.Rows(_load).Select(RowParts.Of));
                    SlotListBuilder.SetupSidebar(_load);
                }
                else
                {
                    failed = SlotListBuilder.Rebuild(_save, keys);
                    _rows.AddRange(SlotListBuilder.Rows(_save).Select(RowParts.Of));
                }
                _keys = keys;

                var broken = new HashSet<string>(failed);
                _broken = broken;
                for (var i = 0; i < _rows.Count && i < keys.Count; i++)
                {
                    var kind = keys[i] == _newRowKey ? (IsLoad ? RowKind.NewGame : RowKind.NewSave)
                        : broken.Contains(keys[i]) ? RowKind.Broken : RowKind.Save;
                    SlotDecorator.Decorate(this, _rows[i], keys[i], kind);
                }

                if (IsLoad)
                {
                    _scrollView.gameObject.SetActive(Controller.ShowsSlotList);
                    _recentBlock.SetActive(Controller.Selected == CategoryController.Recent);
                    _autoBlock.SetActive(Controller.Selected == CategoryController.Auto);
                }

                _leftTitle.text = S("Category.Header");
                var saveCount = listing.Keys.Count;
                _rightTitle.text = DisplayText.Plain(CategoryDisplayName);
                var titleWidth = Mathf.Min(_rightTitle.preferredWidth, 420f);
                UiClone.PlaceCanvas((RectTransform)_rightTitle.transform, RightTitleX, RightTitleY, 420f, 66f);
                _rightCount.gameObject.SetActive(Controller.ShowsSlotList);
                _rightCount.text = string.Format(S("Category.Count"), saveCount);
                UiClone.PlaceCanvas((RectTransform)_rightCount.transform, RightTitleX + titleWidth + 18f, RightTitleY + 14f, 160f, 44f);
                // An empty category has nothing to sort.
                _sort.gameObject.SetActive(Controller.ShowsSlotList && saveCount > 0);
                UiClone.SetTexts(_sort.gameObject, S(Controller.NewestFirst ? "Sort.NewestFirst" : "Sort.OldestFirst"));

                // All 9999 slots taken: the hint takes the first row's place and the saves start one row lower.
                var full = SlotListPlan.ShowsFullHint(!IsLoad, Controller.ShowsSlotList, listing.NewSlotKey);
                _fullTitle.gameObject.SetActive(full);
                _fullHint.gameObject.SetActive(full);
                _fullTitle.text = SlotListPlan.FullHintTitle(Language());
                _fullHint.text = S("Slot.Full.Hint");
                UiClone.PlaceCanvas(_scrollView, RightX, full ? ListY + RowStep : ListY, 730f, full ? 519f - RowStep : 519f);

                var empty = Controller.ShowsSlotList && saveCount == 0;
                var emptyY = full ? ListY + RowStep + 16f : ListY + 130f;
                UiClone.PlaceCanvas((RectTransform)_emptyTitle.transform, RightX + 46f, emptyY, 640f, 48f);
                UiClone.PlaceCanvas((RectTransform)_emptyHint.transform, RightX + 46f, emptyY + 56f, 600f, 70f);
                _emptyTitle.gameObject.SetActive(empty);
                _emptyHint.gameObject.SetActive(empty);
                _emptyTitle.text = S("Category.Empty");
                _emptyHint.text = S(IsLoad ? "Category.Empty.HintLoad" : "Category.Empty.HintSave");

                foreach (var group in _rightGroups)
                {
                    group.alpha = Managing ? 0.32f : 1f;
                    group.interactable = !Managing;
                }

                _sidebar.Refresh(new SidebarState
                {
                    RecentCount = RecentCount(listing.Existing),
                    AutoCount = AutoCount(runtime.UserFolder),
                    UncategorizedCount = Controller.Catalog.UncategorizedCount(listing.Existing),
                });
                if (!IsFixedSelection) _sidebar.Reveal(Controller.Selected);
                ApplyNavigation();

                if (isActiveAndEnabled) runtime.Pending.Set(this, Controller.PendingCategoryId);
            }
            finally
            {
                _refreshing = false;
                LastRefreshMs = watch.Elapsed.TotalMilliseconds;
            }
        }

        private bool IsFixedSelection
        {
            get { return CategoryController.IsFixed(Controller.Selected); }
        }

        private string DisplayName(string id)
        {
            if (id == CategoryController.Recent) return S("Category.Recent");
            if (id == CategoryController.Auto) return S("Category.Auto");
            if (id == CategoryController.Uncategorized) return S("Category.Uncategorized");
            var category = Controller.Catalog.UserCategories().FirstOrDefault(c => c.Id == id);
            return category == null ? S("Category.Uncategorized") : category.Name;
        }

        private static int RecentCount(ISet<string> existing)
        {
            try
            {
                var system = GameAccess.FindSaveSystem() as SaveSystem;
                var current = system == null ? null : system.CurrentSlot;
                return current != null && existing.Contains(current) ? 1 : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int AutoCount(string folder)
        {
            var count = 0;
            foreach (var slot in new[] { "auto", "auto_free", "auto_battle" })
                if (File.Exists(Path.Combine(folder, "Save_" + slot + ".dat"))) count++;
            return count;
        }

        private void SafeRefresh(string what)
        {
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn(what + " failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        private void OnControllerChanged()
        {
            SafeRefresh("Refreshing the panel");
        }

        // ---- navigation & selection -----------------------------------------------------------------------------

        /// <summary>The right column's stops, top to bottom (rows' main buttons, or the game's recent/auto rows).</summary>
        private List<Selectable> RightStops()
        {
            var stops = new List<Selectable>();
            if (Controller.ShowsSlotList)
            {
                foreach (var row in _rows)
                {
                    if (row.Slot != null && row.Slot.IsInteractable()) stops.Add(row.Slot);
                    else
                    {
                        var first = SlotDecorator.RowButtons(row).FirstOrDefault();
                        if (first != null) stops.Add(first);
                    }
                }
            }
            else if (IsLoad)
            {
                var block = Controller.Selected == CategoryController.Recent ? _recentBlock : _autoBlock;
                foreach (var button in block.GetComponentsInChildren<Button>(false))
                    if (button.name == "Slot" && button.IsInteractable()) stops.Add(button);
            }
            return stops;
        }

        public void ApplyNavigation()
        {
            var left = _sidebar.SelectedButton;
            var stops = RightStops();
            var sort = _sort.gameObject.activeInHierarchy && !Managing ? _sort : null;

            if (Controller.ShowsSlotList)
            {
                for (var i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    var buttons = SlotDecorator.RowButtons(row);
                    var main = row.Slot != null && row.Slot.IsInteractable() ? (Selectable)row.Slot : null;
                    var stopIndex = stops.IndexOf(main ?? buttons.FirstOrDefault());
                    var up = stopIndex > 0 ? stops[stopIndex - 1] : sort;
                    var down = stopIndex >= 0 && stopIndex < stops.Count - 1 ? stops[stopIndex + 1] : null;
                    if (main != null) UiClone.SetNavigation(main, up, down, left, buttons.FirstOrDefault());
                    for (var j = 0; j < buttons.Count; j++)
                        UiClone.SetNavigation(buttons[j], up, down, j > 0 ? buttons[j - 1] : main ?? left, j < buttons.Count - 1 ? buttons[j + 1] : null);
                }
            }
            else
            {
                for (var i = 0; i < stops.Count; i++)
                {
                    var delete = stops[i].transform.parent.Find("Delete");
                    var deleteButton = delete != null && delete.gameObject.activeInHierarchy ? delete.GetComponent<Button>() : null;
                    UiClone.SetNavigation(stops[i], i > 0 ? stops[i - 1] : null, i < stops.Count - 1 ? stops[i + 1] : null, left, deleteButton);
                    if (deleteButton != null)
                        UiClone.SetNavigation(deleteButton, i > 0 ? stops[i - 1] : null, i < stops.Count - 1 ? stops[i + 1] : null, stops[i], null);
                }
            }
            if (sort != null) UiClone.SetNavigation(sort, null, stops.FirstOrDefault(), left, null);
            // Right from the sidebar enters at the row the panel opens on (never the load panel's new-game row while a
            // save is listed); when nothing else is listed it is the only row, so right still reaches it.
            _sidebar.ApplyNavigation(Managing ? null : InitialStop() ?? stops.FirstOrDefault() ?? sort);
        }

        /// <summary>
        /// The right-column stop the cursor starts on (keyboard/gamepad: the cursor starts on the first row of the right column), except that on the
        /// load panel it is the first real save, not 「＋ 開始新的旅程」 (one click there starts a new game and loses
        /// unsaved progress; it stays one up-press away). Null when the list has no such row. See <see cref="FocusRules"/>.
        /// </summary>
        private Selectable InitialStop()
        {
            if (!Controller.ShowsSlotList) return RightStops().FirstOrDefault();
            var index = FocusRules.InitialRow(IsLoad, _keys, _existing, _broken);
            return index >= 0 && index < _rows.Count ? RowStop(_rows[index]) : null;
        }

        private static Selectable RowStop(RowParts row)
        {
            return row.Slot != null && row.Slot.IsInteractable() ? (Selectable)row.Slot : SlotDecorator.RowButtons(row).FirstOrDefault();
        }

        /// <summary>Where the cursor goes when the panel opens: <see cref="InitialStop"/>, else the sidebar's selected category.</summary>
        public void SelectInitial()
        {
            Select(InitialStop() ?? _sidebar.SelectedButton);
        }

        private static void Select(Selectable target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            SelectRelay.SelectQuietly(target.gameObject);
        }

        /// <summary>Selects the row showing <paramref name="key"/> and scrolls it into view (after a save, rename, …).</summary>
        public void SelectKey(string key)
        {
            var index = IndexOfKey(key);
            if (index < 0)
            {
                SelectInitial();
                return;
            }
            SelectRowAt(index);
        }

        /// <summary>
        /// After a row left the list (deleted, moved away): selects the row that took its place, or the nearest one
        /// above; on the load panel never the new-game row (then the sidebar's category). See <see cref="FocusRules"/>.
        /// </summary>
        public void SelectAfterRemoval(int oldIndex)
        {
            var index = FocusRules.RowAfterRemoval(IsLoad, _keys, _existing, _broken, oldIndex);
            if (index < 0 || index >= _rows.Count)
            {
                Select(_sidebar.SelectedButton);
                return;
            }
            SelectRowAt(index);
        }

        public int IndexOfKey(string key)
        {
            for (var i = 0; i < _keys.Count; i++) if (_keys[i] == key) return i;
            return -1;
        }

        private void SelectRowAt(int index)
        {
            var row = _rows[index];
            Select(RowStop(row));
            Reveal(row.Root.transform as RectTransform);
        }

        private void Reveal(RectTransform row)
        {
            var scroll = _scrollView.GetComponent<ScrollRect>();
            if (scroll == null || row == null || scroll.content == null) return;
            Canvas.ForceUpdateCanvases();
            var content = scroll.content;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)_scrollView;
            var top = -row.anchoredPosition.y - row.rect.height * (1f - row.pivot.y);
            var bottom = top + row.rect.height;
            var offset = content.anchoredPosition.y;
            var view = viewport.rect.height;
            if (top < offset) offset = top;
            else if (bottom > offset + view) offset = bottom - view;
            var max = Mathf.Max(0f, content.rect.height - view);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(offset, 0f, max));
        }

        // ---- sidebar actions ------------------------------------------------------------------------------------

        /// <summary>A click or the submit key on a category: shown at once.</summary>
        public void OnCategoryClicked(string id)
        {
            _dwell.Cancel();
            if (Managing || id == null) return;
            Run(() => Controller.Select(id));
        }

        /// <summary>
        /// The player's up/down put the cursor on a category: it is shown once the cursor has rested there for
        /// <see cref="CategoryFocusDwell.DefaultSeconds"/> (see <see cref="Update"/>), so passing over categories
        /// does not rebuild each one.
        /// </summary>
        public void OnCategoryFocused(string id)
        {
            if (Managing || id == null || id == Controller.Selected)
            {
                _dwell.Cancel();
                return;
            }
            _dwell.Focused(id, Time.unscaledTime);
        }

        private void Update()
        {
            if (!_dwell.Pending) return;
            if (Managing || OpenDialog != null)
            {
                _dwell.Cancel();
                return;
            }
            var events = EventSystem.current;
            var focused = _sidebar.IdOf(events == null ? null : events.currentSelectedGameObject);
            var id = _dwell.Poll(focused, Controller.Selected, Time.unscaledTime);
            if (id != null) Run(() => Controller.Select(id));
        }

        public void ToggleManage()
        {
            _dwell.Cancel();
            if (!Managing && RefusedReadOnly()) return; // leaving manage mode is always allowed
            Managing = !Managing;
            SafeRefresh("Switching manage mode");
            var categories = Controller.Catalog.UserCategories();
            if (Managing && categories.Count > 0) SelectManageButton(categories[0].Id, true);
            else Select(Managing ? _sidebar.ManageButton : _sidebar.SelectedButton ?? _sidebar.ManageButton);
        }

        public void MoveCategoryUp(string id)
        {
            Run(() => Controller.MoveUp(id));
            SelectManageButton(id, true);
        }

        public void MoveCategoryDown(string id)
        {
            Run(() => Controller.MoveDown(id));
            SelectManageButton(id, false);
        }

        private void SelectManageButton(string id, bool preferUp)
        {
            var row = _sidebar.ButtonFor(id);
            if (row == null) return;
            var parent = row.transform.parent;
            var names = preferUp ? new[] { "Up", "Down", "Rename" } : new[] { "Down", "Up", "Rename" };
            foreach (var n in names)
            {
                var t = parent.Find("LSM.Cat." + n + "." + id);
                var b = t == null ? null : t.GetComponent<Button>();
                if (b != null && b.gameObject.activeInHierarchy && b.IsInteractable())
                {
                    Select(b);
                    _sidebar.Reveal(id);
                    return;
                }
            }
        }

        public void BeginCreateCategory()
        {
            if (RefusedReadOnly()) return;
            _name.Show("category_new", "Name.Title.CategoryNew", null, "Name.Placeholder.Category", null, "",
                SaveCatalog.MaxCategoryNameLength, "Name.Cancel", text =>
                {
                    var error = Attempt(() =>
                    {
                        var id = Controller.Create(text);
                        if (!Managing) Controller.Select(id);
                    });
                    if (error == null) Select(_sidebar.SelectedButton);
                    return error;
                }, () => Select(_sidebar.AddButton));
        }

        public void BeginRenameCategory(string id)
        {
            if (RefusedReadOnly()) return;
            var category = Controller.Catalog.UserCategories().FirstOrDefault(c => c.Id == id);
            if (category == null) return;
            _name.Show("category_rename", "Name.Title.CategoryRename", null, "Name.Placeholder.Category", null, category.Name,
                SaveCatalog.MaxCategoryNameLength, "Name.Cancel", text =>
                {
                    var error = Attempt(() => Controller.Rename(id, text));
                    if (error == null) SelectManageButton(id, true);
                    return error;
                }, () => SelectManageButton(id, true));
        }

        public void BeginDeleteCategory(string id)
        {
            if (RefusedReadOnly()) return;
            var category = Controller.Catalog.UserCategories().FirstOrDefault(c => c.Id == id);
            if (category == null) return;
            _confirm.Show("category_delete", () => string.Format(S("Category.ConfirmDelete"), DisplayText.Plain(category.Name), category.Count), () =>
            {
                // Like every other action: a refusal (read-only, or the write was refused) is a toast, and the focus is
                // still put back on a button whether or not the category went away.
                Run(() => Controller.Delete(id));
                var first = Controller.Catalog.UserCategories().FirstOrDefault();
                if (first != null) SelectManageButton(first.Id, true);
                else Select(_sidebar.ManageButton);
            }, true);
        }

        public void ToggleSort()
        {
            if (!RefusedReadOnly()) Run(() => Controller.ToggleSort());
            Select(_sort);
        }

        // ---- save actions ---------------------------------------------------------------------------------------

        /// <summary>Opens the naming dialog for a save; after a save (<paramref name="afterSave"/>) the cancel button is 「略過」.</summary>
        public void BeginNameSave(string key, bool afterSave)
        {
            if (!_existing.Contains(key)) return;
            if (RefusedReadOnly()) return; // after a save too: the save itself is done, only its name cannot be kept
            _name.Show(afterSave ? "save_name_after_save" : "save_name", "Name.Title.Save", Context(key, true), "Name.Placeholder.Save",
                "Name.Hint.Save", Controller.Catalog.NameOf(key) ?? "", SaveCatalog.MaxSlotNameLength,
                afterSave ? "Name.Skip" : "Name.Cancel", text =>
                {
                    var error = Attempt(() => Controller.Name(key, text));
                    if (error == null) SelectKey(key);
                    return error;
                }, () => SelectKey(key));
        }

        public void BeginMoveSave(string key)
        {
            if (!_existing.Contains(key)) return;
            if (RefusedReadOnly()) return;
            var oldIndex = IndexOfKey(key);
            var current = Controller.Catalog.CategoryOf(key) ?? CategoryController.Uncategorized;
            _move.Show(Context(key, false), current, Controller.Catalog.UserCategories(), target =>
            {
                var error = Attempt(() => Controller.Move(key, target));
                if (error != null)
                {
                    ShowToast(S(error));
                    SelectKey(key);
                    return;
                }
                ShowToast(string.Format(S("Toast.Moved"), DisplayText.Plain(DisplayName(target))));
                if (IndexOfKey(key) >= 0) SelectKey(key);
                else SelectAfterRemoval(oldIndex);
            });
        }

        /// <summary>The save panel's 刪除 (the game's save panel has none): the game's own delete, after its own question.</summary>
        public void BeginDeleteSave(string key)
        {
            if (IsLoad || !_existing.Contains(key)) return;
            var oldIndex = IndexOfKey(key);
            _confirm.Show("save_delete", () => Native("System/ConfirmDelete") ?? S("Category.Delete") + "?", () =>
            {
                var system = GameAccess.FindSaveSystem() as SaveSystem;
                if (system == null) return;
                system.DeleteSaveData(key);
                Refresh();
                SelectAfterRemoval(oldIndex);
            }, true);
        }

        /// <summary>
        /// Called after the game wrote <paramref name="key"/> from the save panel (a new save, or an overwrite after the
        /// game's own confirm): the list is fresh, the row is selected and the naming dialog opens (old name prefilled).
        /// </summary>
        public void AfterSave(string key)
        {
            if (key == null || !_existing.Contains(key)) return;
            SelectKey(key);
            BeginNameSave(key, true);
        }

        /// <summary>"傳奇NNN　name" (move) or "傳奇NNN　date　time" (naming), for the dialog's context line.</summary>
        private string Context(string key, bool withTime)
        {
            var index = IndexOfKey(key);
            var row = index >= 0 && index < _rows.Count ? _rows[index] : null;
            var header = row != null && row.Header != null ? row.Header.text : key;
            var gold = "<color=#" + ColorUtility.ToHtmlStringRGB(UiClone.Gold) + ">" + header + "</color>";
            if (!withTime) return gold + "　" + (DisplayText.Plain(Controller.Catalog.NameOf(key)) ?? S("Slot.Unnamed"));
            var state = row == null ? null : row.Root.GetComponent<SlotDecorator.RowState>();
            if (state == null) return gold;
            return gold + "　" + state.NativeTitle + "　" + state.NativeTime;
        }

        // ---- dialogs & errors -----------------------------------------------------------------------------------

        private IEnumerable<DialogBase> Dialogs()
        {
            yield return _confirm;
            yield return _name;
            yield return _move;
        }

        public DialogBase OpenDialog
        {
            get { return Dialogs().FirstOrDefault(d => d != null && d.IsOpen); }
        }


        public void ShowToast(string message)
        {
            if (_toast != null) _toast.Show(message);
        }

        /// <summary>
        /// True (after a toast saying so) when names and categories cannot be saved this session: the edit is not
        /// offered at all, so nothing appears that would vanish at the next start. The controller refuses it as well.
        /// </summary>
        private bool RefusedReadOnly()
        {
            if (Controller.CanEdit) return false;
            ShowToast(S(CategoryController.ReadOnlyErrorKey));
            return true;
        }

        /// <summary>Runs a controller action; a validation error key is returned (null = done), other failures are logged.</summary>
        private string Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (ArgumentException ex)
            {
                return Strings.Keys.Contains(ex.Message) ? ex.Message : SaveCatalog.ErrorKeys.CategoryNotFound;
            }
        }

        /// <summary>Runs an action from a button; a validation error becomes a floating message, anything else a log line.</summary>
        public void Run(Action action)
        {
            try
            {
                var error = Attempt(action);
                if (error != null) ShowToast(S(error));
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Save manager action failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        // ---- diagnostics ----------------------------------------------------------------------------------------

        public Dictionary<string, object> Snapshot()
        {
            var dialog = OpenDialog;
            return new Dictionary<string, object>
            {
                { "panel", IsLoad ? "load" : "save" },
                { "path", PathOf(transform) },
                { "open", isActiveAndEnabled && gameObject.activeInHierarchy },
                { "selected", Controller.Selected },
                { "selected_name", CategoryDisplayName },
                { "pending_category", Controller.PendingCategoryId },
                { "newest_first", Controller.NewestFirst },
                { "managing", Managing },
                { "can_edit", Controller.CanEdit },
                { "keys", _keys.ToList() },
                { "new_row_key", _newRowKey },
                { "category_switch_pending", _dwell.Pending },
                { "dialog", dialog == null ? null : dialog.Kind },
                { "dialog_text", dialog is NameDialog ? ((NameDialog)dialog).Field.text : null },
                { "toast", _toast == null ? null : _toast.Current },
            };
        }

        private static string PathOf(Transform node)
        {
            var parts = new List<string>();
            for (var t = node; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
