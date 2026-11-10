using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LoMSaveManager
{
    /// <summary>
    /// 「移到哪個分類？」: the user categories and 未分類 as category-style rows in a copy of the game's
    /// confirm window; the save's current category is marked 目前所在 and cannot be chosen; 取消 closes.
    /// Choosing a row moves the save at once.
    /// </summary>
    internal sealed class MoveDialog : DialogBase
    {
        private const float OptionStep = 58f;
        private const int VisibleOptions = 6;

        private Text _title;
        private Text _context;
        private RectTransform _viewport;
        private RectTransform _content;
        private readonly List<CategoryRow> _options = new List<CategoryRow>();
        private Action<string> _choose;
        private string _currentId;

        public static MoveDialog Create(ManagedPanel owner)
        {
            return Create<MoveDialog>(owner, "LSM.Dialog.Move");
        }

        protected override void Build()
        {
            base.Build();
            Message.gameObject.SetActive(false);

            _title = UiClone.Text(OkTexts[0], Panel, "LSM.Dialog.Title", "");
            _title.fontSize = 40;
            _title.color = UiClone.White;
            _title.alignment = TextAnchor.MiddleLeft;
            UiClone.PlaceTL((RectTransform)_title.transform, 50f, 26f, 900f, 56f);

            _context = UiClone.Text(Message, Panel, "LSM.Dialog.Context", "");
            _context.fontSize = 20;
            _context.color = UiClone.Hex("#B9B0A0");
            _context.alignment = TextAnchor.MiddleLeft;
            _context.supportRichText = true;
            UiClone.PlaceTL((RectTransform)_context.transform, 50f, 88f, 900f, 34f);

            _viewport = UiClone.CreateRect("LSM.Dialog.Options", Panel);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _content = UiClone.CreateRect("LSM.Dialog.OptionList", _viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _content;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 15f;
        }

        /// <summary>Lists the targets; <paramref name="choose"/> receives a category id or <see cref="CategoryController.Uncategorized"/>.</summary>
        public void Show(string context, string currentId, IReadOnlyList<CategoryView> categories, Action<string> choose)
        {
            Kind = "move";
            _choose = choose;
            _currentId = currentId;
            OnCancel = null;
            _title.text = Owner.S("Move.Title");
            _context.text = context ?? "";

            var targets = categories.Select(c => new KeyValuePair<string, string>(c.Id, DisplayText.Plain(c.Name))).ToList();
            targets.Add(new KeyValuePair<string, string>(CategoryController.Uncategorized, Owner.S("Category.Uncategorized")));
            while (_options.Count < targets.Count)
            {
                var row = CategoryRow.Create(Owner.Kit, _content, null);
                row.Button.onClick.AddListener(() => Pick(row.Id));
                _options.Add(row);
            }

            var usable = new List<Selectable>();
            for (var i = 0; i < _options.Count; i++)
            {
                var row = _options[i];
                if (i >= targets.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }
                row.Id = targets[i].Key;
                row.Root.name = "LSM.MoveTo." + row.Id;
                row.Root.SetActive(true);
                UiClone.PlaceTL(row.Rect, 0f, i * OptionStep, 900f, OptionStep - 2f);
                var current = row.Id == currentId;
                row.Show(targets[i].Value, current ? Owner.S("Move.Current") : "", false);
                row.SetCountColor(UiClone.Hex("#7C7365"));
                row.SetUsable(!current, current);
                if (!current) usable.Add(row.Button);
            }

            var visible = Math.Min(targets.Count, VisibleOptions);
            var listHeight = visible * OptionStep;
            _content.sizeDelta = new Vector2(0f, targets.Count * OptionStep);
            _content.anchoredPosition = Vector2.zero;
            UiClone.PlaceTL(_viewport, 50f, 136f, 900f, listHeight);
            var height = 136f + listHeight + 30f + ButtonsHeight + 42f;
            SetLabel(CancelTexts, Owner.S("Name.Cancel"));
            Layout(height, false, true);

            for (var i = 0; i < usable.Count; i++)
                UiClone.SetNavigation(usable[i], i > 0 ? usable[i - 1] : null, i < usable.Count - 1 ? usable[i + 1] : Cancel, null, null);
            UiClone.SetNavigation(Cancel, usable.Count > 0 ? usable[usable.Count - 1] : null, null, null, null);
            Open(usable.Count > 0 ? usable[0].gameObject : Cancel.gameObject);
        }

        public override void Relocalize()
        {
            if (!IsOpen) return;
            _title.text = Owner.S("Move.Title");
            SetLabel(CancelTexts, Owner.S("Name.Cancel"));
            var names = Owner.Controller.Catalog.UserCategories().ToDictionary(c => c.Id, c => DisplayText.Plain(c.Name));
            foreach (var row in _options)
            {
                if (!row.Root.activeSelf) continue;
                string name;
                if (row.Id == CategoryController.Uncategorized) name = Owner.S("Category.Uncategorized");
                else if (!names.TryGetValue(row.Id, out name)) continue;
                row.Show(name, row.Id == _currentId ? Owner.S("Move.Current") : "", false);
            }
        }

        private void Pick(string id)
        {
            var choose = _choose;
            _choose = null;
            Close(false);
            if (choose != null) choose(id);
        }
    }
}
