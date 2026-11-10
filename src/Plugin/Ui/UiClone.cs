using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// Copies of the game's own UI objects (no new art): a copy is made under an inactive holder so none of its
    /// scripts wake up before the game logic is stripped from it, every Button loses its persistent (scene-bound)
    /// onClick calls, and texts the mod sets lose their LeanLocalizedText so the language system does not overwrite
    /// them. Fonts stay switchable per language (LeanLocalizedTextFont is kept).
    /// Also holds the small layout helpers: positions are in the 1920x1080 reference canvas, origin top-left.
    /// </summary>
    internal static class UiClone
    {
        // Game scripts that would read saves, listen to global game events or drive panels if left on a copy.
        private static readonly string[] GameLogic =
        {
            "LoadSlotPanel", "SaveSlotPanel", "LoadGameSlot", "RecentSaveSlotPanel", "AutoSaveSlotPanel", "CommonPanel",
            "LoadGamePanel", "SaveGamePanel", "ScrollRectAutoScroll", "GamepadIconController",
        };

        public static readonly Color Gold = Hex("#D9B077");
        public static readonly Color TextColor = Hex("#D8D5D5");
        public static readonly Color White = Hex("#FAFAFA");
        public static readonly Color Muted = Hex("#8D8270");
        public static readonly Color Dim = Hex("#8A8274");
        public static readonly Color Red = Hex("#D0705A");

        /// <summary>A stripped copy of <paramref name="source"/> under <paramref name="parent"/>; keeps the source's activeSelf.</summary>
        public static GameObject Instantiate(GameObject source, Transform parent, string name)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var holder = new GameObject("LSM.CloneHolder", typeof(RectTransform));
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            try
            {
                var copy = Object.Instantiate(source, holder.transform, false);
                copy.name = name;
                Strip(copy);
                copy.transform.SetParent(parent, false);
                return copy;
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        /// <summary>Removes game logic and scene-bound button calls from a copy.</summary>
        public static void Strip(GameObject copy)
        {
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;
                if (GameLogic.Contains(typeName) || typeName.EndsWith("EventListener", StringComparison.Ordinal))
                    Object.DestroyImmediate(component);
            }
            foreach (var button in copy.GetComponentsInChildren<Button>(true))
                button.onClick = new Button.ButtonClickedEvent();
        }

        /// <summary>Removes the components that would overwrite a text with a translation (fonts stay localized).</summary>
        public static void RemoveLocalizers(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;
                if (typeName == "LeanLocalizedText" || typeName == "LeanLocalizedTextMeshProUGUI" || typeName == "LeanLocalizedTextArgs")
                    Object.DestroyImmediate(component);
            }
        }

        /// <summary>A copy of one Text object (its children removed) with <paramref name="text"/>.</summary>
        public static Text Text(Text template, Transform parent, string name, string text)
        {
            var go = Instantiate(template.gameObject, parent, name);
            for (var i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            RemoveLocalizers(go);
            go.SetActive(true);
            var copy = go.GetComponent<Text>();
            copy.text = text;
            copy.raycastTarget = false;
            return copy;
        }

        /// <summary>Sets every Text under <paramref name="root"/> (inactive state children included).</summary>
        public static void SetTexts(GameObject root, string text)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true)) t.text = text;
        }

        public static Color Hex(string hex)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(hex, out color) ? color : Color.white;
        }

        // ---- layout -----------------------------------------------------------------------------------------------

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Places <paramref name="rect"/> at (x, y, w, h) relative to its parent's top-left corner.</summary>
        public static void PlaceTL(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>A strip at <paramref name="top"/> (height <paramref name="h"/>) spanning the parent's width minus the margins.</summary>
        public static void PlaceRow(RectTransform rect, float left, float top, float right, float h)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, -top - h);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Places <paramref name="rect"/> against its parent's top-right corner (x = distance from the right edge).</summary>
        public static void PlaceTR(RectTransform rect, float right, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Stretches <paramref name="rect"/> over its parent, inset by the given margins.</summary>
        public static void Fill(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Places <paramref name="rect"/> at (x, y, w, h) of the 1920x1080 root canvas, whatever its parent.</summary>
        public static void PlaceCanvas(RectTransform rect, float x, float y, float w, float h)
        {
            var root = RootCanvas(rect);
            var corners = new Vector3[4];
            root.GetWorldCorners(corners); // bottom-left, top-left, top-right, bottom-right
            var right = (corners[2] - corners[1]) / root.rect.width;
            var down = (corners[0] - corners[1]) / root.rect.height;

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.position = corners[1] + right * x + down * y;
            var parentScale = rect.parent == null ? 1f : rect.parent.lossyScale.x;
            var k = parentScale == 0f ? 1f : root.lossyScale.x / parentScale;
            rect.sizeDelta = new Vector2(w * k, h * k);
        }

        public static RectTransform RootCanvas(Transform node)
        {
            var canvas = node.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("no canvas above " + node.name);
            return (RectTransform)canvas.rootCanvas.transform;
        }

        /// <summary>The first descendant (inactive included) with this name, or null.</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ---- framed buttons (copies of the game's 刪除 button) ----------------------------------------------------

        // The 刪除 frame's image is larger than the frame it shows (transparent margins around a sliced border);
        // these margins were measured on the native button (visible 109x57 inside a 200x135 image).
        private const float FrameMarginX = 91f;
        private const float FrameMarginY = 78f;

        /// <summary>A copy of the game's framed 刪除 button, relabelled and sized so its visible frame is w x h.</summary>
        public static Button FramedButton(GameObject deleteTemplate, Transform parent, string name, string label, float fontSize, Action onClick)
        {
            var go = Instantiate(deleteTemplate, parent, name);
            RemoveLocalizers(go);
            go.SetActive(true);
            SetTexts(go, label);
            foreach (var t in go.GetComponentsInChildren<Text>(true)) t.fontSize = Mathf.RoundToInt(fontSize);
            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>Sizes a framed button (a 刪除 copy or the native one) at (x, y) of its parent so the frame is w x h.</summary>
        public static void SizeFramedButton(Button button, float x, float y, float w, float h, float fontSize)
        {
            PlaceTL((RectTransform)button.transform, x, y, w, h);
            SizeFrame(button, w, h, fontSize);
        }

        /// <summary>Sizes a framed button in place (its position is kept): hit area w x h, frame image and label to match.</summary>
        public static void SizeFrame(Button button, float w, float h, float fontSize)
        {
            var rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(w, h);
            foreach (Transform state in rect)
            {
                var stateRect = state as RectTransform;
                if (stateRect == null) continue;
                stateRect.anchorMin = stateRect.anchorMax = stateRect.pivot = new Vector2(0.5f, 0.5f);
                stateRect.anchoredPosition = Vector2.zero;
                stateRect.sizeDelta = new Vector2(w + FrameMarginX, h + FrameMarginY);
                foreach (Transform child in stateRect)
                {
                    var text = child.GetComponent<Text>();
                    if (text == null) continue;
                    var textRect = (RectTransform)child;
                    textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
                    textRect.anchoredPosition = Vector2.zero;
                    textRect.sizeDelta = new Vector2(w + 20f, h);
                    text.fontSize = Mathf.RoundToInt(fontSize);
                    text.alignment = TextAnchor.MiddleCenter;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
        }

        public static void SetNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            if (selectable == null) return;
            var nav = selectable.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            nav.selectOnLeft = left;
            nav.selectOnRight = right;
            selectable.navigation = nav;
        }

        /// <summary>The usable ones among <paramref name="items"/> (active and interactable), in order.</summary>
        public static List<Selectable> Usable(IEnumerable<Selectable> items)
        {
            return items.Where(s => s != null && s.gameObject.activeInHierarchy && s.IsInteractable()).ToList();
        }
    }
}
