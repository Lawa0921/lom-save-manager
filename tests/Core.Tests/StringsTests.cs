using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LoMSaveManager.Tests
{
    internal static class StringsTests
    {
        private const string Traditional = "ChineseTraditional";
        private const string Simplified = "ChineseSimplified";

        // Every key of the approved UI copy table, except the five validation
        // errors: those are the SaveCatalog.ErrorKeys values and have their own test.
        private static readonly string[] ApprovedKeys =
        {
            "Category.Header", "Category.Recent", "Category.Auto", "Category.Group.Mine",
            "Category.Uncategorized", "Category.Add", "Category.Manage", "Category.Manage.Done",
            "Category.Manage.Hint", "Category.MoveUp", "Category.MoveDown", "Category.Rename",
            "Category.Delete", "Category.ConfirmDelete", "Category.Count", "Category.Empty",
            "Category.Empty.HintLoad", "Category.Empty.HintSave",
            "Sort.NewestFirst", "Sort.OldestFirst",
            "Slot.Unnamed", "Slot.Rename", "Slot.Move", "Slot.NewInCategory", "Slot.NewInCategory.Sub",
            "Slot.NewGame", "Slot.NewGame.Sub", "Slot.Full", "Slot.Full.Hint",
            "Name.Title.Save", "Name.Title.CategoryNew", "Name.Title.CategoryRename", "Name.Placeholder.Save",
            "Name.Placeholder.Category", "Name.Hint.Save", "Name.Ok", "Name.Cancel", "Name.Skip",
            "Move.Title", "Move.Current",
            "Toast.Moved",
            "Error.Disabled", "Error.ReadOnly",
        };

        // Keys removed from the UI copy. Name.Title.Category became Name.Title.CategoryNew / Name.Title.CategoryRename.
        private static readonly string[] DroppedKeys = { "Name.Title.Category" };

        // How many {n} arguments each template takes. Every key not listed here takes none.
        private static readonly Dictionary<string, int> ArgumentCounts = new Dictionary<string, int>
        {
            { "Category.ConfirmDelete", 2 },
            { "Category.Count", 1 },
            { "Slot.NewInCategory", 1 },
            { "Slot.Full", 1 },
            { "Toast.Moved", 1 },
        };

        // Characters that only exist in Traditional Chinese. None of them may survive in a Simplified string.
        // 覆 is deliberately absent: it is also Simplified (覆盖). Its Traditional partner is 蓋, which is listed.
        private const string TraditionalOnlyChars = "類檔個據這來裡後從會開關頁題輸確稱數項選擇無錄對為與點載讀蓋複製帶間動刪預設庫過還遊戲態將體額讓應該請";

        private static char[] TraditionalOnlyIn(string text)
        {
            return text.Where(c => TraditionalOnlyChars.IndexOf(c) >= 0).ToArray();
        }

        // True when the digits of number appear as a whole number: 24 is in "最多 24 個字" but not in "240" or "124".
        private static bool ContainsWholeNumber(string text, int number)
        {
            return Regex.IsMatch(text, "(?<!\\d)" + number + "(?!\\d)");
        }

        private static void Expect(string key, string traditional, string simplified)
        {
            Check.Equal(traditional, Strings.Get(key, Traditional));
            Check.Equal(simplified, Strings.Get(key, Simplified));
        }

        public static void Run(Action<string, Action> test)
        {
            test("every_key_exists_in_both_languages", () =>
            {
                Check.True(Strings.Keys.Count > 0);
                foreach (var key in Strings.Keys)
                {
                    Check.False(string.IsNullOrWhiteSpace(Strings.Get(key, Traditional)));
                    Check.False(string.IsNullOrWhiteSpace(Strings.Get(key, Simplified)));
                }
            });

            test("keys_are_unique", () =>
            {
                Check.Equal(Strings.Keys.Count, Strings.Keys.Distinct().Count());
            });

            test("required_keys_are_present", () =>
            {
                var missing = ApprovedKeys.Where(k => !Strings.Keys.Contains(k)).ToList();
                if (missing.Count > 0) throw new Exception("missing keys: " + string.Join(", ", missing));
            });

            test("dropped_keys_are_gone", () =>
            {
                foreach (var key in DroppedKeys) Check.False(Strings.Keys.Contains(key));
            });

            test("every_savecatalog_error_key_has_a_string", () =>
            {
                var errorKeys = typeof(SaveCatalog.ErrorKeys)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.IsLiteral)
                    .Select(f => (string)f.GetRawConstantValue())
                    .ToList();
                Check.True(errorKeys.Count == 6);
                foreach (var key in errorKeys)
                {
                    Check.True(Strings.Keys.Contains(key));
                    Check.False(string.IsNullOrWhiteSpace(Strings.Get(key, Traditional)));
                    Check.False(string.IsNullOrWhiteSpace(Strings.Get(key, Simplified)));
                }
            });

            test("limit_number_match_is_whole_number_only", () =>
            {
                Check.True(ContainsWholeNumber("分類名稱最多 24 個字", 24));
                Check.True(ContainsWholeNumber("最多24個字", 24));
                Check.False(ContainsWholeNumber("最多 240 個字", 24));
                Check.False(ContainsWholeNumber("最多 124 個字", 24));
                Check.False(ContainsWholeNumber("最多 4 個字", 24));
                Check.False(ContainsWholeNumber("最多 24 個字", 4));
            });

            test("limit_messages_state_the_real_limits", () =>
            {
                foreach (var lang in new[] { Traditional, Simplified })
                {
                    Check.True(ContainsWholeNumber(Strings.Get(SaveCatalog.ErrorKeys.CategoryNameTooLong, lang), SaveCatalog.MaxCategoryNameLength));
                    Check.True(ContainsWholeNumber(Strings.Get(SaveCatalog.ErrorKeys.SlotNameTooLong, lang), SaveCatalog.MaxSlotNameLength));
                    Check.True(ContainsWholeNumber(Strings.Get(SaveCatalog.ErrorKeys.CategoryLimit, lang), SaveCatalog.MaxCategories));
                }
            });

            test("unknown_language_falls_back_to_traditional", () =>
            {
                foreach (var key in Strings.Keys)
                {
                    var expected = Strings.Get(key, Traditional);
                    Check.Equal(expected, Strings.Get(key, "Korean"));
                    Check.Equal(expected, Strings.Get(key, "English"));
                    Check.Equal(expected, Strings.Get(key, ""));
                    Check.Equal(expected, Strings.Get(key, null));
                }
            });

            test("language_names_are_case_sensitive_exact_matches", () =>
            {
                Check.Equal(Strings.Get("Category.Recent", Traditional), Strings.Get("Category.Recent", "chinesesimplified"));
                Check.False(Strings.Get("Category.Recent", Traditional) == Strings.Get("Category.Recent", Simplified));
            });

            test("unknown_key_throws", () =>
            {
                Check.Throws<KeyNotFoundException>(() => Strings.Get("No.Such.Key", Traditional));
                Check.Throws<KeyNotFoundException>(() => Strings.Get("No.Such.Key", Simplified));
                Check.Throws<KeyNotFoundException>(() => Strings.Get("", Traditional));
                Check.Throws<KeyNotFoundException>(() => Strings.Get("category.header", Traditional));
                Check.Throws<KeyNotFoundException>(() => Strings.Get("Name.Title.Category", Traditional));
            });

            test("traditional_only_denylist_accepts_valid_simplified_overwrite", () =>
            {
                // 覆盖 is Simplified; 覆蓋 is its Traditional form, caught by 蓋.
                Check.Equal(0, TraditionalOnlyIn("覆盖").Length);
                Check.Equal(0, TraditionalOnlyIn("直接覆盖既有存档").Length);
                Check.Equal("蓋", new string(TraditionalOnlyIn("覆蓋")));
            });

            test("simplified_strings_contain_no_traditional_only_characters", () =>
            {
                foreach (var key in Strings.Keys)
                {
                    var text = Strings.Get(key, Simplified);
                    var bad = TraditionalOnlyIn(text);
                    if (bad.Length > 0) throw new Exception(key + " has Traditional characters: " + new string(bad));
                }
            });

            test("known_keys_use_proper_simplified_text", () =>
            {
                Check.Equal("存档分类", Strings.Get("Category.Header", Simplified));
                Check.Equal("最近存档", Strings.Get("Category.Recent", Simplified));
                Check.Equal("自动存档", Strings.Get("Category.Auto", Simplified));
                Check.Equal("未分类", Strings.Get("Category.Uncategorized", Simplified));
                Check.Equal("存檔分類", Strings.Get("Category.Header", Traditional));
                Check.Equal("最近存檔", Strings.Get("Category.Recent", Traditional));
            });

            test("revised_texts_match_the_approved_copy", () =>
            {
                Expect("Category.Header", "存檔分類", "存档分类");
                Expect("Category.Rename", "改名", "改名");
                Expect("Category.Delete", "刪除", "删除");
                Expect(
                    "Category.ConfirmDelete",
                    "刪除分類「{0}」？\n裡面的 {1} 筆存檔會移到「未分類」，存檔本身不會被刪除。",
                    "删除分类“{0}”？\n里面的 {1} 笔存档会移到“未分类”，存档本身不会被删除。");
                Expect("Name.Title.CategoryNew", "新增分類", "新增分类");
            });

            test("new_texts_match_the_approved_copy", () =>
            {
                Expect("Category.Group.Mine", "我的分類", "我的分类");
                Expect("Category.Manage.Done", "完成", "完成");
                Expect("Category.Count", "{0} 筆", "{0} 笔");
                Expect("Category.Empty", "這個分類還沒有存檔", "这个分类还没有存档");
                Expect("Sort.NewestFirst", "存檔時間：新→舊", "存档时间：新→旧");
                Expect("Sort.OldestFirst", "存檔時間：舊→新", "存档时间：旧→新");
                Expect("Slot.NewInCategory.Sub", "新增一筆存檔", "新增一笔存档");
                Expect("Slot.NewGame", "＋ 開始新的旅程", "＋ 开始新的旅程");
                Expect("Slot.NewGame.Sub", "從頭開始一局新遊戲", "从头开始一局新游戏");
                Expect("Slot.Full.Hint", "刪除用不到的存檔，或直接覆蓋既有存檔。", "删除用不到的存档，或直接覆盖既有存档。");
                Expect("Name.Title.CategoryRename", "分類改名", "分类改名");
                Expect("Name.Hint.Save", "可以留白，之後再用「命名」補上。", "可以留空，之后再用“命名”补上。");
                Expect("Name.Skip", "略過", "跳过");
                Expect("Move.Title", "移到哪個分類？", "移到哪个分类？");
                Expect("Move.Current", "目前所在", "当前所在");
                Expect("Toast.Moved", "已移到「{0}」", "已移到“{0}”");
                Expect("Error.ReadOnly", "本次啟動無法儲存名稱與分類，暫不能修改；遊戲存檔不受影響。", "本次启动无法保存名称与分类，暂不能修改；游戏存档不受影响。");
            });

            test("unchanged_texts_keep_their_content", () =>
            {
                Expect("Category.Recent", "最近存檔", "最近存档");
                Expect("Category.Auto", "自動存檔", "自动存档");
                Expect("Category.Uncategorized", "未分類", "未分类");
                Expect("Category.Add", "＋ 新增分類", "＋ 新增分类");
                Expect("Category.Manage", "管理分類", "管理分类");
                Expect("Slot.Unnamed", "（未命名）", "（未命名）");
                Expect("Slot.Rename", "命名", "命名");
                Expect("Slot.Move", "移動", "移动");
                Expect("Slot.NewInCategory", "＋ 存到「{0}」", "＋ 存到“{0}”");
                Expect("Slot.Full", "存檔數已達上限 {0}", "存档数已达上限 {0}");
                Expect("Name.Title.Save", "為存檔命名", "为存档命名");
                Expect("Name.Ok", "確定", "确定");
                Expect("Name.Cancel", "取消", "取消");
                Expect("Error.Disabled", "存檔管理功能無法使用，已改用原作介面。", "存档管理功能无法使用，已改用原作界面。");
            });

            test("format_strings_take_their_arguments", () =>
            {
                foreach (var lang in new[] { Traditional, Simplified })
                {
                    foreach (var key in Strings.Keys)
                    {
                        var template = Strings.Get(key, lang);
                        int count;
                        if (!ArgumentCounts.TryGetValue(key, out count)) count = 0;

                        // The {n} placeholders are exactly {0}..{count-1}; no more, no fewer, none repeated under another number.
                        var used = Regex.Matches(template, "\\{(\\d+)\\}").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToArray();
                        Check.Equal(string.Join(",", Enumerable.Range(0, count)), string.Join(",", used));

                        // And the template is valid for string.Format: every argument shows up, nothing throws.
                        var args = Enumerable.Range(0, count).Select(n => (object)("ARG" + n + "X")).ToArray();
                        var text = string.Format(template, args);
                        foreach (var arg in args) Check.True(text.Contains((string)arg));
                    }
                }
            });
        }
    }
}
