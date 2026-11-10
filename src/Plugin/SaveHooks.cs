using System;
using HarmonyLib;
using Mortal.Core;

namespace LoMSaveManager
{
    /// <summary>
    /// Bookkeeping around the game's own save and delete. The game still does all the work: nothing here replaces,
    /// skips or alters a SaveSystem call. Every prefix returns void (a prefix that returned false would skip the
    /// original), every hook body is wrapped so a failure is only logged, and the hooks are installed by hand
    /// (no [HarmonyPatch] attributes) after <see cref="HookCatalog"/> has validated the whole game.
    /// </summary>
    internal static class SaveHooks
    {
        public static void Install(Harmony harmony, HookCatalog catalog)
        {
            // Resolve the hook methods before the first patch so a typo cannot leave a half-installed set.
            var before = Hook(nameof(BeforeSaveGameData));
            var after = Hook(nameof(AfterSaveGameData));
            var afterDelete = Hook(nameof(AfterDeleteSaveData));
            var afterNewGame = Hook(nameof(AfterNewGameData));

            harmony.Patch(catalog.SaveGameData, prefix: before, postfix: after);
            harmony.Patch(catalog.DeleteSaveData, postfix: afterDelete);
            harmony.Patch(catalog.NewGameData, postfix: afterNewGame);
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(AccessTools.Method(typeof(SaveHooks), name)
                ?? throw new MissingMethodException(typeof(SaveHooks).Name, name));
        }

        /// <summary>
        /// SaveSystem.SaveGameData(string slot), before the write: notes the slot's name and category while the old
        /// file is still on disk. The state travels to <see cref="AfterSaveGameData"/>.
        /// </summary>
        private static void BeforeSaveGameData(object __instance, string __0, out OwnWriteTicket __state)
        {
            __state = null;
            try
            {
                var runtime = SaveManagerRuntime.Current;
                if (runtime.EnsureReady(__instance)) __state = runtime.Session.BeforeSave(__0);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Save bookkeeping (before) failed for slot '" + __0 + "': " + ex.Message);
            }
        }

        /// <summary>SaveSystem.SaveGameData(string slot), after the write: records the new file stamp and persists the index.</summary>
        private static void AfterSaveGameData(string __0, OwnWriteTicket __state)
        {
            try
            {
                if (__state == null) return;
                var runtime = SaveManagerRuntime.Current;
                runtime.Session.AfterSave(__state, runtime.PendingCategoryId);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Save bookkeeping (after) failed for slot '" + __0 + "': " + ex.Message);
            }
        }

        /// <summary>SaveSystem.DeleteSaveData(string slot), after the delete: forgets the slot's name and category.</summary>
        private static void AfterDeleteSaveData(object __instance, string __0)
        {
            try
            {
                var runtime = SaveManagerRuntime.Current;
                if (runtime.EnsureReady(__instance)) runtime.Session.AfterDelete(__0);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("Delete bookkeeping failed for slot '" + __0 + "': " + ex.Message);
            }
        }

        /// <summary>
        /// SaveSystem.NewGameData(), after it wrote the new game into the current slot (a new game started from the
        /// load panel's empty row): files the save in the pending category (null = uncategorized). Postfix only.
        /// </summary>
        private static void AfterNewGameData(object __instance)
        {
            string slot = null;
            try
            {
                var runtime = SaveManagerRuntime.Current;
                if (!runtime.EnsureReady(__instance)) return;
                slot = ((SaveSystem)__instance).CurrentSlot;
                runtime.Session.AfterNewGame(slot, runtime.PendingCategoryId);
            }
            catch (Exception ex)
            {
                SaveManagerPlugin.LogWarn("New-game bookkeeping failed for slot '" + slot + "': " + ex.Message);
            }
        }
    }
}
