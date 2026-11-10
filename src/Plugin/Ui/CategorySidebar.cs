using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// The left column of the load/save panel: 最近存檔, 自動存檔, the user's categories
    /// (scrolling when there are many), 未分類, then 「＋ 新增分類」 and 「管理分類」. Rows are copies of the game's own slot
    /// row (Normal/Hover texts, green Focus strip for the selected category); buttons are copies of the 刪除 button.
    /// In manage mode every user category gets ▲ ▼ 改名 刪除 and the fixed rows are dimmed.
    /// All actions go through <see cref="CategoryController"/>; the panel reacts to its Changed event.
    /// </summary>
    internal sealed class CategorySidebar
    {
        // Canvas layout of the column (left edge at 307,262; the category list starts 76 below that).
        private const float ColumnX = 307f;
        private const float ListY = 338f;
        private const float RowW = 500f;
        private const float RowH = 48f;
        private const float RowStep = 52f;
        private const int VisibleUserRows = 5;
        private const float ButtonsY = 846f;

        private readonly ManagedPanel _owner;
        private readonly RectTransform _column;
        private readonly RectTransform _viewport;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly Text _groupLabel;
        private readonly Text _hint;
        private readonly CategoryRow _recent;
        private readonly CategoryRow _auto;
        private readonly CategoryRow _none;
        private readonly Button _add;
        private readonly Button _manage;
        private readonly List<CategoryRow> _users = new List<CategoryRow>();

        public CategorySidebar(ManagedPanel owner, Transform parent)
        {
            _owner = owner;
            _column = UiClone.CreateRect("LSM.Sidebar", parent);
            UiClone.PlaceCanvas(_column, ColumnX, ListY, 520f, 560f);

            _recent = CreateRow(_column, CategoryController.Recent);
            UiClone.PlaceTL(_recent.Rect, 0f, 0f, RowW, RowH);
            _auto = CreateRow(_column, CategoryController.Auto);
            UiClone.PlaceTL(_auto.Rect, 0f, RowStep, RowW, RowH);

            _groupLabel = UiClone.Text(owner.Kit.SerifText, _column, "LSM.Sidebar.Group", "");
            _groupLabel.fontSize = 19;
            _groupLabel.color = UiClone.Muted;
            _groupLabel.alignment = TextAnchor.MiddleLeft;
            UiClone.PlaceTL((RectTransform)_groupLabel.transform, 16f, 104f, 300f, 34f);

            _viewport = UiClone.CreateRect("LSM.Sidebar.Viewport", _column);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _content = UiClone.CreateRect("LSM.Sidebar.Content", _viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = _viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 15f;

            _none = CreateRow(_column, CategoryController.Uncategorized);

            _add = UiClone.FramedButton(owner.Kit.DeleteButton, _column, "LSM.Cat.Add", "", 24f, () => owner.BeginCreateCategory());
            _manage = UiClone.FramedButton(owner.Kit.DeleteButton, _column, "LSM.Cat.Manage", "", 24f, () => owner.ToggleManage());

            _hint = UiClone.Text(owner.Kit.SerifText, _column, "LSM.Sidebar.Hint", "");
            _hint.fontSize = 17;
            _hint.color = UiClone.Hex("#9B927F");
            _hint.alignment = TextAnchor.UpperLeft;
            _hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        public Selectable AddButton
        {
            get { return _add; }
        }

        public Selectable ManageButton
        {
            get { return _manage; }
        }

        /// <summary>Redraws names, counts, the selection, manage mode and the localized labels.</summary>
        public void Refresh(SidebarState state)
        {
            var controller = _owner.Controller;
            var manage = _owner.Managing;

            _recent.Show(_owner.S("Category.Recent"), state.RecentCount.ToString(), controller.Selected == CategoryController.Recent);
            _auto.Show(_owner.S("Category.Auto"), state.AutoCount.ToString(), controller.Selected == CategoryController.Auto);
            _none.Show(_owner.S("Category.Uncategorized"), state.UncategorizedCount.ToString(), controller.Selected == CategoryController.Uncategorized);
            _groupLabel.text = Spaced(_owner.S("Category.Group.Mine"));

            // The save panel lists no recent/auto rows on the right, so those two entries cannot be chosen there.
            var fixedUsable = !manage;
            _recent.SetUsable(fixedUsable && _owner.IsLoad, manage);
            _auto.SetUsable(fixedUsable && _owner.IsLoad, manage);
            _none.SetUsable(fixedUsable, manage);

            var categories = controller.Catalog.UserCategories();
            while (_users.Count < categories.Count) _users.Add(CreateRow(_content, null));
            for (var i = 0; i < _users.Count; i++)
            {
                var row = _users[i];
                if (i >= categories.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }
                var category = categories[i];
                row.Id = category.Id;
                row.Root.name = "LSM.Cat." + category.Id;
                row.Root.SetActive(true);
                UiClone.PlaceTL(row.Rect, 0f, i * RowStep, RowW, RowH);
                row.Show(DisplayText.Plain(category.Name), category.Count.ToString(), controller.Selected == category.Id);
                row.SetUsable(true, false);
                row.SetManage(manage, _owner, controller.CanMoveUp(category.Id), controller.CanMoveDown(category.Id));
            }
            _content.sizeDelta = new Vector2(0f, Math.Max(categories.Count * RowStep - (RowStep - RowH), 0f));

            var visible = Math.Min(categories.Count, VisibleUserRows);
            var listHeight = visible == 0 ? 0f : visible * RowStep - (RowStep - RowH);
            UiClone.PlaceTL(_viewport, 0f, 144f, RowW + 20f, listHeight);
            _viewport.gameObject.SetActive(categories.Count > 0);
            var noneY = 144f + (visible == 0 ? 0f : listHeight + 12f);
            UiClone.PlaceTL(_none.Rect, 0f, noneY, RowW, RowH);

            // Bottom buttons sit at a fixed height; the manage hint just above them.
            var buttonsTop = ButtonsY - ListY;
            UiClone.SetTexts(_add.gameObject, _owner.S("Category.Add"));
            UiClone.SizeFramedButton(_add, 4f, buttonsTop, 176f, 50f, 24f);
            UiClone.SetTexts(_manage.gameObject, _owner.S(manage ? "Category.Manage.Done" : "Category.Manage"));
            UiClone.SizeFramedButton(_manage, 4f + 176f + 12f, buttonsTop, manage ? 120f : 150f, 50f, 24f);
            _hint.gameObject.SetActive(manage);
            _hint.text = _owner.S("Category.Manage.Hint");
            UiClone.PlaceTL((RectTransform)_hint.transform, 8f, buttonsTop - 44f, 500f, 40f);
        }

        /// <summary>The row button of the selected category (where ← from the right column goes).</summary>
        public Selectable SelectedButton
        {
            get
            {
                var selected = _owner.Controller.Selected;
                var row = AllRows().FirstOrDefault(r => r.Id == selected && r.Root.activeInHierarchy);
                return row == null ? null : row.Button;
            }
        }

        /// <summary>The id of the category whose row button is <paramref name="selected"/>, or null.</summary>
        public string IdOf(GameObject selected)
        {
            if (selected == null) return null;
            var row = AllRows().FirstOrDefault(r => r.Button != null && r.Button.gameObject == selected && r.Root.activeInHierarchy);
            return row == null ? null : row.Id;
        }

        public Selectable ButtonFor(string id)
        {
            var row = AllRows().FirstOrDefault(r => r.Id == id && r.Root.activeInHierarchy);
            return row == null ? null : row.Button;
        }

        /// <summary>Wires keyboard/gamepad navigation: categories up/down, then 新增/管理; → goes to <paramref name="right"/>.</summary>
        public void ApplyNavigation(Selectable right)
        {
            var manage = _owner.Managing;
            var column = new List<Selectable>();
            if (!manage)
            {
                column.AddRange(UiClone.Usable(new Selectable[] { _recent.Button, _auto.Button }));
                column.AddRange(UiClone.Usable(_users.Where(r => r.Root.activeSelf).Select(r => (Selectable)r.Button)));
                column.AddRange(UiClone.Usable(new Selectable[] { _none.Button }));
            }
            else
            {
                // Manage mode: one stop per user row (its first usable button), then the bottom buttons.
                foreach (var row in _users.Where(r => r.Root.activeSelf))
                {
                    var first = UiClone.Usable(row.ManageButtons).FirstOrDefault();
                    if (first != null) column.Add(first);
                }
            }
            column.AddRange(UiClone.Usable(new Selectable[] { _add, _manage }));

            for (var i = 0; i < column.Count; i++)
            {
                var up = i > 0 ? column[i - 1] : null;
                var down = i < column.Count - 1 ? column[i + 1] : null;
                var isBottom = column[i] == _add || column[i] == _manage;
                Selectable left = null, rightTarget = manage ? null : right;
                if (column[i] == _add) rightTarget = _manage;
                if (column[i] == _manage) { left = _add; rightTarget = manage ? null : right; }
                if (isBottom && column[i] == _add) left = null;
                UiClone.SetNavigation(column[i], up, down, left, rightTarget);
            }

            if (!manage) return;
            foreach (var row in _users.Where(r => r.Root.activeSelf))
            {
                var buttons = UiClone.Usable(row.ManageButtons);
                var stop = buttons.FirstOrDefault();
                var index = column.IndexOf(stop);
                var up = index > 0 ? column[index - 1] : null;
                var down = index >= 0 && index < column.Count - 1 ? column[index + 1] : null;
                for (var i = 0; i < buttons.Count; i++)
                    UiClone.SetNavigation(buttons[i], up, down, i > 0 ? buttons[i - 1] : null, i < buttons.Count - 1 ? buttons[i + 1] : null);
            }
        }

        /// <summary>Scrolls the user-category list so the given category is visible.</summary>
        public void Reveal(string id)
        {
            var index = _users.FindIndex(r => r.Id == id && r.Root.activeSelf);
            if (index < 0) return;
            var top = index * RowStep;
            var bottom = top + RowH;
            var viewHeight = _viewport.rect.height;
            var offset = _content.anchoredPosition.y;
            if (top < offset) offset = top;
            else if (bottom > offset + viewHeight) offset = bottom - viewHeight;
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Max(0f, offset));
        }

        private IEnumerable<CategoryRow> AllRows()
        {
            yield return _recent;
            yield return _auto;
            foreach (var row in _users) yield return row;
            yield return _none;
        }

        private CategoryRow CreateRow(Transform parent, string id)
        {
            var row = CategoryRow.Create(_owner.Kit, parent, id);
            row.Button.onClick.AddListener(() => _owner.OnCategoryClicked(row.Id));
            row.Relay.Selected = () => _owner.OnCategoryFocused(row.Id);
            return row;
        }

        // "我的分類" is drawn with wide letter spacing.
        private static string Spaced(string text)
        {
            return string.Join(" ", text.Select(c => c.ToString()).ToArray());
        }
    }

    /// <summary>Counts shown beside the fixed categories.</summary>
    internal struct SidebarState
    {
        public int RecentCount;
        public int AutoCount;
        public int UncategorizedCount;
    }

    /// <summary>One category row: a copy of the game's slot row resized to 500x48, plus manage-mode buttons.</summary>
    internal sealed class CategoryRow
    {
        public GameObject Root;
        public RectTransform Rect;
        public Button Button;
        public SelectRelay Relay;
        public string Id;
        private Text[] _names;
        private Text _count;
        private GameObject _focus;
        private CanvasGroup _group;
        private Button _up, _down, _rename, _delete;

        public IEnumerable<Selectable> ManageButtons
        {
            get { return new Selectable[] { _up, _down, _rename, _delete }; }
        }

        /// <summary>A row in the category style (also used for the move dialog's choices).</summary>
        public static CategoryRow Create(UiKit kit, Transform parent, string id)
        {
            var row = new CategoryRow { Id = id };
            row.Root = UiClone.Instantiate(kit.RowTemplate, parent, "LSM.Cat." + (id ?? "new"));
            row.Root.SetActive(true);
            row.Rect = (RectTransform)row.Root.transform;
            row._group = row.Root.AddComponent<CanvasGroup>();

            var delete = row.Rect.Find("Delete");
            if (delete != null) Object.DestroyImmediate(delete.gameObject);
            var slot = (RectTransform)row.Rect.Find("Slot");
            UiClone.Fill(slot);
            row.Button = slot.GetComponent<Button>();
            row.Relay = slot.gameObject.AddComponent<SelectRelay>();

            var names = new List<Text>();
            foreach (var stateName in new[] { "Normal", "Hover", "Focus" })
            {
                var state = slot.Find(stateName) as RectTransform;
                if (state == null) continue;
                UiClone.Fill(state);
                var title = state.Find("Title");
                if (title != null)
                {
                    var text = title.GetComponent<Text>();
                    UiClone.PlaceRow((RectTransform)title, 16f, 2f, 140f, 44f);
                    text.alignment = TextAnchor.MiddleLeft;
                    text.resizeTextForBestFit = true;
                    text.resizeTextMaxSize = 30;
                    text.resizeTextMinSize = 16;
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    names.Add(text);
                }
                var time = state.Find("Time");
                if (time != null) time.gameObject.SetActive(false);
                var line = state.Find("Line") as RectTransform;
                if (line != null) UiClone.PlaceRow(line, 0f, 40f, 20f, 10f);
            }
            row._names = names.ToArray();
            row._focus = slot.Find("Focus") != null ? slot.Find("Focus").gameObject : null;
            var star = slot.Find("NewGamePlus");
            if (star != null) star.gameObject.SetActive(false);

            var header = slot.Find("Header");
            if (header != null)
            {
                row._count = header.GetComponent<Text>();
                UiClone.PlaceTR((RectTransform)header, 16f, 4f, 160f, 40f);
                row._count.fontSize = 21;
                row._count.alignment = TextAnchor.MiddleRight;
                row._count.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            return row;
        }

        private static void Dim(Button button, bool dim)
        {
            var group = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dim ? 0.35f : 1f;
        }

        public void Show(string name, string count, bool selected)
        {
            foreach (var text in _names) text.text = name;
            if (_count != null) _count.text = count;
            if (_focus != null) _focus.SetActive(selected);
        }

        /// <summary>Usable rows can be chosen; in manage mode the fixed rows are dimmed.</summary>
        public void SetUsable(bool usable, bool dim)
        {
            Button.interactable = usable;
            _group.alpha = dim ? 0.35f : 1f;
        }

        public void SetCountColor(Color color)
        {
            if (_count != null) _count.color = color;
        }

        public void SetManage(bool manage, ManagedPanel owner, bool canUp, bool canDown)
        {
            if (manage && _up == null)
            {
                _up = UiClone.FramedButton(owner.Kit.DeleteButton, Rect, "LSM.Cat.Up", "▲", 18f, () => owner.MoveCategoryUp(Id));
                _down = UiClone.FramedButton(owner.Kit.DeleteButton, Rect, "LSM.Cat.Down", "▼", 18f, () => owner.MoveCategoryDown(Id));
                _rename = UiClone.FramedButton(owner.Kit.DeleteButton, Rect, "LSM.Cat.Rename", "", 21f, () => owner.BeginRenameCategory(Id));
                _delete = UiClone.FramedButton(owner.Kit.DeleteButton, Rect, "LSM.Cat.Delete", "", 21f, () => owner.BeginDeleteCategory(Id));
            }
            if (_up == null) return;

            foreach (var b in new[] { _up, _down, _rename, _delete }) b.gameObject.SetActive(manage);
            if (!manage)
            {
                foreach (var text in _names)
                {
                    UiClone.PlaceRow((RectTransform)text.transform, 16f, 2f, 140f, 44f);
                    text.resizeTextMaxSize = 30;
                }
                if (_count != null) _count.gameObject.SetActive(true);
                Button.interactable = true;
                return;
            }

            _up.name = "LSM.Cat.Up." + Id;
            _down.name = "LSM.Cat.Down." + Id;
            _rename.name = "LSM.Cat.Rename." + Id;
            _delete.name = "LSM.Cat.Delete." + Id;
            UiClone.SetTexts(_rename.gameObject, owner.S("Category.Rename"));
            UiClone.SetTexts(_delete.gameObject, owner.S("Category.Delete"));
            UiClone.SizeFramedButton(_up, 4f, 4f, 42f, 40f, 18f);
            UiClone.SizeFramedButton(_down, 50f, 4f, 42f, 40f, 18f);
            UiClone.SizeFramedButton(_rename, 330f, 3f, 80f, 42f, 21f);
            UiClone.SizeFramedButton(_delete, 416f, 3f, 80f, 42f, 21f);
            _up.interactable = canUp;
            _down.interactable = canDown;
            Dim(_up, !canUp);
            Dim(_down, !canDown);
            foreach (var text in _names)
            {
                UiClone.PlaceTL((RectTransform)text.transform, 100f, 2f, 226f, 44f);
                text.resizeTextMaxSize = 27;
            }
            if (_count != null) _count.gameObject.SetActive(false);
            // The row itself is not a stop in manage mode; its buttons are.
            Button.interactable = false;
        }
    }

    /// <summary>
    /// Tells the panel when a category row gains keyboard/gamepad focus (↑↓ switches the list like tabs). Focus that
    /// comes from the mouse hovering the row (the game's buttons select themselves on hover) is ignored: a click does that.
    /// </summary>
    internal sealed class SelectRelay : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Selected;
        private bool _hovered;
        private bool _pending;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
        }

        // The game's hover handler (on the same object) may select the button before OnPointerEnter here runs, so
        // the decision waits for the end of the frame.
        public void OnSelect(BaseEventData eventData)
        {
            // Selections the mod makes itself (restoring focus after a dialog, after a refresh) are not navigation.
            _pending = !(eventData is PointerEventData) && Time.frameCount != ProgrammaticFrame;
        }

        /// <summary>The frame in which the mod last moved the selection itself.</summary>
        public static int ProgrammaticFrame = -1;

        /// <summary>Selects <paramref name="target"/> without it counting as the player's navigation.</summary>
        public static void SelectQuietly(GameObject target)
        {
            var events = EventSystem.current;
            if (events == null) return;
            ProgrammaticFrame = Time.frameCount;
            events.SetSelectedGameObject(target);
        }

        private void LateUpdate()
        {
            if (!_pending) return;
            _pending = false;
            if (_hovered) return;
            var handler = Selected;
            if (handler != null) handler();
        }
    }
}
