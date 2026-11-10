using System;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// The game objects the mod copies (it draws nothing of its own): the load panel's slot row prefab (category rows,
    /// the 刪除 button frame, the gold serif and calligraphy texts) and the panel's own confirm window (dialogs).
    /// The load-row prefab is taken from any LoadGamePanel loaded in the scene, so the save panel can use it too.
    /// </summary>
    internal sealed class UiKit
    {
        private static readonly AccessTools.FieldRef<LoadGamePanel, GameObject> LoadPrefab = AccessTools.FieldRefAccess<LoadGamePanel, GameObject>("_slotPrefab");
        private static readonly AccessTools.FieldRef<LoadGamePanel, CommonPanel> DeletePanel = AccessTools.FieldRefAccess<LoadGamePanel, CommonPanel>("_deletePanel");
        private static readonly AccessTools.FieldRef<SaveGamePanel, CommonPanel> ConfirmPanel = AccessTools.FieldRefAccess<SaveGamePanel, CommonPanel>("_confirmPanel");

        private UiKit()
        {
        }

        /// <summary>The game's LoadGameSlot prefab (row 470x100 with Slot/{Focus,Normal,Hover,Header,NewGamePlus} and Delete).</summary>
        public GameObject RowTemplate { get; private set; }

        /// <summary>The prefab's framed 刪除 button.</summary>
        public GameObject DeleteButton { get; private set; }

        /// <summary>Gold SourceHanSerifTC-Bold text (the row's 傳奇NNN header).</summary>
        public Text SerifText { get; private set; }

        /// <summary>Calligraphy text (the row's title, font follows the language).</summary>
        public Text CalligraphyText { get; private set; }

        /// <summary>The panel's own confirm window (CommonPanel root with Panel/{Message, MenuButton_Submit, MenuButton_Cancel}).</summary>
        public GameObject ConfirmTemplate { get; private set; }

        public static UiKit For(MonoBehaviour panel)
        {
            var load = panel as LoadGamePanel;
            var save = panel as SaveGamePanel;
            var confirm = load != null ? DeletePanel(load) : save != null ? ConfirmPanel(save) : null;
            if (confirm == null) throw new InvalidOperationException("the panel has no confirm window");

            var rowPrefab = load != null ? LoadPrefab(load) : null;
            if (rowPrefab == null)
            {
                var anyLoad = Resources.FindObjectsOfTypeAll<LoadGamePanel>().FirstOrDefault(p => p != null && LoadPrefab(p) != null);
                if (anyLoad != null) rowPrefab = LoadPrefab(anyLoad);
            }
            if (rowPrefab == null) throw new InvalidOperationException("no load-row prefab is loaded");

            var kit = new UiKit { RowTemplate = rowPrefab, ConfirmTemplate = confirm.gameObject };
            kit.DeleteButton = Need(rowPrefab.transform, "Delete").gameObject;
            kit.SerifText = Need(rowPrefab.transform, "Header").GetComponent<Text>();
            kit.CalligraphyText = Need(rowPrefab.transform, "Title").GetComponent<Text>();
            if (kit.DeleteButton.GetComponent<Button>() == null || kit.SerifText == null || kit.CalligraphyText == null)
                throw new InvalidOperationException("the load-row prefab has an unexpected layout");
            if (Need(confirm.transform, "Message").GetComponent<Text>() == null) throw new InvalidOperationException("the confirm window has no message");
            Need(confirm.transform, "MenuButton_Submit");
            Need(confirm.transform, "MenuButton_Cancel");
            return kit;
        }

        private static Transform Need(Transform root, string name)
        {
            var found = UiClone.FindDeep(root, name);
            if (found == null) throw new InvalidOperationException("missing " + name + " under " + root.name);
            return found;
        }
    }
}
