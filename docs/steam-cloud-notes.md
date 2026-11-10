# Steam Cloud 與 mod metadata 同步調查（2026-11-09，唯讀調查）

目標：讓存檔名稱和分類跟著 Steam Auto-Cloud 同步。本文件記錄調查到的事實、證據和據此做的設計決定，給想了解原理或寫類似 mod 的人參考。

## 1. App 1859910 的 Auto-Cloud 規則（已確認）

來源：Steam 用戶端的 `Steam\appcache\appinfo.vdf`（複製出來後解析；格式 magic `0x07564429`＝v29，key 走字串表）。
調查當時 app 1859910 的 header：`change_number 40716555`。`ufs` 段的完整內容：

```
"ufs": {
  "quota": 100000000,          // 100 MB
  "maxnumfiles": 2000,
  "savefiles": {
    "0": { "root": "WinAppDataLocalLow",
           "path": "Obb Studio/Mortal/{64BitSteamID}",
           "pattern": "*" }    // 沒有 "recursive"、沒有 "platforms"、沒有 "rootoverrides"
  }
}
```

結論：
- **存檔資料夾第一層的所有檔案都會同步**（pattern `*`），不分檔名、副檔名。
- **沒有 `recursive`，子資料夾不同步。** 這就是舊版放在 `LoMSaveManager/index.json`（子資料夾）的名稱和分類沒上雲的原因。
- 沒有 platforms 限制（所有平台）。
- 配額：100,000,000 bytes、2000 個檔案。每個手動存檔約 70–130 KB，所以 2000 個檔案的上限會比 100 MB 更早碰到（約 1960 個存檔），新增一個幾 KB 的 metadata 檔沒有配額疑慮。

### remotecache.vdf（`Steam\userdata\<AccountID>\1859910\remotecache.vdf`）
- 這是 Steam 記錄哪些檔已同步的清單。每個條目欄位：`root`、`size`、`localtime`、`time`、`remotetime`、`sha`（SHA-1）、`syncstate`、`persiststate`、`platformstosync2`。
- 調查當時清單裡有 `Legend_*.dat`、`Save_*.dat`、`Save_universe.dat/.json`，**沒有**子資料夾裡的 `LoMSaveManager/index.json`。`steam_autocloud.vdf` 是 Steam 自己的標記檔，不在清單裡。

## 2. mtime 會不會在下載後保留？（關鍵）

### 證據
用 `stat` 讀第一層每個檔的 mtime（含奈秒）和建立時間（birth），對照 remotecache：

| 檔案 | 本機 mtime | birth | remotecache localtime / time |
|---|---|---|---|
| 從雲端下載、之後沒改過的檔（`Legend_*.dat`、部分 `Save_*.dat`） | 全部是整數秒（奈秒部分 `.000000000`），而且是下載之前很久的舊時間 | 全部等於同一個時刻（第一次從雲端下載整個資料夾的時間） | localtime = mtime，time = mtime − 1 |
| 本機寫入的存檔 | 有次秒精度（例如 `….306745600`） | 等於 mtime | localtime ≈ mtime |

### 推論
- **Steam 下載時會把 mtime 設回雲端記錄的時間，但只有秒精度**（而且觀察到跟 `time` 差 1 秒，原因不明，可能是捨入）。不會設成下載當下時間。
- 所以：電腦 A 寫存檔時 `LastWriteTimeUtc.Ticks` 有次秒部分；電腦 B 下載後同一份檔的 ticks 只剩整數秒（甚至差 1 秒）。**任何以「大小＋修改時間 ticks 完全相等」辨識存檔的做法，換一台電腦一定對不上。**
- 如果只是把 metadata 搬到會同步的位置、不改辨識方式：電腦 B 開遊戲時會判定每個存檔都「被外部替換」→ 丟掉所有名稱和分類 → 寫回空的 metadata → 遊戲結束時上傳 → 電腦 A 下次下載後名稱也沒了。

### 內容不變
remotecache 的 `sha` 是內容 SHA-1；觀察到重新下載的存檔 SHA-1 與原檔相同。**檔案內容（bytes）跨機器穩定，mtime 不穩定。**

## 3. metadata 檔名的選擇

同步只需要「放在第一層」，檔名不必以 `Save_` 開頭。

### 遊戲（`Mortal.Core.SaveSystem`）
- 全部遊戲組件找 `GetFiles / EnumerateFiles / Directory.Delete`：**沒有任何地方列舉存檔資料夾**。`File.Delete` 只出現在 SaveSystem 的 `.tmp/.bak` 處理和 `DeleteSaveData`（只刪 `Save_<slot>.dat`）。
- 沒有呼叫 `SteamRemoteStorage`，同步完全由 Auto-Cloud 處理。
- 遊戲只用 `Save_<key>.dat`、`Legend_<nnn>.dat`，以及 `Save_universe.json`。**任何不是這些形式的檔名都不會被遊戲讀寫或刪除。**
- 避免 `Save_<something>.dat` 形式：遊戲把 slot key 原樣串進路徑，理論上任何 key 都能對到它（例如有人呼叫 `GetSaveData("meta")` 就會用 BinaryFormatter 去讀它而丟例外）。
- 其他會掃描存檔資料夾的 mod 通常比對 `Save_*.dat`。用 `.json` 副檔名（4 字元）可避開 Windows `*.dat` 3 字元副檔名的舊式前綴比對怪癖；名字不以 `Save_` 開頭的話連 `Save_*` 都不會列到。

### 本 mod
- `SlotFiles.OccupiedKeys` / `ScanManualSaves`（`src/Core/SlotAllocator.cs`）用 `Save_*` 列舉，再要求 `.dat/.dat.tmp/.dat.bak` 且 key 通過 `SlotKey.IsManual`。`LoMSaveManager.json` 不會被當成存檔。
- 寫入暫存（`.tmp`）和隔離的壞檔（`.corrupt-<ticks>`）放在第一層的話也會被同步，所以都放在 `LoMSaveManager/` 子資料夾（不同步）。

### 候選比較

| 候選 | 會同步 | 遊戲 | 掃描 `Save_*.dat` 的 mod | 本 mod | 評語 |
|---|---|---|---|---|---|
| `LoMSaveManager.json` | 是 | 無關 | 不會被列到 | 不符 `Save_*` | **採用**：一看就知道是誰的檔 |
| `Save_LoMSaveManager.json` | 是 | 無關 | 被 `Save_*` 列到但 `.json` 不符 `*.dat` | 被過濾 | 可用 |
| `Save_meta.dat` | 是 | **有風險**（形狀等於一個 key 為 `meta` 的存檔） | 依賴 key 規則過濾 | 被過濾 | 不建議 |
| `LoMSaveManager/` 子資料夾 | **否**（非 recursive） | — | — | — | 舊版位置，不同步 |

## 4. 衝突語意與風險

- Auto-Cloud 以整個檔案為單位：遊戲結束時上傳有變動的檔，啟動前下載雲端較新的檔；遊戲執行中刪除的檔會同步刪除（見 `docs/game-internals.md` 1.2）。兩台機器都離線改過時，Steam 跳出衝突視窗，使用者選「本機」或「雲端」（整個 app 一起選），metadata 和 `.dat` 會一起選同一邊。
- **沒有合併**：兩台電腦各自改名或分類，只有一邊的 metadata 會留下。
- mod 只在遊戲執行中寫 metadata，所以它會在遊戲結束時跟存檔一起上傳。不要在遊戲外的工具改這個檔。
- 規則依據是調查當時的 `appinfo.vdf`。開發商改後台設定（例如加 `Save_*` pattern 或改成 `*.dat`）會改變結果；這是推測風險，無證據。

## 5. 採用的設計

1. **位置與檔名**：存檔資料夾第一層的 `LoMSaveManager.json`。`LoMSaveManager/` 子資料夾放不需同步的東西：隔離的壞檔、寫入暫存、本機雜湊快取、遷移後的舊檔備份。
2. **辨識存檔**：同步檔裡的每筆記錄用跨機器穩定的身分 `Length + SHA-256 內容雜湊`。本機在 `LoMSaveManager/` 放快取 `key → (Length, WriteTicks, Hash)`，本機大小和修改時間沒變就沿用快取的雜湊，變了才重算（100 KB 的檔案 SHA-256 是毫秒級）。判斷「遊戲剛剛有沒有寫入」是同一台機器內的比較，繼續用 Length + WriteTicks。
3. **遷移**：第一層有 `LoMSaveManager.json` 就以它為準；只有舊的 `LoMSaveManager/index.json` 時，把仍對得上的記錄轉成新格式，寫出新檔成功後再把舊檔改名為 `.migrated-<ticks>` 留著。
4. **讀到更新版本的檔案**：不隔離、不寫回，本次執行唯讀，避免舊版 mod 把新版的 metadata 清空後又同步上雲。
