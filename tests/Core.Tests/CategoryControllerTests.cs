using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoMSaveManager.Tests
{
    /// <summary>The rest of the UI-facing CategoryController behaviour (selection, ordering, sorting, errors).</summary>
    internal static class CategoryControllerTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("controller_starts_on_recent_and_selects_only_known_ids", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var controller = new CategoryController(catalog, () => { });
                Check.Equal(CategoryController.Recent, controller.Selected);
                Check.False(controller.ShowsSlotList);
                Check.Null(controller.PendingCategoryId);

                controller.Select(CategoryController.Auto);
                Check.False(controller.ShowsSlotList);
                controller.Select(CategoryController.Uncategorized);
                Check.True(controller.ShowsSlotList);
                Check.Null(controller.PendingCategoryId);
                Check.Null(controller.SelectedCatalogId);

                // An id that does not exist (deleted elsewhere, stale UI) falls back to 未分類.
                controller.Select("gone");
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
                Check.Throws<ArgumentNullException>(() => controller.Select(null));

                // Selecting the same id again does not raise Changed.
                var changed = 0;
                controller.Changed += () => changed++;
                controller.Select(CategoryController.Uncategorized);
                Check.Equal(0, changed);
            });

            test("controller_selects_the_category_of_a_save", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(Files("001", "002"));
                var controller = new CategoryController(catalog, () => { });
                var id = controller.Create("A");
                controller.Move("002", id);

                controller.SelectCategoryOf("002");
                Check.Equal(id, controller.Selected);
                controller.SelectCategoryOf("001");
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
                controller.SelectCategoryOf("auto");
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
                controller.SelectCategoryOf(null);
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
            });

            test("controller_validate_drops_a_vanished_selection", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var controller = new CategoryController(catalog, () => { });
                var id = controller.Create("A");
                controller.Select(id);
                catalog.DeleteCategory(id); // e.g. the other panel deleted it
                controller.Validate();
                Check.Equal(CategoryController.Uncategorized, controller.Selected);
            });

            test("controller_reorders_with_move_up_and_down", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                var persisted = 0;
                var controller = new CategoryController(catalog, () => persisted++);
                var a = controller.Create("A");
                var b = controller.Create("B");
                var c = controller.Create("C");

                controller.MoveUp(c);
                Check.Equal("A,C,B", Names(catalog));
                controller.MoveDown(a);
                Check.Equal("C,A,B", Names(catalog));
                // Already at the edge: nothing changes and nothing is written.
                var before = persisted;
                controller.MoveUp(c);
                controller.MoveDown(b);
                Check.Equal("C,A,B", Names(catalog));
                Check.Equal(before, persisted);
                Check.False(controller.CanMoveUp(c));
                Check.True(controller.CanMoveDown(c));
                Check.False(controller.CanMoveDown(b));
                Check.False(controller.CanMoveUp(CategoryController.Uncategorized));
            });

            test("controller_validation_errors_carry_the_catalog_error_keys", () =>
            {
                var catalog = new SaveCatalog(new SaveIndex());
                catalog.Reconcile(Files("001"));
                var persisted = 0;
                var changed = 0;
                var controller = new CategoryController(catalog, () => persisted++);
                controller.Changed += () => changed++;

                ThrowsKey(SaveCatalog.ErrorKeys.CategoryNameEmpty, () => controller.Create("  "));
                ThrowsKey(SaveCatalog.ErrorKeys.CategoryNameTooLong, () => controller.Create(new string('字', 25)));
                ThrowsKey(SaveCatalog.ErrorKeys.SlotNameTooLong, () => controller.Name("001", new string('字', 41)));
                ThrowsKey(SaveCatalog.ErrorKeys.CategoryNotFound, () => controller.Move("001", "gone"));
                ThrowsKey(SaveCatalog.ErrorKeys.CategoryNotFound, () => controller.Rename("gone", "x"));
                Check.Equal(0, persisted);
                Check.Equal(0, changed);

                // An empty save name clears it.
                controller.Name("001", "n");
                controller.Name("001", "   ");
                Check.Null(catalog.NameOf("001"));
                Check.Equal(2, persisted);
            });

            test("controller_sort_toggle_is_persisted_in_the_index", () =>
            {
                var index = new SaveIndex();
                var catalog = new SaveCatalog(index);
                var persisted = 0;
                var controller = new CategoryController(catalog, () => persisted++);
                Check.True(controller.NewestFirst);

                controller.ToggleSort();
                Check.False(controller.NewestFirst);
                Check.False(index.NewestFirst);
                Check.Equal(1, persisted);
                controller.ToggleSort();
                Check.True(index.NewestFirst);
            });

            test("session_list_keys_follows_the_sort_preference", () => WithFolder(folder =>
            {
                Touch(folder, "Save_001.dat", 2020);
                Touch(folder, "Save_002.dat", 2024);
                Touch(folder, "Save_003.dat", 2022);
                var session = SaveSession.Open(folder, w => { });
                Check.Equal("002,003,001", string.Join(",", session.ListKeys(null).Keys));

                session.Catalog.NewestFirst = false;
                session.Persist();
                Check.Equal("001,003,002", string.Join(",", session.ListKeys(null).Keys));

                var reopened = SaveSession.Open(folder, w => { });
                Check.False(reopened.Catalog.NewestFirst);
                Check.Equal("001,003,002", string.Join(",", reopened.ListKeys(null).Keys));
            }));
        }

        private static void ThrowsKey(string key, Action action)
        {
            try { action(); }
            catch (ArgumentException ex)
            {
                Check.Equal(key, ex.Message);
                return;
            }
            throw new Exception("expected ArgumentException " + key);
        }

        private static string Names(SaveCatalog catalog)
        {
            return string.Join(",", catalog.UserCategories().Select(c => c.Name));
        }

        private static Dictionary<string, SaveStamp> Files(params string[] keys)
        {
            var files = new Dictionary<string, SaveStamp>();
            foreach (var key in keys) files[key] = TestStamps.S(100, 1);
            return files;
        }

        private static void Touch(string folder, string name, int year)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, name);
            File.SetLastWriteTimeUtc(path, new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        private static void WithFolder(Action<string> body)
        {
            var folder = Path.Combine(Path.GetTempPath(), "LoMSaveManagerTests-ctl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try { body(folder); }
            finally { Directory.Delete(folder, true); }
        }
    }
}
