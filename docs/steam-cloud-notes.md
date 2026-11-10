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

## 6. 插件實際的行為（給玩家和 mod 作者的細節）

這一節是從 README 移過來的細節。標記：**【實測】**是作者在自己的電腦上看到的結果；**【推論】**是依上面的 Auto-Cloud 規則推得、還沒有實機驗證；**【程式測試】**是只有自動測試涵蓋的插件行為。

### 檔案

- 名稱和分類：存檔資料夾第一層的 `LoMSaveManager.json`（每個 Steam 帳號一份）。**【實測】**遊戲結束後它出現在 `remotecache.vdf`，大小和雜湊等於本機的檔，表示已上傳。
- `LoMSaveManager\` 子資料夾只放這台電腦自己用的東西：內容雜湊快取 `local-cache.json`、寫入暫存、壞檔備份（`*.corrupt-<數字>`）、舊版轉移後的備份（`index.json.migrated-<數字>`）。子資料夾**不同步**。除了 `LoMSaveManager.json`，插件不會在第一層留下任何檔案。
- **【實測】** 021 以後的存檔（`Save_021.dat`…）和原作存檔一樣會被同步。
- **【推論】** 另一台電腦會把 `LoMSaveManager.json` 下載下來（下載端還沒有雙機實測）。

### 辨識存檔

- 每筆紀錄存的是存檔的檔案大小和 SHA-256 內容雜湊，不是修改時間（見第 2 節：下載後的修改時間只剩秒精度）。換電腦後只要內容一樣，名稱和分類就對得上。**【程式測試】**，另在單機上模擬過（改修改時間、刪本機快取）。
- 在遊戲外刪掉或換掉某個存檔（大小或內容不同了），它的名稱和分類會自動清掉，不會套到別的存檔上。

### 什麼時候寫 `LoMSaveManager.json`

- 還沒有任何名稱、分類或排序設定時，不會建立它：一個沒有內容的檔（例如離線啟動、或 Steam 這次沒下載成功）可能在 Steam Cloud 衝突時蓋掉雲端那份有內容的檔。第一次取名、建分類、移動存檔或改排序時才建立。**【程式測試】**
- 單純載入不會改寫它（0.1.1 起）：只有你做了修改，或某筆紀錄對應的存檔確實不見或被換掉時才會寫。
- 插件只在遊戲執行中寫它，所以它會在遊戲結束時跟存檔一起上傳。不要在遊戲外編輯它。

### 讀到有問題的檔

- **改壞了（無法解析）**：移到 `LoMSaveManager\LoMSaveManager.json.corrupt-<數字>`，從空白開始。因為第一層的檔被移走，Steam Cloud 也會刪掉雲端那份；資料還在本機的備份裡，插件不會再讀它，可以自己用文字編輯器打開來看。
- **大於 1 MB**（0.1.1 起）：正常的檔遠小於這個大小，所以不讀、**也不移動**（移動會讓 Steam Cloud 把雲端那份一起刪掉），這次執行期間名稱和分類不顯示也不儲存，log 會有警告。本機快取 `local-cache.json` 大於 4 MB 時直接忽略，重新計算雜湊。
- **更新版本的插件寫的**（`Version` 比自己大）：不寫、不移動，這次執行期間唯讀，免得把新版的資料清空後又同步上雲。請把每台電腦的插件更新到同一版。
- 載入時會套用跟介面一樣的限制（0.1.1 起）：分類最多 200 個、分類名稱最多 24 字、存檔名稱最多 40 字，沒有 id 或 id 重複的分類、不是手動存檔格號的紀錄會被略過。這只影響記憶體裡的資料，不會因此改寫檔案。

### 衝突、配額與刪除

- **【推論】** 兩台電腦都改過名稱或分類時沒有合併：Steam 以整個檔案為單位，只會留其中一份（兩邊都離線改過時，可能跳出 Steam 的衝突視窗讓你選本機或雲端）。存檔本身不受影響。
- 配額：100 MB／2000 個檔（第 1 節）。每個存檔約 70–130 KB，大約可以放 1000 個存檔（估算）；`LoMSaveManager.json` 每筆紀錄約 200 bytes，不影響配額。
- **【實測】** 在遊戲外刪存檔時，請在遊戲開著的時候刪，Steam 才會一併刪掉雲端那份；遊戲關閉時刪的檔，下次啟動會被下載回來。最簡單的做法是在遊戲面板裡刪。

### 移除插件後

- 原作只顯示 001–020，這 20 格照常存讀（**【實測】**）。021 以後的存檔留在硬碟上，原作面板看不到，重新安裝就會再出現（依原作程式推論）。
- `LoMSaveManager.json` 和 `LoMSaveManager\` 會留著，不影響原作；`LoMSaveManager.json` 依規則推論會繼續同步。不需要的話可以在遊戲開著時刪掉。
