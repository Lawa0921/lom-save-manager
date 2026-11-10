using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// A copy of the panel's own confirm window (the black Panel with Message, 確定 and 取消, sorted above the panel by
    /// its own Canvas). While one is open the game's hotkey buttons elsewhere (Esc closing the panel, …) are paused, as the
    /// game does for its own confirm windows; the dialog's 確定/取消 keep their Enter/Esc hotkeys.
    /// </summary>
    internal class DialogBase : MonoBehaviour
    {
        protected const float PanelWidth = 1000f;
        protected const float ButtonsHeight = 88f;

        private static readonly AccessTools.FieldRef<InputButton, bool> InputButtonActive = AccessTools.FieldRefAccess<InputButton, bool>("_active");

        protected ManagedPanel Owner;
        protected RectTransform Panel;
        protected Text Message;
        protected Button Ok;
        protected Button Cancel;
        protected Text[] OkTexts;
        protected Text[] CancelTexts;
        protected Action OnCancel;

        private CanvasGroup _group;
        private readonly List<KeyValuePair<InputButton, bool>> _paused = new List<KeyValuePair<InputButton, bool>>();
        private GameObject _restoreSelection;

        public bool IsOpen
        {
            get { return gameObject.activeSelf; }
        }

        public string Kind { get; protected set; }

        protected static T Create<T>(ManagedPanel owner, string name) where T : DialogBase
        {
            var template = owner.Kit.ConfirmTemplate;
            var go = UiClone.Instantiate(template, template.transform.parent, name);
            go.SetActive(false);
            var dialog = go.AddComponent<T>();
            dialog.Owner = owner;
            dialog.Build();
            return dialog;
        }

        protected virtual void Build()
        {
            _group = GetComponent<CanvasGroup>();
            Message = UiClone.FindDeep(transform, "Message").GetComponent<Text>();
            Panel = (RectTransform)Message.transform.parent;
            UiClone.RemoveLocalizers(Message.gameObject);
            Ok = UiClone.FindDeep(transform, "MenuButton_Submit").GetComponent<Button>();
            Cancel = UiClone.FindDeep(transform, "MenuButton_Cancel").GetComponent<Button>();
            Ok.name = "LSM.Dialog.Ok";
            Cancel.name = "LSM.Dialog.Cancel";
            UiClone.RemoveLocalizers(Ok.gameObject);
            UiClone.RemoveLocalizers(Cancel.gameObject);
            OkTexts = Ok.GetComponentsInChildren<Text>(true);
            CancelTexts = Cancel.GetComponentsInChildren<Text>(true);
            Ok.onClick.AddListener(Submit);
            Cancel.onClick.AddListener(() => Close(true));
        }

        protected virtual void Submit()
        {
        }

        /// <summary>Sizes the black panel (height <paramref name="height"/>) centred on the screen and puts 確定/取消 at its bottom.</summary>
        protected void Layout(float height, bool showOk, bool showCancel)
        {
            UiClone.PlaceCanvas(Panel, (1920f - PanelWidth) / 2f, (1080f - height) / 2f, PanelWidth, height);
            var buttonsY = height - ButtonsHeight - 42f;
            Ok.gameObject.SetActive(showOk);
            Cancel.gameObject.SetActive(showCancel);
            if (showOk && showCancel)
            {
                UiClone.PlaceTL((RectTransform)Ok.transform, 0f, buttonsY, 460f, ButtonsHeight);
                UiClone.PlaceTL((RectTransform)Cancel.transform, 549f, buttonsY, 460f, ButtonsHeight);
            }
            else
            {
                var only = showOk ? Ok : Cancel;
                UiClone.PlaceTL((RectTransform)only.transform, (PanelWidth - 460f) / 2f, buttonsY, 460f, ButtonsHeight);
            }
            UiClone.SetNavigation(Ok, null, null, null, showCancel ? Cancel : null);
            UiClone.SetNavigation(Cancel, null, null, showOk ? Ok : null, null);
        }

        protected static void SetLabel(Text[] texts, string label)
        {
            foreach (var text in texts) text.text = label;
        }

        protected void Open(GameObject focus)
        {
            var events = EventSystem.current;
            _restoreSelection = events != null ? events.currentSelectedGameObject : null;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            if (_group != null)
            {
                _group.alpha = 1f;
                _group.interactable = true;
                _group.blocksRaycasts = true;
            }
            PauseOtherHotkeys();
            if (focus != null) SelectRelay.SelectQuietly(focus);
        }

        /// <summary>Closes the dialog; a cancel runs the dialog's cancel action (e.g. 略過 after a save).</summary>
        public void Close(bool cancelled)
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            ResumeHotkeys();
            var cancel = OnCancel;
            OnCancel = null;
            if (cancelled && cancel != null) cancel();
            var events = EventSystem.current;
            if (events == null) return;
            var current = events.currentSelectedGameObject;
            if (current == null || !current.activeInHierarchy || current.transform.IsChildOf(transform))
                SelectRelay.SelectQuietly(_restoreSelection != null && _restoreSelection.activeInHierarchy ? _restoreSelection : null);
        }

        /// <summary>Re-applies the mod's texts after a language change while the dialog is open.</summary>
        public virtual void Relocalize()
        {
        }

        private void PauseOtherHotkeys()
        {
            ResumeHotkeys();
            foreach (var button in Object.FindObjectsOfType<InputButton>())
            {
                if (button.transform.IsChildOf(transform)) continue;
                _paused.Add(new KeyValuePair<InputButton, bool>(button, InputButtonActive(button)));
                button.SetActive(false);
            }
        }

        private void ResumeHotkeys()
        {
            foreach (var pair in _paused)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
            _paused.Clear();
        }

        /// <summary>Pauses or resumes this dialog's own 確定/取消 hotkeys (while typing, Enter/Esc belong to the text field).</summary>
        protected void SetOwnHotkeys(bool active)
        {
            foreach (var button in GetComponentsInChildren<InputButton>(true)) button.SetActive(active);
        }

        protected virtual void OnDisable()
        {
            ResumeHotkeys();
        }
    }

    /// <summary>A yes/no question in the game's confirm window (delete a category, delete a save from the save panel).</summary>
    internal sealed class ConfirmDialog : DialogBase
    {
        private Action _onOk;
        private Func<string> _message;

        public static ConfirmDialog Create(ManagedPanel owner)
        {
            return Create<ConfirmDialog>(owner, "LSM.Dialog.Confirm");
        }

        /// <summary>
        /// Shows the text <paramref name="message"/> builds (a "\n" makes a second line; it is built again after a
        /// language change); the cursor starts on 取消 when <paramref name="focusCancel"/>.
        /// </summary>
        public void Show(string kind, Func<string> message, Action onOk, bool focusCancel)
        {
            Kind = kind;
            _onOk = onOk;
            _message = message;
            OnCancel = null;
            ApplyTexts();
            Open(focusCancel ? Cancel.gameObject : Ok.gameObject);
        }

        public override void Relocalize()
        {
            if (IsOpen && _message != null) ApplyTexts();
        }

        private void ApplyTexts()
        {
            var message = _message();
            var lines = message.Split('\n').Length;
            var height = lines > 1 ? 320f : 240f;
            Message.text = message;
            Message.fontSize = lines > 1 ? 30 : 42;
            Message.alignment = TextAnchor.MiddleCenter;
            Message.horizontalOverflow = HorizontalWrapMode.Wrap;
            Message.verticalOverflow = VerticalWrapMode.Overflow;
            UiClone.PlaceTL((RectTransform)Message.transform, 50f, 24f, PanelWidth - 100f, height - ButtonsHeight - 66f);
            SetLabel(OkTexts, Owner.S("Name.Ok"));
            SetLabel(CancelTexts, Owner.S("Name.Cancel"));
            Layout(height, true, true);
        }

        protected override void Submit()
        {
            var action = _onOk;
            _onOk = null;
            Close(false);
            if (action != null) Owner.Run(action);
        }
    }

    /// <summary>A short floating message near the top of the panel that fades out (move done, an error, …).</summary>
    internal sealed class Toast : MonoBehaviour
    {
        private Text _text;
        private CanvasGroup _group;
        private Coroutine _running;

        public static Toast Create(ManagedPanel owner, Transform parent)
        {
            var template = UiClone.FindDeep(owner.Kit.ConfirmTemplate.transform, "Message").parent.gameObject;
            var go = UiClone.Instantiate(template, parent, "LSM.Toast");
            for (var i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            go.SetActive(false);
            var image = go.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.047f, 0.043f, 0.035f, 0.94f);
                image.raycastTarget = false;
            }
            var toast = go.AddComponent<Toast>();
            toast._group = go.AddComponent<CanvasGroup>();
            toast._group.blocksRaycasts = false;
            toast._text = UiClone.Text(owner.Kit.SerifText, go.transform, "LSM.Toast.Text", "");
            toast._text.fontSize = 26;
            toast._text.color = UiClone.TextColor;
            toast._text.alignment = TextAnchor.MiddleCenter;
            UiClone.Fill((RectTransform)toast._text.transform, 30f, 0f, 30f, 0f);
            return toast;
        }

        public void Show(string message)
        {
            transform.SetAsLastSibling();
            _text.text = message;
            var width = Mathf.Max(640f, _text.preferredWidth + 120f);
            UiClone.PlaceCanvas((RectTransform)transform, (1920f - width) / 2f, 176f, width, 62f);
            gameObject.SetActive(true);
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Fade());
        }

        public string Current
        {
            get { return gameObject.activeSelf ? _text.text : null; }
        }

        // Menus pause the game (timeScale 0), so the timing is in real time.
        private IEnumerator Fade()
        {
            _group.alpha = 1f;
            yield return new WaitForSecondsRealtime(2.2f);
            for (var t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - t / 0.6f;
                yield return null;
            }
            gameObject.SetActive(false);
            _running = null;
        }
    }
}
