# LoM Save Manager（活俠傳存檔管理）

這是《活俠傳》（Legend of Mortal）的 BepInEx 插件，把原作的讀檔和存檔面板改成「分類＋存檔清單」兩欄：

- 存檔數不再限 20 格。
- 可以替存檔命名。
- 可以把存檔放進自己建的分類。

原作的存檔檔案格式完全不動。名稱和分類另外存在一個小檔案裡。

[安裝方式](#安裝方式) · [相容性](#相容性) · [回報問題](#回報問題) · [功能](#功能) · [從原始碼建置](#從原始碼建置開發者) · [授權](#授權)

## 安裝方式

### 需求

- **Windows 上的 Steam 版《活俠傳》**（Legend of Mortal，Steam appid 1859910）。測試過的遊戲版本是 **release_1.0.6001.0**，其他版本未測試。插件如果在遊戲裡找不到需要的地方，會整個停用，畫面維持原作。
- **BepInEx 6 Unity.Mono（64 位元 Windows 版）**。測試過的版本是 **6.0.0-be.697**。社群常用的 6.0.0-be.692 **沒有測試過**；BepInEx 5 不能用。
  - 不確定自己裝的是不是 BepInEx 6：看遊戲資料夾裡有沒有 `BepInEx\core\BepInEx.Unity.Mono.dll`，有就是 6。

### 安裝步驟

1. **找到遊戲資料夾。** 在 Steam 圖書館對《活俠傳》按右鍵 → **管理** → **瀏覽本機檔案**。開啟的資料夾就是「遊戲資料夾」，裡面有 `Mortal.exe` 和 `Mortal_Data`。預設位置是 `C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal`。路徑裡有中文字的話，BepInEx 可能出問題，建議避開。
2. **安裝 BepInEx**（已經裝好 BepInEx 6 就跳過這步；不是 be.697 的版本未測試）。
   1. 下載 [`BepInEx-Unity.Mono-win-x64-6.0.0-be.697+5362580.zip`](https://builds.bepinex.dev/projects/bepinex_be/697/BepInEx-Unity.Mono-win-x64-6.0.0-be.697%2B5362580.zip)。它來自官方建置站 [builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be) 的第 697 號建置。遊戲是 64 位元，所以選 `win-x64`，不要選 `win-x86`、`IL2CPP` 或 `linux`。
   2. 把壓縮檔**解壓縮到遊戲資料夾**，讓 `winhttp.dll`、`doorstop_config.ini` 和 `BepInEx` 資料夾跟 `Mortal.exe` 在同一層。
   3. **啟動遊戲一次**，進到標題畫面後關掉。BepInEx 第一次執行會產生 `BepInEx\config`、`BepInEx\LogOutput.log` 等檔案。
3. **下載插件。** 到 GitHub 的 [Releases](https://github.com/Lawa0921/lom-save-manager/releases) 下載最新的 `LoMSaveManager-<版本>.zip`。
4. **解壓縮到遊戲資料夾**（跟上面同一層，有 `Mortal.exe` 的那層）。壓縮檔裡本來就是 `BepInEx\plugins\LoMSaveManager\` 的結構，解壓縮後會跟 BepInEx 的 `plugins` 資料夾合併；系統問是否合併資料夾時選「是」。完成後這個檔案必須存在：

   ```
   <遊戲資料夾>\BepInEx\plugins\LoMSaveManager\LoMSaveManager.dll
   ```

   最常見的錯誤是多包了一層資料夾。用 Windows 檔案總管的「全部解壓縮」時，把目的地改成遊戲資料夾，並取消自動加上的壓縮檔名稱資料夾。
5. **啟動遊戲。**

### 確認安裝成功

- 打開 `<遊戲資料夾>\BepInEx\LogOutput.log`，應該能找到一行 `LoM Save Manager 0.1.0 ready.`（版本號依你下載的版本）。
- 標題畫面按「啟程」，或在遊戲內開「讀取遊戲」「儲存遊戲」，會看到新的兩欄面板：左邊是分類，右邊是存檔清單。
- log 裡出現 `LoM Save Manager disabled: missing ...` 表示遊戲版本跟測試過的不同，插件在遊戲裡找不到需要的地方，所以整個停用。這時畫面維持原作，存檔不受影響。
- log 裡完全沒有 LoM Save Manager 的訊息，多半是檔案位置不對，或 BepInEx 沒裝好。回頭檢查上面第 2 步和第 4 步。

### 更新

1. 關閉遊戲。
2. 從 [Releases](https://github.com/Lawa0921/lom-save-manager/releases) 下載新版 zip，用同樣的方式解壓縮到遊戲資料夾，覆蓋舊的 `LoMSaveManager.dll`。
3. 啟動遊戲，在 `BepInEx\LogOutput.log` 確認新的版本號。

名稱和分類存在存檔資料夾，更新不會動到它們。從舊版（名稱和分類存在 `LoMSaveManager\index.json` 的版本）升級時會自動轉移，見〈[從舊版升級](#從舊版升級)〉。

### 移除

1. 關閉遊戲。
2. 刪掉 `<遊戲資料夾>\BepInEx\plugins\LoMSaveManager` 資料夾。

你的存檔不會被動到。存檔資料夾（`%USERPROFILE%\AppData\LocalLow\Obb Studio\Mortal\<SteamID>\`）裡的 `LoMSaveManager.json` 和 `LoMSaveManager\` 子資料夾只記名稱和分類，可以刪掉，也可以留著（之後重新安裝就能接回來）。要刪的話建議在遊戲開著的時候刪，理由和移除後原作面板會怎樣，見〈[移除後會怎樣](#移除後會怎樣)〉。

## 相容性

- **遊戲和 BepInEx**：測試過的是 release_1.0.6001.0 加 BepInEx 6.0.0-be.697 Unity.Mono。其他版本未測試。
- **Story Weaver**（本作者的 AI 劇情插件）：可以一起安裝，存讀正常。目前的 Story Weaver 只認得 001–020，021 以後的存檔要等 Story Weaver 支援延伸槽位的版本才會接回 AI 記憶，細節見〈[搭配 Story Weaver](#搭配-story-weaver)〉。
- **骰子大師（DiceMaster）**：<!-- TODO: DiceMaster verified -->（驗證中）開發機驗收時有一起載入，沒有出現本插件的錯誤或警告，但還沒有做專門的相容性驗證，結果確認後會更新這裡。已知的一點：骰子大師啟動時顯示的面板可能蓋住讀檔面板的左欄。
- **其他存檔類 mod**，例如咖啡伴侶（Coffee Companion）、存檔格數擴充（Save Slot Expander）等會改存讀檔畫面或存檔格數的 mod：**不支援，也沒有測試過**。兩個 mod 改同一個畫面，同時安裝可能互相干擾，請不要同時使用。

## 回報問題

- **Discord**（優先）：[加入 Discord 伺服器](https://discord.gg/QRyTagwSSF)：回報 bug、提出建議都在這裡
- **GitHub Issues**（次要）：[回報 Issue](https://github.com/Lawa0921/lom-save-manager/issues)

回報時請附上：

- `BepInEx\LogOutput.log`（整個檔案）。log 裡可能有你的 Windows 使用者名稱（路徑），貼出前自己檢查一下。
- 遊戲版本、BepInEx 版本（log 開頭有）、插件版本。
- 你做了什麼、預期會怎樣、實際怎樣。

## 功能

- **分類**
  - 左欄固定有「最近存檔」「自動存檔」「未分類」，以及你自己建的分類。
  - 按「＋ 新增分類」可以建分類。
  - 按「管理分類」可以改名、刪除，或用 ▲▼ 調整分類順序。
  - 分類名稱 1–24 個字，最多 200 個分類。
- **存檔數不限 20 格**
  - 在「儲存遊戲」選一個分類，再選「＋ 存到「分類名」」，就會存成一筆新存檔。新存檔用最小的空號。
  - 存檔會自動歸進這個分類。
- **命名**
  - 新存檔存好後會跳出命名框，可以直接按「略過」。
  - 之後可以用每筆存檔的「命名」補上或修改名稱，最多 40 個字。
  - 覆蓋舊存檔時會預先填好原本的名稱，分類也不變。
- **移動**：用「移動」把存檔換到別的分類或「未分類」。
- **刪除分類**：分類裡的存檔會回到「未分類」，**存檔本身不會被刪除**。
- **排序**：右上角可以切換「存檔時間：新→舊／舊→新」。
- **開始新的旅程**：在「啟程」選好分類後按「＋ 開始新的旅程」，新的一局會歸進那個分類。
- **語言**：支援繁體中文和簡體中文，跟著遊戲的語言設定切換。其他語言顯示繁體中文。

原作的「傳奇NNN」編號、日期、覆蓋和刪除的確認視窗都照原作保留。

## 名稱和分類存在哪裡

名稱和分類存在存檔資料夾第一層的 `LoMSaveManager.json`。依這款遊戲的 Steam Auto-Cloud 規則，第一層的檔案都會同步，所以它會跟著 Steam Cloud 同步。開發機上已實測到它在遊戲結束後被上傳；從另一台電腦下載還沒有實測（見下方〈Steam Cloud〉）：

```
%USERPROFILE%\AppData\LocalLow\Obb Studio\Mortal\<SteamID>\LoMSaveManager.json
```

- 每個 Steam 帳號各一份。
- 同一個資料夾還有一個子資料夾 `LoMSaveManager\`。它只放這台電腦自己用的東西（內容雜湊的快取、寫入暫存、壞檔和舊檔的備份），**不會同步**。除了 `LoMSaveManager.json`，本插件不會在存檔資料夾第一層留下任何檔案。
- 每筆存檔記錄的是它的檔案大小和內容雜湊（SHA-256），不是修改時間。依開發機上的觀察推論，Steam 下載下來的檔案修改時間會跟原本不同（只剩秒精度，見 `docs/steam-cloud-notes.md`），所以不拿修改時間當依據：換一台電腦、Steam 把存檔下載下來之後，只要內容一樣，名稱和分類就該對得上。這點有程式測試，也在開發機上模擬過（把存檔的修改時間改成整秒加 1 秒、刪掉本機快取，分類仍在、雜湊比對成立）；不過實測用的存檔都沒有取名，所以「名稱」保留只有程式測試涵蓋。還沒有雙機實測。
- 如果你在遊戲外刪掉或換掉某個存檔（大小或內容不同了），它的名稱和分類會自動清掉，不會套到別的存檔上。
- 還沒有任何名稱或分類時，插件不會建立 `LoMSaveManager.json`：單純在遊戲裡存檔、開始新的旅程都不會建立它（存檔的大小和雜湊先記在記憶體裡）；第一次取名、建分類、把存檔放進分類或改排序時才建立，建立時會一併寫入當時已知的存檔大小和雜湊。這是為了避免一個沒有內容的檔（例如離線啟動、或 Steam 這次沒下載成功）在 Steam Cloud 衝突時蓋掉雲端那份有內容的檔。檔案一旦存在，之後每次存檔都會照常更新它。【程式測試】
- 不要在遊戲外手動編輯 `LoMSaveManager.json`。插件只在遊戲執行中寫它，它會在遊戲結束時跟存檔一起上傳（已實測）。

### 從舊版升級

舊版的名稱和分類存在 `LoMSaveManager\index.json`（不同步）。更新插件後的**第一次啟動**會自動轉移：

- 轉移到 `LoMSaveManager.json`，名稱和分類原樣保留。只有上次之後被換過的存檔（大小或修改時間跟舊記錄對不上）會被清掉，規則和舊版一樣。
- 舊檔改名成 `LoMSaveManager\index.json.migrated-<數字>` 留著，不會刪。確定沒問題後可以自行刪掉。
- 如果轉移當下出問題（例如某個存檔剛好被鎖住），這次什麼都不會寫，也不會建立空的 `LoMSaveManager.json`；下次啟動再試。遊戲照常存讀，只是這次執行期間的名稱和分類改動不會被存下來。log 會有警告說明原因。
- 如果舊的 `index.json` 壞到永遠讀不了（無法解析，或版本不對），它會被改名成 `LoMSaveManager\index.json.corrupt-<數字>` 留著，再從空白開始，不會一直卡在轉移。見下方〈疑難排解〉。
- 已經有 `LoMSaveManager.json`（例如另一台電腦先轉移並同步過來）時，以它為準，舊的 `index.json` 不動，也**不會合併**。所以如果第二台電腦在舊版時也取過自己的名稱和分類，更新後那台看到的是同步過來的那份，它自己的舊名稱不會出現；log 每次啟動都會記一行說明 `index.json` 沒有轉移（舊檔裡沒有任何名稱、分類或排序設定時不記，因為沒有東西會漏掉）。舊檔原樣留在 `LoMSaveManager\index.json`，需要的話可以自己用文字編輯器開來看、手動在遊戲裡重新取名。
- 舊的 `index.json` 裡沒有任何要轉移的東西（沒有分類、沒有改過排序、還對得上的紀錄都沒有名稱也沒有分類）時，舊檔一樣改名留著，但不會建立 `LoMSaveManager.json`；等你第一次取名或建分類時才建立。

## Steam Cloud

以下各點分成三種：**【實測】**是 2026-11-09／11-10 在開發機上看到的結果；**【推論】**是依 Steam 的 Auto-Cloud 規則（[`docs/steam-cloud-notes.md`](docs/steam-cloud-notes.md)）推得的，**還沒有在實機驗證**；**【程式測試】**是只有自動測試涵蓋、沒有在 Steam 上實測的插件行為。

- **【實測】** 021 以後的存檔（`Save_021.dat`…）和原作存檔一樣，會被 Steam Cloud 同步。
- **【實測】名稱和分類（`LoMSaveManager.json`）會上傳到 Steam Cloud。** 遊戲結束後，Steam 的同步清單（`remotecache.vdf`）出現了這個檔，大小和雜湊都等於本機的檔，狀態和其他已同步的存檔相同；子資料夾 `LoMSaveManager\` 裡的檔則沒有出現。這符合這款遊戲目前的 Auto-Cloud 規則「存檔資料夾第一層的所有檔案都同步，不含子資料夾」（見 `docs/steam-cloud-notes.md`）。舊版把名稱和分類放在子資料夾 `LoMSaveManager\index.json`，所以當時沒有同步。這個規則是開發商在 Steamworks 後台設定的，日後可能改變。
- **【推論】另一台電腦會把它下載下來。** 下載這一端還沒有雙機實測；依 `.dat` 存檔的實測和同一條規則推論會下載。
- **【推論】兩台電腦都改過名稱或分類時，沒有合併。** Steam 以整個檔案為單位，預期會挑其中一份（如果兩邊都離線改過，可能跳出 Steam 的衝突視窗，讓你選本機或雲端）。沒被選到的那台所做的名稱和分類改動會消失。存檔本身不受影響。
- **【程式測試】本版碰到未來更新版本寫的檔案不會覆蓋它。** 這個保護是從本版（v2，第一個使用 `LoMSaveManager.json` 的版本）開始才有：如果日後的新版改了格式（版本號比本版大，即使欄位形狀變了、本版讀不懂），本版那次執行期間不會寫、也不會移走這個檔，名稱和分類會先顯示成空白，免得把新版的資料清空後又同步上雲。**舊版（v1）根本不讀 `LoMSaveManager.json`**：另一台電腦若還裝著 v1，它既不會同步名稱和分類，也不受這個保護。請把每台電腦的插件都更新到同一版。
- **【實測】** 要在遊戲外刪掉存檔時，請在遊戲開著的時候刪。實測這樣 Steam 會一併刪掉雲端那份；在遊戲關閉時刪，下次啟動會被雲端下載回來。

## 搭配 Story Weaver

Story Weaver（另一個 AI 劇情插件）會替每個存檔記住 AI 劇情記憶。目前的 Story Weaver 只認得 001–020：

- 021 以後的存檔可以正常存讀。
- 但 021 以後的存檔**要等 Story Weaver 支援延伸槽位的版本**，才會接回各自的 AI 記憶。在那之前，讀這些存檔時 Story Weaver 會當成新的記憶開始。

## 移除後會怎樣

照〈[移除](#移除)〉的步驟刪掉插件資料夾之後：

- 原作只會顯示 **001–020** 這 20 格。這 20 格完全不受影響，照常存讀。
- **021 以後的存檔仍然留在硬碟上**，但原作的面板看不到。重新安裝插件後就會再出現。這點是依原作的程式推論的：原作面板固定只建 20 格，而本插件從不搬移或改名存檔。移除插件時「001–020 照常顯示和讀取」有實測，「021 以後看不到」沒有實測。
- `LoMSaveManager.json`（名稱和分類）和 `LoMSaveManager\` 子資料夾會留在存檔資料夾，不影響原作。`LoMSaveManager.json` 在第一層，依 Auto-Cloud 規則推論會繼續跟著 Steam Cloud 同步（插件裝著時已實測會上傳；移除後沒有實測）。不再需要的話可以自行刪除；建議在遊戲執行中刪。依 `.dat` 存檔的實測（見上方〈Steam Cloud〉），遊戲關閉時刪掉的檔，下次啟動可能會被雲端下載回來；這個檔本身沒有實測過。

## 已知限制

- 存檔上限是 **9999 筆**（`Save_001`–`Save_9999`）。設計上，全部用完時「＋ 存到…」那一列會換成「存檔數已達上限 9999」的提示，仍可覆蓋既有存檔。這個畫面**還沒有在遊戲裡實測過**，只有程式測試。
- 名稱和分類會上傳到 Steam Cloud（已實測；另一台電腦下載還沒實測）。兩台電腦都改過時，推論 Steam 只會留其中一份，沒有合併（見上方 Steam Cloud）。
- 分類只有一層，不能巢狀。
- 「最近存檔」「自動存檔」「未分類」是固定分類，不能改名或刪除，也不能把存檔移進「最近存檔」或「自動存檔」。

## 疑難排解

- **裝好了但面板沒有變，或 log 裡沒有 `ready`**：見〈[確認安裝成功](#確認安裝成功)〉，先檢查檔案位置和 BepInEx 版本。
- **名稱和分類全部不見，存檔都在「未分類」**：先看 `LoMSaveManager.json` 還在不在。
  - 檔案壞掉（無法解析）時，插件會把它改名成 `LoMSaveManager\LoMSaveManager.json.corrupt-<數字>` 保留，然後從空白開始。原檔被移走，所以第一層不會留下壞檔，也不會把壞檔同步上雲。遊戲照常存讀。之後第一次改名稱或分類時會寫出新的 `LoMSaveManager.json`，它一樣在第一層，會跟著 Steam Cloud 同步（取代雲端那份壞檔）。
  - 舊版轉移時，如果舊的 `LoMSaveManager\index.json` 永遠無法解析（壞掉，或版本不對），它會被改名成 `LoMSaveManager\index.json.corrupt-<數字>` 留著（不刪），再從空白開始。
  - 這兩種壞檔備份，插件都不會再讀回來。想救回名稱的話，可以自己用文字編輯器開來看。
- **名稱和分類在這次執行期間改了卻沒存下來**：看 log（`BepInEx\LogOutput.log`）裡 LoM Save Manager 的警告。可能是舊版轉移還沒完成，或 `LoMSaveManager.json` 是更新版本插件寫的（見上方〈Steam Cloud〉）。遊戲本身的存讀不受影響。
- **在遊戲外刪存檔**：請在遊戲開著的時候刪，這樣 Steam 才會一併刪掉雲端那份（理由見上方〈Steam Cloud〉）。

## 從原始碼建置（開發者）

一般玩家不需要看這節，直接用〈[安裝方式](#安裝方式)〉的壓縮檔就好。

建置需要 **.NET SDK**（`dotnet` 指令，例如 .NET 8 或更新版；插件本身編成 .NET Framework 4.8，SDK 會自動取得參考組件）。建置要參照遊戲和 BepInEx 的 DLL，所以電腦上要有已經安裝 BepInEx 的遊戲；這些 DLL 只在編譯時被讀取，不會被複製進插件或發佈包。遊戲不在預設 Steam 路徑 `C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal` 時，要用 `-GameDir`（腳本）或 `-p:GameDir`（`dotnet build`）指向遊戲安裝資料夾。

- **建置並部署到本機遊戲**：`powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1`。它會建置 Release 版並複製到 `<遊戲資料夾>\BepInEx\plugins\LoMSaveManager\`。必須在遊戲關閉時執行。
- **只建置**：`dotnet build src\Plugin\LoMSaveManager.csproj -c Release -p:GameDir=<遊戲資料夾>`。產出在 `src\Plugin\bin\Release\net48\LoMSaveManager.dll`，手動放到 `<遊戲資料夾>\BepInEx\plugins\LoMSaveManager\` 裡就能用。
- **打包發佈壓縮檔**：`powershell -ExecutionPolicy Bypass -File scripts\package.ps1`。它會建置 Release 版（不需要關遊戲，也不會動遊戲資料夾），再產生 `dist\LoMSaveManager-<版本>.zip` 和同名的 `.zip.sha256`，並印出 SHA-256。版本號讀自 `src\Plugin\SaveManagerPlugin.cs` 的 `PluginVersion`，發佈新版時只要改那一處。壓縮檔的內容：

  ```
  BepInEx/plugins/LoMSaveManager/LoMSaveManager.dll
  BepInEx/plugins/LoMSaveManager/README.md
  BepInEx/plugins/LoMSaveManager/LICENSE
  ```

  設計成直接解壓縮到遊戲資料夾。README 和 LICENSE 放在插件資料夾裡而不是壓縮檔根目錄，這樣解壓縮不會在遊戲資料夾留下多餘的檔案，移除時刪掉插件資料夾也會一起清掉。
- **跑測試**：`dotnet run --project tests/Core.Tests/Core.Tests.csproj`。全部通過時印出 `N passed, 0 failed`，結束碼為 0。

## 授權

本專案以 [MIT 授權](LICENSE) 釋出，Copyright (c) 2026 SuperGalen0921。

這是玩家自製的非官方 mod，與 Obb Studio 無關。發佈包只有插件本身（`LoMSaveManager.dll`）、README 和授權文字，不含遊戲的任何檔案，也不含 BepInEx；BepInEx 有它自己的授權（LGPL-2.1），請見 BepInEx 專案。
