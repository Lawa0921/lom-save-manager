using System;
using System.Collections.Generic;

namespace LoMSaveManager
{
    /// <summary>
    /// Every user-facing string, in Traditional and Simplified Chinese. The table in <see cref="Entries"/> is the
    /// only place to edit copy. The keys of <see cref="SaveCatalog.ErrorKeys"/> are part of it, so a validation
    /// failure thrown by <see cref="SaveCatalog"/> can always be shown as text.
    /// </summary>
    public static class Strings
    {
        /// <summary>Lean Localization language names.</summary>
        public const string ChineseTraditional = "ChineseTraditional";

        public const string ChineseSimplified = "ChineseSimplified";

        // Key, Traditional, Simplified. This is the approved UI copy table, in its
        // original order. Texts with {0}, {1} are string.Format templates; the caller supplies the arguments.
        // The numbers in the three *TooLong / *Limit messages repeat SaveCatalog's limits (a test checks they agree).
        private static readonly string[][] Entries =
        {
            // Sidebar
            new[] { "Category.Header", "存檔分類", "存档分类" },
            new[] { "Category.Recent", "最近存檔", "最近存档" },
            new[] { "Category.Auto", "自動存檔", "自动存档" },
            new[] { "Category.Group.Mine", "我的分類", "我的分类" },
            new[] { "Category.Uncategorized", "未分類", "未分类" },
            new[] { "Category.Add", "＋ 新增分類", "＋ 新增分类" },
            new[] { "Category.Manage", "管理分類", "管理分类" },
            new[] { "Category.Manage.Done", "完成", "完成" },
            new[] { "Category.Manage.Hint", "用 ▲▼ 調整順序。最近存檔、自動存檔、未分類固定不動。", "用 ▲▼ 调整顺序。最近存档、自动存档、未分类固定不动。" },
            new[] { "Category.MoveUp", "▲", "▲" },
            new[] { "Category.MoveDown", "▼", "▼" },
            new[] { "Category.Rename", "改名", "改名" },
            new[] { "Category.Delete", "刪除", "删除" },
            new[] { "Category.ConfirmDelete", "刪除分類「{0}」？\n裡面的 {1} 筆存檔會移到「未分類」，存檔本身不會被刪除。", "删除分类“{0}”？\n里面的 {1} 笔存档会移到“未分类”，存档本身不会被删除。" },

            // Category contents
            new[] { "Category.Count", "{0} 筆", "{0} 笔" },
            new[] { "Category.Empty", "這個分類還沒有存檔", "这个分类还没有存档" },
            new[] { "Category.Empty.HintLoad", "在「儲存遊戲」選這個分類就能存進來，也可以用「移動」把其他存檔放進來。", "在“储存游戏”选这个分类就能存进来，也可以用“移动”把其他存档放进来。" },
            new[] { "Category.Empty.HintSave", "選上方的「＋ 存到…」，存下這個分類的第一筆存檔。", "选上方的“＋ 存到…”，存下这个分类的第一笔存档。" },
            new[] { "Sort.NewestFirst", "存檔時間：新→舊", "存档时间：新→旧" },
            new[] { "Sort.OldestFirst", "存檔時間：舊→新", "存档时间：旧→新" },

            // Save slots
            new[] { "Slot.Unnamed", "（未命名）", "（未命名）" },
            new[] { "Slot.Rename", "命名", "命名" },
            new[] { "Slot.Move", "移動", "移动" },
            new[] { "Slot.NewInCategory", "＋ 存到「{0}」", "＋ 存到“{0}”" },
            new[] { "Slot.NewInCategory.Sub", "新增一筆存檔", "新增一笔存档" },
            new[] { "Slot.NewGame", "＋ 開始新的旅程", "＋ 开始新的旅程" },
            new[] { "Slot.NewGame.Sub", "從頭開始一局新遊戲", "从头开始一局新游戏" },
            new[] { "Slot.Full", "存檔數已達上限 {0}", "存档数已达上限 {0}" },
            new[] { "Slot.Full.Hint", "刪除用不到的存檔，或直接覆蓋既有存檔。", "删除用不到的存档，或直接覆盖既有存档。" },

            // Name dialog
            new[] { "Name.Title.Save", "為存檔命名", "为存档命名" },
            new[] { "Name.Title.CategoryNew", "新增分類", "新增分类" },
            new[] { "Name.Title.CategoryRename", "分類改名", "分类改名" },
            new[] { "Name.Placeholder.Save", "例如：第三年春・告白前", "例如：第三年春・告白前" },
            new[] { "Name.Placeholder.Category", "例如：主線・二周目", "例如：主线・二周目" },
            new[] { "Name.Hint.Save", "可以留白，之後再用「命名」補上。", "可以留空，之后再用“命名”补上。" },
            new[] { "Name.Ok", "確定", "确定" },
            new[] { "Name.Cancel", "取消", "取消" },
            new[] { "Name.Skip", "略過", "跳过" },

            // Move dialog and toast
            new[] { "Move.Title", "移到哪個分類？", "移到哪个分类？" },
            new[] { "Move.Current", "目前所在", "当前所在" },
            new[] { "Toast.Moved", "已移到「{0}」", "已移到“{0}”" },

            // Validation errors (SaveCatalog.ErrorKeys)
            new[] { SaveCatalog.ErrorKeys.CategoryNameEmpty, "分類名稱不能空白", "分类名称不能为空" },
            new[] { SaveCatalog.ErrorKeys.CategoryNameTooLong, "分類名稱最多 24 個字", "分类名称最多 24 个字" },
            new[] { SaveCatalog.ErrorKeys.SlotNameTooLong, "存檔名稱最多 40 個字", "存档名称最多 40 个字" },
            new[] { SaveCatalog.ErrorKeys.CategoryLimit, "分類數量已達上限 200", "分类数量已达上限 200" },
            new[] { SaveCatalog.ErrorKeys.CategoryNotFound, "找不到這個分類", "找不到这个分类" },
            new[] { SaveCatalog.ErrorKeys.SaveNotScanned, "還讀不到這個存檔，請重新開啟列表再試", "还读不到这个存档，请重新打开列表再试" },

            // Plugin state
            new[] { "Error.Disabled", "存檔管理功能無法使用，已改用原作介面。", "存档管理功能无法使用，已改用原作界面。" },
            new[] { CategoryController.ReadOnlyErrorKey, "本次啟動無法儲存名稱與分類，暫不能修改；遊戲存檔不受影響。", "本次启动无法保存名称与分类，暂不能修改；游戏存档不受影响。" },
        };

        private static readonly Dictionary<string, string[]> Table = BuildTable();

        /// <summary>Every key in the table, in table order.</summary>
        public static readonly IReadOnlyList<string> Keys = BuildKeys();

        /// <summary>
        /// The text for <paramref name="key"/> in <paramref name="leanLanguage"/> (a Lean Localization language name).
        /// Only <c>ChineseSimplified</c> selects Simplified; every other language, including null, gets Traditional.
        /// An unknown key throws <see cref="KeyNotFoundException"/> so a missing entry shows up in tests.
        /// </summary>
        public static string Get(string key, string leanLanguage)
        {
            if (key == null) throw new KeyNotFoundException("Strings key is null");

            string[] entry;
            if (!Table.TryGetValue(key, out entry)) throw new KeyNotFoundException("No string for key: " + key);

            return string.Equals(leanLanguage, ChineseSimplified, StringComparison.Ordinal) ? entry[2] : entry[1];
        }

        private static Dictionary<string, string[]> BuildTable()
        {
            var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var entry in Entries) table.Add(entry[0], entry);
            return table;
        }

        private static IReadOnlyList<string> BuildKeys()
        {
            var keys = new List<string>(Entries.Length);
            foreach (var entry in Entries) keys.Add(entry[0]);
            return keys.AsReadOnly();
        }
    }
}
