using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace LoMSaveManager
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class SaveManagerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.lom.savemanager";
        public const string PluginName = "LoM Save Manager";
        public const string PluginVersion = "0.1.2";

        private static ManualLogSource _log;

        internal static void LogInfo(string message)
        {
            if (_log != null) _log.LogInfo(message);
        }

        internal static void LogDebug(string message)
        {
            if (_log != null) _log.LogDebug(message);
        }

        internal static void LogWarn(string message)
        {
            if (_log != null) _log.LogWarning(message);
        }

        private void Awake()
        {
            _log = Logger;
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                Logger.LogError("LoM Save Manager disabled: unexpected error (" + ex + ")");
            }
        }

        private void Initialize()
        {
            // 1. Every game member must be present, or nothing is touched.
            var catalog = HookCatalog.Resolve();
            var missing = HookTargets.Validate(catalog.Targets);
            if (missing.Count > 0)
            {
                Logger.LogError(HookTargets.DisabledMessage(missing));
                return;
            }

            // 2. Read the index and reconcile it with the save folder now if the game can name that folder yet;
            //    otherwise the first hook (or panel) that runs does it.
            GameAccess.Bind(catalog);
            if (!SaveManagerRuntime.Current.EnsureReady())
                Logger.LogInfo("The save folder is not known yet; the save index loads on first use.");

            // 3. Patch.
            var harmony = new Harmony(PluginGuid);
            try
            {
                SaveHooks.Install(harmony, catalog);
                PanelHooks.Install(harmony, catalog);
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Logger.LogError("LoM Save Manager disabled: could not install hooks (" + ex.Message + ")");
                return;
            }

            SaveManagerRuntime.Current.Enabled = true;
            Logger.LogInfo(PluginName + " " + PluginVersion + " ready.");
        }
    }
}
