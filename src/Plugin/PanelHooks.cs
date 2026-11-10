using System;
using System.Collections.Generic;
using HarmonyLib;
using Mortal.Core;

namespace LoMSaveManager
{
    /// <summary>
    /// Replaces the fixed 001..020 rows of the game's load and save panels with the selected category's saves, inside
    /// the two-column layout of <see cref="ManagedPanel"/> (category sidebar, naming, moving).
    ///
    /// Only panel methods are ever skipped (return-false prefixes on CreateSlot, SetButtonNavigation, UpdateSlot);
    /// SaveSystem is never skipped, and OnPanelOpen / SaveData / ConfirmSaveSlot / ConfirmDeleteSlot always run
    /// (other mods, e.g. Story Weaver, have postfixes on them). Whenever the save manager is not ready (the save folder
    /// is unknown), the panel could not be laid out, or a hook fails, the game's own method runs, so the panel falls
    /// back to the original 20 rows.
    ///
    /// Refreshing: the list is rebuilt (reconcile + recompute + Setup) once per panel open, once after each save or
    /// delete, and whenever the player changes the category or the catalog. The game's methods call UpdateSlot
    /// themselves; while one of them is running the rows are not set up a second time (only the load panel's
    /// recent/auto rows are), and the rebuild happens in its postfix instead.
    /// </summary>
    internal static class PanelHooks
    {
        private static readonly AccessTools.FieldRef<SaveGamePanel, string> ConfirmSlot = AccessTools.FieldRefAccess<SaveGamePanel, string>("_currentConfirmSlot");
        private static readonly AccessTools.FieldRef<LoadGamePanel, string> DeleteSlot = AccessTools.FieldRefAccess<LoadGamePanel, string>("_currentDeleteSlot");

        // Panels inside OnPanelOpen / SaveData / ConfirmSaveSlot / ConfirmDeleteSlot (see the class comment).
        private static readonly HashSet<object> Deferred = new HashSet<object>();

        public static void Install(Harmony harmony, HookCatalog catalog)
        {
            var loadCreate = Hook(nameof(LoadCreateSlot));
            var loadNavigation = Hook(nameof(LoadSetButtonNavigation));
            var loadUpdate = Hook(nameof(LoadUpdateSlot));
            var loadOpen = Hook(nameof(LoadOnPanelOpen));
            var loadOpenEnd = Hook(nameof(LoadOnPanelOpenEnd));
            var loadDeleteBegin = Hook(nameof(LoadDeleteBegin));
            var loadAfterDelete = Hook(nameof(LoadAfterDelete));
            var loadEnd = Hook(nameof(LoadEndDefer));

            var saveCreate = Hook(nameof(SaveCreateSlot));
            var saveNavigation = Hook(nameof(SaveSetButtonNavigation));
            var saveUpdate = Hook(nameof(SaveUpdateSlot));
            var saveOpen = Hook(nameof(SaveOnPanelOpen));
            var saveOpenEnd = Hook(nameof(SaveOnPanelOpenEnd));
            var saveDefer = Hook(nameof(SaveDefer));
            var saveAfterData = Hook(nameof(SaveAfterSaveData));
            var confirmBegin = Hook(nameof(SaveConfirmBegin));
            var saveAfterConfirm = Hook(nameof(SaveAfterConfirm));
            var saveEnd = Hook(nameof(SaveEndDefer));

            harmony.Patch(catalog.LoadPanelMethod("CreateSlot"), prefix: loadCreate);
            harmony.Patch(catalog.LoadPanelMethod("SetButtonNavigation"), prefix: loadNavigation);
            harmony.Patch(catalog.LoadPanelMethod("UpdateSlot"), prefix: loadUpdate);
            harmony.Patch(catalog.LoadPanelMethod("OnPanelOpen"), prefix: loadOpen, finalizer: loadOpenEnd);
            harmony.Patch(catalog.LoadPanelMethod("ConfirmDeleteSlot"), prefix: loadDeleteBegin, postfix: loadAfterDelete, finalizer: loadEnd);

            harmony.Patch(catalog.SavePanelMethod("CreateSlot"), prefix: saveCreate);
            harmony.Patch(catalog.SavePanelMethod("SetButtonNavigation"), prefix: saveNavigation);
            harmony.Patch(catalog.SavePanelMethod("UpdateSlot"), prefix: saveUpdate);
            harmony.Patch(catalog.SavePanelMethod("OnPanelOpen"), prefix: saveOpen, finalizer: saveOpenEnd);
            harmony.Patch(catalog.SavePanelMethod("SaveData"), prefix: saveDefer, postfix: saveAfterData, finalizer: saveEnd);
            harmony.Patch(catalog.SavePanelMethod("ConfirmSaveSlot"), prefix: confirmBegin, postfix: saveAfterConfirm, finalizer: saveEnd);
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(AccessTools.Method(typeof(PanelHooks), name)
                ?? throw new MissingMethodException(typeof(PanelHooks).Name, name));
        }

        /// <summary>True when the panels may be customized: all hooks installed and the save folder known.</summary>
        private static bool Active()
        {
            var runtime = SaveManagerRuntime.Current;
            return runtime.Enabled && runtime.EnsureReady();
        }

        // ---- building -------------------------------------------------------------------------------------------

        /// <summary>The panel's save-manager layer when the panel can be customized, else null (the game's own panel).</summary>
        private static ManagedPanel Ui(LoadGamePanel panel)
        {
            return Active() ? ManagedPanel.For(panel) : null;
        }

        private static ManagedPanel Ui(SaveGamePanel panel)
        {
            return Active() ? ManagedPanel.For(panel) : null;
        }

        private static void Warn(string what, Exception ex)
        {
            SaveManagerPlugin.LogWarn(what + " failed; using the game's own list (" + ex.GetType().Name + ": " + ex.Message + ")");
        }

        // ---- LoadGamePanel ---------------------------------------------------------------------------------------

        private static bool LoadCreateSlot(LoadGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                ui.Refresh();
                return false;
            }
            catch (Exception ex)
            {
                Warn("Building the load list", ex);
                return true;
            }
        }

        private static bool LoadSetButtonNavigation(LoadGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                ui.ApplyNavigation();
                return false;
            }
            catch (Exception ex)
            {
                Warn("Load list navigation", ex);
                return true;
            }
        }

        private static bool LoadUpdateSlot(LoadGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                // The recent/auto rows are only ever set up here and in Refresh, as in the game.
                if (!Deferred.Contains(__instance)) ui.Refresh();
                else SlotListBuilder.SetupSidebar(__instance);
                return false;
            }
            catch (Exception ex)
            {
                Warn("Refreshing the load list", ex);
                return true;
            }
        }

        /// <summary>Before OnPanelOpen's own body (which selects the recent row and calls UpdateSlot): reconcile and rebuild.</summary>
        private static void LoadOnPanelOpen(LoadGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return;
                ui.Refresh();
                Deferred.Add(__instance);
            }
            catch (Exception ex)
            {
                Warn("Opening the load list", ex);
            }
        }

        /// <summary>After OnPanelOpen: the cursor goes to the right column's first row (the game picked the recent row).</summary>
        private static Exception LoadOnPanelOpenEnd(LoadGamePanel __instance, Exception __exception)
        {
            var deferred = Deferred.Remove(__instance);
            if (__exception != null || !deferred) return __exception;
            try
            {
                var ui = __instance.GetComponent<ManagedPanel>();
                if (ui != null) ui.SelectInitial();
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Selecting the first row failed: " + ex.Message);
            }
            return null;
        }

        /// <summary>Notes which row is being deleted (to select its neighbour afterwards) and defers the game's refresh.</summary>
        private static void LoadDeleteBegin(LoadGamePanel __instance, out int __state)
        {
            __state = -1;
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return;
                __state = ui.IndexOfKey(DeleteSlot(__instance));
                Deferred.Add(__instance);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Delete bookkeeping (panel) failed: " + ex.Message);
            }
        }

        /// <summary>After ConfirmDeleteSlot: the deleted save leaves the list (and the new-game row may move).</summary>
        private static void LoadAfterDelete(LoadGamePanel __instance, int __state)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return;
                ui.Refresh();
                if (__state >= 0) ui.SelectAfterRemoval(__state);
                else SlotListBuilder.KeepSelectionAlive(__instance);
            }
            catch (Exception ex)
            {
                Warn("Refreshing the load list after a delete", ex);
            }
        }

        private static Exception LoadEndDefer(LoadGamePanel __instance, Exception __exception)
        {
            Deferred.Remove(__instance);
            return __exception;
        }

        // ---- SaveGamePanel ---------------------------------------------------------------------------------------

        private static bool SaveCreateSlot(SaveGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                ui.Refresh();
                return false;
            }
            catch (Exception ex)
            {
                Warn("Building the save list", ex);
                return true;
            }
        }

        private static bool SaveSetButtonNavigation(SaveGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                ui.ApplyNavigation();
                return false;
            }
            catch (Exception ex)
            {
                Warn("Save list navigation", ex);
                return true;
            }
        }

        private static bool SaveUpdateSlot(SaveGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return true;
                if (!Deferred.Contains(__instance)) ui.Refresh();
                return false;
            }
            catch (Exception ex)
            {
                Warn("Refreshing the save list", ex);
                return true;
            }
        }

        /// <summary>
        /// Before OnPanelOpen's own body (which selects row 0 and calls UpdateSlot): the sidebar selects the category of
        /// the save being played, then the list is rebuilt.
        /// </summary>
        private static void SaveOnPanelOpen(SaveGamePanel __instance)
        {
            try
            {
                var ui = Ui(__instance);
                if (ui == null) return;
                var system = GameAccess.FindSaveSystem() as SaveSystem;
                var before = ui.Controller.Selected;
                ui.Controller.SelectCategoryOf(system == null ? null : system.CurrentSlot);
                if (ui.Controller.Selected == before) ui.Refresh();
                Deferred.Add(__instance);
            }
            catch (Exception ex)
            {
                Warn("Opening the save list", ex);
            }
        }

        /// <summary>
        /// OnPanelOpen's body selects _saveSlots[0] unguarded. The list is only empty when every slot 001..9999 is
        /// taken and the category has no saves; that IndexOutOfRange is the only exception swallowed here.
        /// </summary>
        private static Exception SaveOnPanelOpenEnd(SaveGamePanel __instance, Exception __exception)
        {
            var original = __exception;
            try
            {
                var deferred = Deferred.Remove(__instance);
                if (__exception is IndexOutOfRangeException && SaveManagerRuntime.Current.Enabled && SlotListBuilder.Count(__instance) == 0)
                    __exception = null;
                if (__exception != null || !deferred) return __exception;
                try
                {
                    var ui = __instance.GetComponent<ManagedPanel>();
                    if (ui != null) ui.SelectInitial();
                }
                catch (Exception ex)
                {
                    SaveManagerPlugin.LogWarn("Selecting the first row failed: " + ex.Message);
                }
                return null;
            }
            catch (Exception ex)
            {
                // A finalizer must never replace the game's own outcome with an exception of ours.
                SaveManagerPlugin.LogWarn("Finishing the save panel failed (" + ex.GetType().Name + ": " + ex.Message + ")");
                return original;
            }
        }

        private static void SaveDefer(SaveGamePanel __instance)
        {
            if (Active()) Deferred.Add(__instance);
        }

        /// <summary>After SaveData (an empty row): the new save joins the list, gets selected, and the naming dialog opens.</summary>
        private static void SaveAfterSaveData(SaveGamePanel __instance, string __0)
        {
            AfterWrite(__instance, __0);
        }

        /// <summary>Before ConfirmSaveSlot (overwrite after the game's own question): remembers the slot, defers the refresh.</summary>
        private static void SaveConfirmBegin(SaveGamePanel __instance, out string __state)
        {
            __state = null;
            try
            {
                if (!Active()) return;
                __state = ConfirmSlot(__instance);
                Deferred.Add(__instance);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Overwrite bookkeeping (panel) failed: " + ex.Message);
            }
        }

        /// <summary>After ConfirmSaveSlot: the overwritten save keeps its category; the naming dialog opens with its old name.</summary>
        private static void SaveAfterConfirm(SaveGamePanel __instance, string __state)
        {
            AfterWrite(__instance, __state);
        }

        private static void AfterWrite(SaveGamePanel panel, string key)
        {
            try
            {
                var ui = Ui(panel);
                if (ui == null) return;
                ui.Refresh();
                ui.AfterSave(key);
            }
            catch (Exception ex)
            {
                Warn("Refreshing the save list after a save", ex);
            }
        }

        private static Exception SaveEndDefer(SaveGamePanel __instance, Exception __exception)
        {
            Deferred.Remove(__instance);
            return __exception;
        }
    }
}
