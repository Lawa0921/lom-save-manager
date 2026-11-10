using System;
using System.Collections.Generic;
using System.Linq;

namespace LoMSaveManager
{
    /// <summary>
    /// A read-only snapshot of the save manager's state, for dev-only test tooling (not part of this repository; command
    /// mod_state), which finds it by reflection. Nothing here changes any state.
    /// </summary>
    public static class SaveManagerDiagnostics
    {
        public static Dictionary<string, object> Snapshot()
        {
            var runtime = SaveManagerRuntime.Current;
            var result = new Dictionary<string, object>
            {
                { "enabled", runtime.Enabled },
                { "ready", runtime.IsReady },
                { "pending_category", runtime.PendingCategoryId },
                { "language", ManagedPanel.Language() },
            };
            try
            {
                if (runtime.IsReady)
                {
                    var catalog = runtime.Catalog;
                    result["newest_first"] = catalog.NewestFirst;
                    result["categories"] = catalog.UserCategories()
                        .Select(c => (object)new Dictionary<string, object> { { "id", c.Id }, { "name", c.Name }, { "count", c.Count } })
                        .ToList();
                    var saves = runtime.ScanManualSaves().Keys.OrderBy(k => k, SlotKey.NumericOrder)
                        .Select(k => (object)new Dictionary<string, object> { { "key", k }, { "name", catalog.NameOf(k) }, { "category", catalog.CategoryOf(k) } })
                        .ToList();
                    result["saves"] = saves;
                }
                result["panels"] = ManagedPanel.All.Select(p => (object)p.Snapshot()).ToList();
                var timing = SlotListBuilder.LastTiming;
                result["last_rebuild"] = timing == null ? null : timing.ToDictionary();
                result["last_refresh_ms"] = ManagedPanel.LastRefreshMs;
            }
            catch (Exception ex)
            {
                result["error"] = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }
    }
}
