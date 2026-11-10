using System;
using System.Collections.Generic;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LoMSaveManager
{
    /// <summary>What a decorated row is.</summary>
    internal enum RowKind
    {
        /// <summary>A save: name in the big line, in-game date + real time in the small line, 命名 / 移動 / 刪除.</summary>
        Save,
        /// <summary>The load panel's empty row: 「＋ 開始新的旅程」 (clicking it starts a new game, as in the game).</summary>
        NewGame,
        /// <summary>The save panel's empty row: 「＋ 存到「X」」.</summary>
        NewSave,
        /// <summary>A save whose file could not be read (not clickable; the load panel keeps 刪除).</summary>
        Broken,
    }

    /// <summary>The parts of one native slot row (LoadSlotPanel or SaveSlotPanel) the decorator touches.</summary>
    internal sealed class RowParts
    {
        private static readonly AccessTools.FieldRef<LoadSlotPanel, Text> LoadHeader = AccessTools.FieldRefAccess<LoadSlotPanel, Text>("_slotText");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, Text[]> LoadTitles = AccessTools.FieldRefAccess<LoadSlotPanel, Text[]>("_titleText");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, Text[]> LoadTimes = AccessTools.FieldRefAccess<LoadSlotPanel, Text[]>("_timeText");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, GameObject> LoadFocus = AccessTools.FieldRefAccess<LoadSlotPanel, GameObject>("_focusObj");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, GameObject> LoadStar = AccessTools.FieldRefAccess<LoadSlotPanel, GameObject>("_newGamePlusIcon");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, Text> SaveHeader = AccessTools.FieldRefAccess<SaveSlotPanel, Text>("_slotText");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, Text[]> SaveTitles = AccessTools.FieldRefAccess<SaveSlotPanel, Text[]>("_titleText");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, Text[]> SaveTimes = AccessTools.FieldRefAccess<SaveSlotPanel, Text[]>("_timeText");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, GameObject> SaveFocus = AccessTools.FieldRefAccess<SaveSlotPanel, GameObject>("_focusObj");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, GameObject> SaveStar = AccessTools.FieldRefAccess<SaveSlotPanel, GameObject>("_newGamePlusIcon");

        public GameObject Root;
        public Button Slot;
        public Button NativeDelete;
        public Text Header;
        public Text[] Titles;
        public Text[] Times;
        public GameObject Focus;
        public GameObject Star;

        public static RowParts Of(LoadSlotPanel row)
        {
            return new RowParts
            {
                Root = row.gameObject, Slot = row.SlotButton, NativeDelete = row.DeleteButton, Header = LoadHeader(row),
                Titles = LoadTitles(row) ?? new Text[0], Times = LoadTimes(row) ?? new Text[0], Focus = LoadFocus(row), Star = LoadStar(row),
            };
        }

        public static RowParts Of(SaveSlotPanel row)
        {
            return new RowParts
            {
                Root = row.gameObject, Slot = row.SlotButton, Header = SaveHeader(row),
                Titles = SaveTitles(row) ?? new Text[0], Times = SaveTimes(row) ?? new Text[0], Focus = SaveFocus(row), Star = SaveStar(row),
            };
        }
    }

    /// <summary>
    /// Turns the game's slot rows into the approved design (frames 01/03/10): the save's name replaces the in-game date
    /// in the big line (「（未命名）」 in grey when it has none), the date moves to the small line beside the real time,
    /// and 命名 / 移動 / 刪除 (74x46 copies of the game's 刪除 frame) sit right of the row. The game's 傳奇NNN header,
    /// NG+ star and green Focus strip stay as the game set them. Rows are reused, so every call fully restyles the row.
    /// </summary>
    internal static class SlotDecorator
    {
        // Row-relative positions (the row is the game's 470x100 slot; the design's column starts 15px further left).
        private const float ButtonsY = 20f;
        private const float RenameX = 467f;
        private const float MoveX = 547f;
        private const float DeleteX = 627f;
        private const float ButtonW = 74f;
        private const float ButtonH = 46f;
        private const float ButtonFont = 23f;

        private static readonly Color NewTitle = UiClone.Gold;
        private static readonly Color NewTitleHover = UiClone.Hex("#F1CF95");
        private static readonly Color NewSub = UiClone.Hex("#9B927F");
        private static readonly Color DateColor = UiClone.Hex("#B9B0A0");

        public static void Decorate(ManagedPanel owner, RowParts row, string key, RowKind kind)
        {
            var state = row.Root.GetComponent<RowState>() ?? row.Root.AddComponent<RowState>();
            state.Capture(row);
            state.Restore(row);
            state.Key = key;
            row.Root.name = "LSM.Row." + key;

            var buttons = state.Buttons(owner, row);
            switch (kind)
            {
                case RowKind.NewGame:
                case RowKind.NewSave:
                {
                    var label = kind == RowKind.NewGame
                        ? owner.S("Slot.NewGame")
                        : string.Format(owner.S("Slot.NewInCategory"), owner.CategoryDisplayName);
                    var sub = owner.S(kind == RowKind.NewGame ? "Slot.NewGame.Sub" : "Slot.NewInCategory.Sub");
                    if (row.Header != null) row.Header.gameObject.SetActive(false);
                    if (row.Star != null) row.Star.SetActive(false);
                    for (var i = 0; i < row.Titles.Length; i++)
                    {
                        var title = row.Titles[i];
                        title.text = label;
                        title.color = i == 0 ? NewTitle : NewTitleHover;
                        title.resizeTextMaxSize = 36;
                        UiClone.PlaceTL((RectTransform)title.transform, 29f, 14f, 640f, 50f);
                        title.alignment = TextAnchor.MiddleLeft;
                    }
                    foreach (var time in row.Times)
                    {
                        time.text = sub;
                        time.color = NewSub;
                        time.fontStyle = FontStyle.Normal;
                        UiClone.PlaceTL((RectTransform)time.transform, 31f, 64f, 640f, 30f);
                    }
                    SetButtons(buttons, false, false);
                    if (row.NativeDelete != null) row.NativeDelete.gameObject.SetActive(false);
                    break;
                }
                case RowKind.Broken:
                    if (row.Star != null) row.Star.SetActive(false);
                    if (row.Focus != null) row.Focus.SetActive(false);
                    SetButtons(buttons, false, false);
                    if (row.NativeDelete != null)
                    {
                        row.NativeDelete.gameObject.SetActive(true);
                        UiClone.SizeFramedButton(row.NativeDelete, DeleteX, ButtonsY, ButtonW, ButtonH, ButtonFont);
                    }
                    break;
                default:
                {
                    var name = owner.Controller.Catalog.NameOf(key);
                    var date = state.NativeTitle;
                    var time = state.NativeTime;
                    for (var i = 0; i < row.Titles.Length; i++)
                    {
                        row.Titles[i].text = name ?? owner.S("Slot.Unnamed");
                        if (name != null) continue;
                        // Unnamed: smaller and grey (design .ttl.unnamed); the hover copy stays bright.
                        row.Titles[i].resizeTextMaxSize = 30;
                        if (i == 0) row.Titles[i].color = UiClone.Dim;
                    }
                    foreach (var t in row.Times)
                    {
                        t.supportRichText = true;
                        t.text = string.IsNullOrEmpty(date) ? time : "<color=#" + ColorUtility.ToHtmlStringRGB(DateColor) + ">" + date + "</color>　" + time;
                    }
                    SetButtons(buttons, true, true);
                    UiClone.SetTexts(buttons.Rename.gameObject, owner.S("Slot.Rename"));
                    UiClone.SetTexts(buttons.Move.gameObject, owner.S("Slot.Move"));
                    var delete = row.NativeDelete ?? buttons.Delete;
                    if (delete != null)
                    {
                        delete.gameObject.SetActive(true);
                        UiClone.SizeFramedButton(delete, DeleteX, ButtonsY, ButtonW, ButtonH, ButtonFont);
                    }
                    if (buttons.Delete != null && row.NativeDelete == null) UiClone.SetTexts(buttons.Delete.gameObject, owner.DeleteLabel);
                    break;
                }
            }
        }

        /// <summary>The 命名 / 移動 / 刪除 buttons of a decorated row that are showing, left to right.</summary>
        public static List<Selectable> RowButtons(RowParts row)
        {
            var result = new List<Selectable>();
            var state = row.Root.GetComponent<RowState>();
            if (state == null) return result;
            foreach (var b in new[] { state.RenameButton, state.MoveButton, row.NativeDelete ?? state.DeleteButton })
                if (b != null && b.gameObject.activeInHierarchy && b.IsInteractable()) result.Add(b);
            return result;
        }

        private static void SetButtons(RowState.ButtonSet buttons, bool rename, bool move)
        {
            buttons.Rename.gameObject.SetActive(rename);
            buttons.Move.gameObject.SetActive(move);
            if (buttons.Delete != null) buttons.Delete.gameObject.SetActive(rename);
        }

        /// <summary>Per-row memory: the native layout to restore (rows are reused) and the added buttons.</summary>
        internal sealed class RowState : MonoBehaviour
        {
            public string Key;
            public string NativeTitle;
            public string NativeTime;
            public Button RenameButton;
            public Button MoveButton;
            public Button DeleteButton;

            private bool _captured;
            private readonly List<TextLayout> _layouts = new List<TextLayout>();

            internal struct ButtonSet
            {
                public Button Rename;
                public Button Move;
                public Button Delete;
            }

            private struct TextLayout
            {
                public Text Text;
                public Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
                public Color Color;
                public int MaxSize;
                public TextAnchor Alignment;
                public FontStyle Style;
                public bool RichText;
            }

            /// <summary>Remembers the native layout once, and the texts the game's Setup just wrote.</summary>
            public void Capture(RowParts row)
            {
                NativeTitle = row.Titles.Length > 0 ? row.Titles[0].text : null;
                NativeTime = row.Times.Length > 0 ? row.Times[0].text : null;
                if (_captured) return;
                _captured = true;
                var texts = new List<Text>(row.Titles);
                texts.AddRange(row.Times);
                foreach (var text in texts)
                {
                    if (text == null) continue;
                    var rect = (RectTransform)text.transform;
                    _layouts.Add(new TextLayout
                    {
                        Text = text, AnchorMin = rect.anchorMin, AnchorMax = rect.anchorMax, Pivot = rect.pivot,
                        Position = rect.anchoredPosition, Size = rect.sizeDelta, Color = text.color,
                        MaxSize = text.resizeTextMaxSize, Alignment = text.alignment, Style = text.fontStyle, RichText = text.supportRichText,
                    });
                }
            }

            public void Restore(RowParts row)
            {
                foreach (var layout in _layouts)
                {
                    var rect = (RectTransform)layout.Text.transform;
                    rect.anchorMin = layout.AnchorMin;
                    rect.anchorMax = layout.AnchorMax;
                    rect.pivot = layout.Pivot;
                    rect.anchoredPosition = layout.Position;
                    rect.sizeDelta = layout.Size;
                    layout.Text.color = layout.Color;
                    layout.Text.resizeTextMaxSize = layout.MaxSize;
                    layout.Text.alignment = layout.Alignment;
                    layout.Text.fontStyle = layout.Style;
                    layout.Text.supportRichText = layout.RichText;
                }
                if (row.Header != null) row.Header.gameObject.SetActive(true);
            }

            public ButtonSet Buttons(ManagedPanel owner, RowParts row)
            {
                if (RenameButton == null)
                {
                    var template = owner.Kit.DeleteButton;
                    RenameButton = UiClone.FramedButton(template, row.Root.transform, "LSM.Rename", "", ButtonFont, () => owner.BeginNameSave(Key, false));
                    MoveButton = UiClone.FramedButton(template, row.Root.transform, "LSM.Move", "", ButtonFont, () => owner.BeginMoveSave(Key));
                    UiClone.SizeFramedButton(RenameButton, RenameX, ButtonsY, ButtonW, ButtonH, ButtonFont);
                    UiClone.SizeFramedButton(MoveButton, MoveX, ButtonsY, ButtonW, ButtonH, ButtonFont);
                    // The save panel's rows have no 刪除 of their own.
                    if (row.NativeDelete == null)
                        DeleteButton = UiClone.FramedButton(template, row.Root.transform, "LSM.Delete", "", ButtonFont, () => owner.BeginDeleteSave(Key));
                }
                return new ButtonSet { Rename = RenameButton, Move = MoveButton, Delete = DeleteButton };
            }
        }
    }
}
