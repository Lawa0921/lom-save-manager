using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LoMSaveManager
{
    /// <summary>
    /// Fills the game's own load/save panels with one native slot object per key, instead of the fixed 001..020.
    /// The slot objects are the game's prefab (_slotPrefab), set up with the game's own SetSlot/Setup, so they look
    /// and behave exactly like the original rows. Rows are reused between rebuilds (no destroy/instantiate churn,
    /// the selected button survives a refresh); only rows beyond the new count are destroyed.
    /// Only call after <see cref="HookCatalog"/> validated every member.
    /// </summary>
    internal static class SlotListBuilder
    {
        private static readonly AccessTools.FieldRef<LoadGamePanel, GameObject> LoadPrefab = AccessTools.FieldRefAccess<LoadGamePanel, GameObject>("_slotPrefab");
        private static readonly AccessTools.FieldRef<LoadGamePanel, Transform> LoadContainer = AccessTools.FieldRefAccess<LoadGamePanel, Transform>("_slotContainer");
        private static readonly AccessTools.FieldRef<LoadGamePanel, LoadSlotPanel[]> LoadSlots = AccessTools.FieldRefAccess<LoadGamePanel, LoadSlotPanel[]>("_saveSlots");
        private static readonly AccessTools.FieldRef<LoadGamePanel, RecentSaveSlotPanel> Recent = AccessTools.FieldRefAccess<LoadGamePanel, RecentSaveSlotPanel>("_recentSavePanel");
        private static readonly AccessTools.FieldRef<LoadGamePanel, AutoSaveSlotPanel[]> AutoSlots = AccessTools.FieldRefAccess<LoadGamePanel, AutoSaveSlotPanel[]>("_autoSaveSlot");

        private static readonly AccessTools.FieldRef<SaveGamePanel, GameObject> SavePrefab = AccessTools.FieldRefAccess<SaveGamePanel, GameObject>("_slotPrefab");
        private static readonly AccessTools.FieldRef<SaveGamePanel, Transform> SaveContainer = AccessTools.FieldRefAccess<SaveGamePanel, Transform>("_slotContainer");
        private static readonly AccessTools.FieldRef<SaveGamePanel, SaveSlotPanel[]> SaveSlots = AccessTools.FieldRefAccess<SaveGamePanel, SaveSlotPanel[]>("_saveSlots");

        private static readonly AccessTools.FieldRef<LoadSlotPanel, GameSave> LoadData = AccessTools.FieldRefAccess<LoadSlotPanel, GameSave>("_currentData");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, Text[]> LoadTitles = AccessTools.FieldRefAccess<LoadSlotPanel, Text[]>("_titleText");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, Text[]> LoadTimes = AccessTools.FieldRefAccess<LoadSlotPanel, Text[]>("_timeText");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, GameObject> LoadStar = AccessTools.FieldRefAccess<LoadSlotPanel, GameObject>("_newGamePlusIcon");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, GameObject> LoadFocus = AccessTools.FieldRefAccess<LoadSlotPanel, GameObject>("_focusObj");
        private static readonly AccessTools.FieldRef<LoadSlotPanel, LoadGameSlot> LoadSlotMarker = AccessTools.FieldRefAccess<LoadSlotPanel, LoadGameSlot>("_loadGameSlot");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, GameObject> SaveStar = AccessTools.FieldRefAccess<SaveSlotPanel, GameObject>("_newGamePlusIcon");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, GameObject> SaveFocus = AccessTools.FieldRefAccess<SaveSlotPanel, GameObject>("_focusObj");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, GameSave> SaveData = AccessTools.FieldRefAccess<SaveSlotPanel, GameSave>("_currentData");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, Text[]> SaveTitles = AccessTools.FieldRefAccess<SaveSlotPanel, Text[]>("_titleText");
        private static readonly AccessTools.FieldRef<SaveSlotPanel, Text[]> SaveTimes = AccessTools.FieldRefAccess<SaveSlotPanel, Text[]>("_timeText");

        /// <summary>Timing of the last <see cref="Rebuild(LoadGamePanel, IReadOnlyList{string})"/> or save-panel rebuild (diagnostics only).</summary>
        public static RebuildTiming LastTiming { get; private set; }

        public static int Count(LoadGamePanel panel)
        {
            var slots = LoadSlots(panel);
            return slots == null ? 0 : slots.Length;
        }

        public static int Count(SaveGamePanel panel)
        {
            var slots = SaveSlots(panel);
            return slots == null ? 0 : slots.Length;
        }

        public static LoadSlotPanel[] Rows(LoadGamePanel panel)
        {
            return LoadSlots(panel) ?? new LoadSlotPanel[0];
        }

        public static SaveSlotPanel[] Rows(SaveGamePanel panel)
        {
            return SaveSlots(panel) ?? new SaveSlotPanel[0];
        }

        /// <summary>Fills the load panel with one row per key; returns the keys whose save could not be read.</summary>
        public static IReadOnlyList<string> Rebuild(LoadGamePanel panel, IReadOnlyList<string> keys)
        {
            var timing = RebuildTiming.Start("load", keys.Count);
            var rows = Rows(LoadContainer(panel), LoadPrefab(panel), LoadSlots(panel), keys.Count);
            // Published at once: a later failure must not leave the panel pointing at destroyed rows.
            LoadSlots(panel) = rows.ToArray();
            var byKey = new Dictionary<string, LoadSlotPanel>();
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].SetSlot(SlotKey.Number(keys[i]));
                byKey[keys[i]] = rows[i];
            }

            var failed = SlotListPlan.SetupAll(keys, key =>
            {
                var row = byKey[key];
                row.SlotButton.interactable = true;
                try { timing.Measure(row.Setup); }
                catch (Exception)
                {
                    MarkBroken(row.SlotButton, LoadTitles(row), LoadTimes(row), LoadStar(row), LoadFocus(row));
                    LoadData(row) = null;
                    // Keep the delete button (and say the row is not empty, so the delete hotkey works too): a broken
                    // save can still be deleted. Its row cannot be clicked, so it can never be mistaken for an empty
                    // slot (which would start a new game over it).
                    row.DeleteButton.gameObject.SetActive(true);
                    var slot = LoadGameSlotOf(row);
                    if (slot != null) slot.IsEmpty = false;
                    throw;
                }
            });
            ReportFailures("load", failed);
            ApplyNavigation(panel);
            LastTiming = timing.Finish();
            return failed;
        }

        /// <summary>Fills the save panel with one row per key; returns the keys whose save could not be read.</summary>
        public static IReadOnlyList<string> Rebuild(SaveGamePanel panel, IReadOnlyList<string> keys)
        {
            var timing = RebuildTiming.Start("save", keys.Count);
            var rows = Rows(SaveContainer(panel), SavePrefab(panel), SaveSlots(panel), keys.Count);
            SaveSlots(panel) = rows.ToArray();
            var byKey = new Dictionary<string, SaveSlotPanel>();
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].SetSlot(SlotKey.Number(keys[i]));
                byKey[keys[i]] = rows[i];
            }

            var failed = SlotListPlan.SetupAll(keys, key =>
            {
                var row = byKey[key];
                row.SlotButton.interactable = true;
                try { timing.Measure(row.Setup); }
                catch (Exception)
                {
                    // Not clickable: with no data the game would save over it without asking.
                    MarkBroken(row.SlotButton, SaveTitles(row), SaveTimes(row), SaveStar(row), SaveFocus(row));
                    SaveData(row) = null;
                    throw;
                }
            });
            ReportFailures("save", failed);
            ApplyNavigation(panel);
            LastTiming = timing.Finish();
            return failed;
        }

        /// <summary>Recent and auto-save rows of the load panel, each on its own so one broken file cannot stop the others.</summary>
        public static void SetupSidebar(LoadGamePanel panel)
        {
            var autos = AutoSlots(panel) ?? new AutoSaveSlotPanel[0];
            foreach (var auto in autos)
            {
                if (auto == null || !auto.gameObject.activeSelf) continue;
                try { auto.Setup(); }
                catch (Exception ex) { SaveManagerPlugin.LogWarn("Auto-save row could not be read: " + ex.Message); }
            }

            var recent = Recent(panel);
            if (recent == null) return;
            try { recent.Setup(); }
            catch (Exception ex) { SaveManagerPlugin.LogWarn("Recent-save row could not be read: " + ex.Message); }
        }

        /// <summary>
        /// The game's navigation, for any number of rows (its own code indexes [i+1] and [0] without a check):
        /// rows up/down, row left to the recent save, delete button beside its row, sidebar right to the first row.
        /// </summary>
        public static void ApplyNavigation(LoadGamePanel panel)
        {
            var slots = LoadSlots(panel) ?? new LoadSlotPanel[0];
            var recent = Recent(panel);
            var recentButton = recent == null ? null : recent.SlotButton;
            var plan = SlotListPlan.Navigation(slots.Length);
            for (var i = 0; i < slots.Length; i++)
            {
                var up = plan.Up(i) < 0 ? null : slots[plan.Up(i)].SlotButton;
                var down = plan.Down(i) < 0 ? null : slots[plan.Down(i)].SlotButton;

                var nav = slots[i].SlotButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = recentButton;
                nav.selectOnUp = up;
                nav.selectOnDown = down;
                slots[i].SlotButton.navigation = nav;

                var deleteNav = slots[i].DeleteButton.navigation;
                deleteNav.mode = Navigation.Mode.Explicit;
                deleteNav.selectOnLeft = slots[i].SlotButton;
                deleteNav.selectOnUp = up;
                deleteNav.selectOnDown = down;
                slots[i].DeleteButton.navigation = deleteNav;
            }

            var first = slots.Length > 0 ? slots[0].SlotButton : null;
            var autos = AutoSlots(panel) ?? new AutoSaveSlotPanel[0];
            var autoPlan = SlotListPlan.Navigation(autos.Length);
            for (var i = 0; i < autos.Length; i++)
            {
                var up = autoPlan.Up(i) < 0 ? null : autos[autoPlan.Up(i)].SlotButton;
                var down = autoPlan.Down(i) < 0 ? null : autos[autoPlan.Down(i)].SlotButton;

                var nav = autos[i].SlotButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnRight = first;
                nav.selectOnUp = up;
                nav.selectOnDown = down;
                autos[i].SlotButton.navigation = nav;

                var deleteNav = autos[i].DeleteButton.navigation;
                deleteNav.mode = Navigation.Mode.Explicit;
                deleteNav.selectOnLeft = autos[i].SlotButton;
                deleteNav.selectOnRight = first;
                deleteNav.selectOnUp = up;
                deleteNav.selectOnDown = down;
                autos[i].DeleteButton.navigation = deleteNav;
            }

            if (recentButton != null)
            {
                var nav = recentButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnRight = first;
                recentButton.navigation = nav;
            }
        }

        public static void ApplyNavigation(SaveGamePanel panel)
        {
            var slots = SaveSlots(panel) ?? new SaveSlotPanel[0];
            var plan = SlotListPlan.Navigation(slots.Length);
            for (var i = 0; i < slots.Length; i++)
            {
                var nav = slots[i].SlotButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = plan.Up(i) < 0 ? null : slots[plan.Up(i)].SlotButton;
                nav.selectOnDown = plan.Down(i) < 0 ? null : slots[plan.Down(i)].SlotButton;
                slots[i].SlotButton.navigation = nav;
            }
        }

        /// <summary>
        /// When the selected button belonged to a row that was just removed, selects the last remaining row
        /// (or the recent-save row) so keyboard/gamepad navigation keeps working.
        /// </summary>
        public static void KeepSelectionAlive(LoadGamePanel panel)
        {
            var slots = LoadSlots(panel) ?? new LoadSlotPanel[0];
            var recent = Recent(panel);
            KeepSelectionAlive(LoadContainer(panel), slots.Select(s => s.SlotButton).ToList(), recent == null ? null : recent.SlotButton);
        }

        private static void KeepSelectionAlive(Transform container, IList<Button> rows, Selectable fallback)
        {
            var events = EventSystem.current;
            if (events == null) return;
            var selected = events.currentSelectedGameObject;
            if (selected == null || selected.activeInHierarchy || !selected.transform.IsChildOf(container)) return;

            var target = rows.Count > 0 ? (Selectable)rows[rows.Count - 1] : fallback;
            if (target != null) target.Select();
        }

        // Rows to fill: the live rows already in the container (reused in order), new prefab instances for the rest.
        // Everything else under the container (the scene's placeholder rows, rows beyond the new count) is removed.
        private static List<T> Rows<T>(Transform container, GameObject prefab, T[] current, int count) where T : Component
        {
            var reusable = new List<T>();
            if (current != null)
                foreach (var row in current)
                    if (row != null && row.transform.parent == container && row.gameObject.activeSelf && !reusable.Contains(row)) reusable.Add(row);

            var rows = reusable.Take(count).ToList();
            while (rows.Count < count)
                rows.Add(Object.Instantiate(prefab, container, false).GetComponent<T>());

            var keep = new HashSet<Transform>(rows.Select(r => r.transform));
            foreach (Transform child in container.Cast<Transform>().ToList())
            {
                if (keep.Contains(child)) continue;
                // Destroy is deferred to the end of the frame; hiding it now keeps it out of the layout and navigation.
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }

            for (var i = 0; i < rows.Count; i++) rows[i].transform.SetSiblingIndex(i);
            return rows;
        }

        private static LoadGameSlot LoadGameSlotOf(LoadSlotPanel row)
        {
            return LoadSlotMarker(row);
        }

        private static void MarkBroken(Button button, Text[] titles, Text[] times, GameObject star, GameObject focus)
        {
            button.interactable = false;
            // Setup threw before it set the star and the current-save strip; whatever the previous key left is wrong.
            if (star != null) star.SetActive(false);
            if (focus != null) focus.SetActive(false);
            string noData = null;
            try { noData = LocalizationManager.Instance.LocaleResolver.GetString("System/NoData"); }
            catch (Exception) { }
            foreach (var text in titles ?? new Text[0]) if (text != null) text.text = noData ?? "-";
            foreach (var text in times ?? new Text[0]) if (text != null) text.text = "";
        }

        private static void ReportFailures(string panel, IReadOnlyList<string> failedKeys)
        {
            if (failedKeys.Count == 0) return;
            SaveManagerPlugin.LogWarn("Some saves could not be read in the " + panel + " list and are shown as no data: "
                + string.Join(", ", failedKeys.ToArray()));
        }
    }

    /// <summary>
    /// How long one list rebuild took: each row's Setup() (the game reads and deserializes that save file) and the
    /// whole rebuild. Logged at debug level and shown by <see cref="SaveManagerDiagnostics"/>; nothing depends on it.
    /// </summary>
    internal sealed class RebuildTiming
    {
        private readonly System.Diagnostics.Stopwatch _total = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<double> _setupMs = new List<double>();

        public string Panel { get; private set; }

        public int Rows { get; private set; }

        public double TotalMs { get; private set; }

        public IReadOnlyList<double> SetupMs
        {
            get { return _setupMs; }
        }

        public static RebuildTiming Start(string panel, int rows)
        {
            return new RebuildTiming { Panel = panel, Rows = rows };
        }

        /// <summary>Runs one row's Setup() and records its time (also when it throws).</summary>
        public void Measure(Action setup)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { setup(); }
            finally { _setupMs.Add(watch.Elapsed.TotalMilliseconds); }
        }

        public RebuildTiming Finish()
        {
            TotalMs = _total.Elapsed.TotalMilliseconds;
            SaveManagerPlugin.LogDebug(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Rebuilt the {0} list: {1} rows in {2:0.0} ms (Setup total {3:0.0} ms, mean {4:0.00} ms, max {5:0.00} ms)",
                Panel, Rows, TotalMs, _setupMs.Sum(), _setupMs.Count == 0 ? 0 : _setupMs.Average(), _setupMs.Count == 0 ? 0 : _setupMs.Max()));
            return this;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "panel", Panel },
                { "rows", Rows },
                { "total_ms", Math.Round(TotalMs, 3) },
                { "setup_ms", _setupMs.Select(ms => (object)Math.Round(ms, 3)).ToList() },
            };
        }
    }
}
