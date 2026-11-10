using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoMSaveManager
{
    /// <summary>
    /// Text entry in a copy of the game's confirm window (the game has no input field of its own): a uGUI InputField made
    /// at run time with the window's SourceHanSerifTC-Bold font, a frame from the window's own panel sprite, a counter,
    /// and a hint or a red validation error under it (design frames 05/06).
    /// Used for naming a save (after saving: 「略過」 instead of 「取消」), creating and renaming a category.
    /// While the field has focus every hotkey button is paused (Enter/Esc belong to the field, and an IME uses Enter to
    /// commit); Enter in the field confirms, Esc cancels.
    /// </summary>
    internal sealed class NameDialog : DialogBase
    {
        private static readonly Color FrameColor = UiClone.Hex("#6B5A45");
        private static readonly Color FieldBack = UiClone.Hex("#0D0C0A");

        private Text _title;
        private Text _context;
        private Image _frame;
        private InputField _field;
        private Text _counter;
        private Text _note;
        private Func<string, string> _apply;
        private string _hint;
        private string _hintKey;
        private string _titleKey;
        private string _placeholderKey;
        private string _cancelLabel;
        private string _errorKey;
        private int _limit;
        private bool _focusedLastFrame;

        public static NameDialog Create(ManagedPanel owner)
        {
            return Create<NameDialog>(owner, "LSM.Dialog.Name");
        }

        public InputField Field
        {
            get { return _field; }
        }

        protected override void Build()
        {
            base.Build();
            Message.gameObject.SetActive(false);
            var serif = Message.font;

            _title = UiClone.Text(OkTexts[0], Panel, "LSM.Dialog.Title", "");
            _title.fontSize = 40;
            _title.color = UiClone.White;
            _title.alignment = TextAnchor.MiddleLeft;
            UiClone.PlaceTL((RectTransform)_title.transform, 50f, 28f, 900f, 56f);

            _context = UiClone.Text(Message, Panel, "LSM.Dialog.Context", "");
            _context.fontSize = 20;
            _context.color = UiClone.Hex("#B9B0A0");
            _context.alignment = TextAnchor.MiddleLeft;
            _context.supportRichText = true;
            UiClone.PlaceTL((RectTransform)_context.transform, 50f, 92f, 900f, 34f);

            // Frame: the window's own panel sprite, tinted (outer = border colour, inner = field background).
            var panelImage = Panel.GetComponent<Image>();
            _frame = UiClone.CreateRect("LSM.Dialog.InputFrame", Panel).gameObject.AddComponent<Image>();
            _frame.sprite = panelImage != null ? panelImage.sprite : null;
            _frame.type = Image.Type.Sliced;
            _frame.color = FrameColor;
            var inner = UiClone.CreateRect("LSM.Dialog.InputBack", _frame.transform).gameObject.AddComponent<Image>();
            inner.sprite = _frame.sprite;
            inner.type = Image.Type.Sliced;
            inner.color = FieldBack;
            UiClone.Fill((RectTransform)inner.transform, 2f, 2f, 2f, 2f);

            var fieldRect = UiClone.CreateRect("LSM.Dialog.Input", inner.transform);
            UiClone.Fill(fieldRect, 18f, 0f, 110f, 0f);
            fieldRect.gameObject.SetActive(false);
            var text = MakeText(fieldRect, "Text", serif, UiClone.White, FontStyle.Normal);
            var placeholder = MakeText(fieldRect, "Placeholder", serif, UiClone.Hex("#5F584D"), FontStyle.Normal);
            _field = fieldRect.gameObject.AddComponent<InputField>();
            _field.textComponent = text;
            _field.placeholder = placeholder;
            _field.lineType = InputField.LineType.SingleLine;
            _field.customCaretColor = true;
            _field.caretColor = UiClone.Gold;
            _field.caretWidth = 2;
            _field.selectionColor = new Color(0.36f, 0.55f, 0.55f, 0.6f);
            _field.transition = Selectable.Transition.None;
            var nav = _field.navigation;
            nav.mode = Navigation.Mode.None;
            _field.navigation = nav;
            _field.onValueChanged.AddListener(_ => UpdateCounter());
            _field.onEndEdit.AddListener(OnEndEdit);
            fieldRect.gameObject.SetActive(true);

            _counter = UiClone.Text(Message, inner.transform, "LSM.Dialog.Counter", "");
            _counter.fontSize = 18;
            _counter.alignment = TextAnchor.MiddleRight;
            UiClone.Fill((RectTransform)_counter.transform, 0f, 0f, 16f, 0f);

            _note = UiClone.Text(Message, Panel, "LSM.Dialog.Note", "");
            _note.alignment = TextAnchor.MiddleLeft;
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiClone.PlaceTL((RectTransform)_note.transform, 50f, 214f, 900f, 34f);
        }

        private static Text MakeText(Transform parent, string name, Font font, Color color, FontStyle style)
        {
            var rect = UiClone.CreateRect(name, parent);
            UiClone.Fill(rect);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 30;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>
        /// Opens the dialog. <paramref name="apply"/> receives the text and returns an error key (shown in red, the
        /// dialog stays open) or null when done. <paramref name="cancelLabel"/> is "Name.Cancel" or "Name.Skip";
        /// <paramref name="onCancel"/> runs when the player cancels or skips.
        /// </summary>
        public void Show(string kind, string titleKey, string context, string placeholderKey, string hintKey, string initial,
            int limit, string cancelLabel, Func<string, string> apply, Action onCancel)
        {
            Kind = kind;
            _apply = apply;
            OnCancel = onCancel;
            _limit = limit;
            _titleKey = titleKey;
            _placeholderKey = placeholderKey;
            _hintKey = hintKey;
            _cancelLabel = cancelLabel;
            _hint = hintKey == null ? null : Owner.S(hintKey);
            _title.text = Owner.S(titleKey);
            _context.text = context ?? "";
            _context.gameObject.SetActive(!string.IsNullOrEmpty(context));
            ((Text)_field.placeholder).text = Owner.S(placeholderKey);
            _field.characterLimit = limit;
            _field.text = initial ?? "";

            var top = string.IsNullOrEmpty(context) ? 100f : 140f;
            UiClone.PlaceTL((RectTransform)_frame.transform, 50f, top, 900f, 64f);
            UiClone.PlaceTL((RectTransform)_note.transform, 50f, top + 74f, 900f, 34f);
            var height = top + 64f + 50f + ButtonsHeight + 60f;
            SetLabel(OkTexts, Owner.S("Name.Ok"));
            SetLabel(CancelTexts, Owner.S(cancelLabel));
            ShowNote(null);
            UpdateCounter();
            Layout(height, true, true);
            Open(_field.gameObject);
            // The field takes focus on the next frame; keep Enter/Esc away from 確定/取消 until then.
            SetOwnHotkeys(false);
            _focusedLastFrame = true;
            // InputField selects all text on focus (its default), so a prefilled old name is replaced by typing.
            _field.ActivateInputField();
        }

        protected override void Submit()
        {
            string error;
            try
            {
                error = _apply == null ? null : _apply(_field.text);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Naming failed: " + ex.Message);
                error = ex.Message;
            }

            if (error != null)
            {
                ShowNote(error);
                return;
            }
            OnCancel = null;
            Close(false);
        }

        public override void Relocalize()
        {
            if (!IsOpen) return;
            _hint = _hintKey == null ? null : Owner.S(_hintKey);
            _title.text = Owner.S(_titleKey);
            ((Text)_field.placeholder).text = Owner.S(_placeholderKey);
            SetLabel(OkTexts, Owner.S("Name.Ok"));
            SetLabel(CancelTexts, Owner.S(_cancelLabel));
            ShowNote(_errorKey);
        }

        private void ShowNote(string errorKey)
        {
            _errorKey = errorKey;
            var error = errorKey != null;
            string text;
            try { text = error ? Owner.S(errorKey) : _hint; }
            catch (Exception) { text = errorKey; }
            _note.text = text ?? "";
            _note.gameObject.SetActive(!string.IsNullOrEmpty(_note.text));
            _note.fontSize = error ? 20 : 18;
            _note.color = error ? UiClone.Red : UiClone.Muted;
            _frame.color = error ? UiClone.Red : FrameColor;
        }

        private void UpdateCounter()
        {
            var length = _field.text == null ? 0 : _field.text.Length;
            _counter.text = length + " / " + _limit;
            _counter.color = length >= _limit ? UiClone.Red : UiClone.Hex("#7C7365");
        }

        private void OnEndEdit(string text)
        {
            if (!IsOpen) return;
            if (_field.wasCanceled)
            {
                Close(true);
                return;
            }
            if (EnterPressed())
            {
                Submit();
                return;
            }
            // Focus left the field another way (mouse, test driver): the next stop is 確定.
            var events = EventSystem.current;
            if (events != null && (events.currentSelectedGameObject == null || events.currentSelectedGameObject == _field.gameObject))
                SelectRelay.SelectQuietly(Ok.gameObject);
        }

        /// <summary>
        /// Enter (or keypad Enter) went down this frame, read from the Input System the game itself uses (the legacy
        /// input manager may be disabled). An Enter that commits an IME composition does not count.
        /// </summary>
        private static bool EnterPressed()
        {
            if (Composing()) return false;
            var keyboard = Keyboard.current;
            if (keyboard != null) return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            try
            {
                return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool Composing()
        {
            try
            {
                return !string.IsNullOrEmpty(Input.compositionString);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void Update()
        {
            var focused = _field != null && _field.isFocused;
            if (focused == _focusedLastFrame) return;
            _focusedLastFrame = focused;
            SetOwnHotkeys(!focused);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _focusedLastFrame = false;
            SetOwnHotkeys(true);
        }
    }
}
