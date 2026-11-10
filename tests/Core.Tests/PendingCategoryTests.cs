using System;

namespace LoMSaveManager.Tests
{
    /// <summary>Where a brand-new save goes: only the open panel that set it may name a category, and closing clears it.</summary>
    internal static class PendingCategoryTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("pending_category_is_cleared_when_its_panel_closes", () =>
            {
                var pending = new PendingCategory();
                var loadPanel = new object();
                var savePanel = new object();
                Check.Null(pending.Id);

                pending.Set(loadPanel, "main");
                Check.Equal("main", pending.Id);

                // A panel that did not set it cannot clear it.
                pending.Clear(savePanel);
                Check.Equal("main", pending.Id);

                // Closing the panel that set it: a new game started from the title (TitleManager.NewGame) or a
                // quick save outside any panel is uncategorized again.
                pending.Clear(loadPanel);
                Check.Null(pending.Id);

                pending.Set(loadPanel, "main");
                pending.Set(savePanel, null);
                Check.Null(pending.Id);
                pending.Clear(loadPanel);
                pending.Set(savePanel, "side");
                Check.Equal("side", pending.Id);
                pending.Clear(savePanel);
                Check.Null(pending.Id);
                Check.Throws<ArgumentNullException>(() => pending.Set(null, "x"));
            });
        }
    }
}
