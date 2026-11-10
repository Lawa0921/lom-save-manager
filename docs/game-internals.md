# 活俠傳 (Legend of Mortal) 存讀檔內部結構參考

- 對象版本：Steam `release_1.0.6001.0`；Unity `2020.3.49f1`（`globalgamemanagers` BuildSettings）；Mono 後端。
- 已安裝 BepInEx：`6.0.0-be.697`（`BepInEx\core\BepInEx.Unity.Mono.dll` ProductVersion）。
- 調查日期：2026-11-09。全部為唯讀調查，未啟動遊戲，未修改遊戲或存檔資料夾。

## 0. 來源與標記方式

- 反編譯：`ilspycmd 9.1.0.7988`（需 `DOTNET_ROLL_FORWARD=Major`，本機只有 .NET 9/10 runtime）。
  反編譯輸出沒有放進本 repo（遊戲程式碼的版權屬於開發商），需要時自行產生到任一資料夾 `decomp\<Assembly>\...`。
  產生的指令：`ilspycmd -p -o <out>\<Asm> -r <Managed> <Managed>\<Asm>.dll`。
- 存讀檔相關的程式碼幾乎都在 **`Mortal.Core.dll`**（namespace `Mortal.Core`），不在 `Assembly-CSharp.dll`（那裡只有 13 個檔案）。
  下文的 `[src: X.cs]` 指 `decomp\Mortal.Core\Mortal.Core\X.cs`；其他組件會寫全名，例如 `Mortal.Free/FreeManager.cs`。
- 場景與 prefab 的序列化資料是用 UnityPy 1.25.4 加 TypeTreeGeneratorAPI 讀的（遊戲 build 拿掉了 typetree，要從 Managed DLL 重建）。
  標成 `[scene: levelN …]` 或 `[prefab: …]`。場景對照（BuildSettings）：
  `level0`=CoreScenes, `level1`=Title, `level2`=Story, `level3`=Free(養成), `level4`=GameOver, `level5`=Battle, `level6`=Combat, `level7`=Loading1, `level8`=End, `level9`=DemoEnd, `level10`=Pinball。
- 不確定的地方標 **(未確認)**；從程式碼推論、但沒有實際跑過的標 **(推論)**。

---

## 1. SaveSystem（`Mortal.Core.SaveSystem : ScriptableObject`）[src: SaveSystem.cs]

### 1.1 取得 instance
- `public static SaveSystem Instance`：lazy 呼叫 `Resources.FindObjectsOfTypeAll<SaveSystem>().FirstOrDefault()`。
- 序列化欄位（asset 上的值）：`bool _isDemo`、`string _currentSlot`、`PlayerStatManagerData _playerStat`、`MissionManagerData _missionData`、`ItemDatabase _itemDatabase`、`ShopDatabase _shopDatabase`、`LibrarySystem _librarySystem`、`LibraryAwardData[] _awardList`、`ReadStorySystem _readStorySystem`、`SavePlatformType _savePlatformType`、`string _restoreData`、`int _titlePartner`。
- 常數（`private const string`）：`NEWGAME_FILE="new"`（程式裡沒用到）、`STORY_AUTO_SAVE_FILE="auto"`、`FREE_AUTO_SAVE_FILE="auto_free"`、`BATTLE_AUTO_SAVE_FILE="auto_battle"`、`GLOBAL_SAVE_FILE="universe"`。

### 1.2 存檔目錄與檔名
- `private string GetSaveDataFile(string name)` 的內容就是
  `Path.Combine(Application.persistentDataPath, GetUserFolder() + "/Save_" + name + ".dat")`。
- `private string GetUserFolder()`：預設是 `"Save"`；當 `_savePlatformType == SavePlatformType.Steam` 時改用 `SteamUser.GetSteamID().m_SteamID.ToString()`。
  - 實際路徑：`%USERPROFILE%\AppData\LocalLow\Obb Studio\Mortal\<SteamID64>\`。Steam 版走的是 Steam 分支。
- **`name` 會原樣串進路徑**，沒有做任何清理，也沒有長度或格式限制。任何字串都能當 slot key（例如 `"021"`、`"1000"`、`"abc"`）。
  要注意保留字會撞名：`auto`、`auto_free`、`auto_battle`、`universe`；`act1` 是 `TitleManager.StartActGame` 用的 demo/act slot。
- slot key 的「格式」不是 SaveSystem 決定的，而是 UI 決定的：`LoadSlotPanel.SetSlot(int)` 和 `SaveSlotPanel.SetSlot(int)` 都寫 `_slot = index.ToString().PadLeft(3, '0')`。
  - `PadLeft(3)` 只補零、不截斷：1 → `"001"`，999 → `"999"`，1000 → `"1000"`，12345 → `"12345"`。**4 位數以上可以用**，對應檔名 `Save_1000.dat`（推論，沒有實際跑過）。
  - 只要整數 slot 不會產生兩種寫法（例如 `"0100"` 和 `"100"`），就不會有 key 歧義。
- 其他檔案：
  - `GetLegendDataFile(int slot)` → `<user>/Legend_<slot.PadLeft(3)>.dat`。這是「結局傳奇」紀錄，給講經堂和結局面板用，**跟一般存檔槽無關**。
  - `GetBackupDataFile(name)` → `persistentDataPath/Backup/yyyyMMddHHmmss/Save_<name>.dat`（**不在** SteamID 子目錄裡），只有 `BackupLibraryData()` 用來備份 universe。
- 實際目錄內容（唯讀列出）：`Save_001..020.dat`（69–127 KB）、`Save_auto*.dat`、`Save_universe.dat`（1.19 MB）加 `Save_universe.json`（1.5 MB）、`Legend_001..012.dat`、`steam_autocloud.vdf`。
  有 `steam_autocloud.vdf` 代表開了 Steam Auto-Cloud。同步哪些檔名是 Steamworks 後台的設定，下面是**已確認（2026-11-09 作者實測，對照 Steam 的 `remotecache.vdf`）**的結果：
  - **`Save_021+.dat` 會上雲。** `remotecache.vdf` 出現 `Save_021.dat`（size 92377，syncstate 1）和 `Save_022.dat`（size 115361，syncstate 1）。本機在遊戲關閉時刪掉後，下次啟動被 Steam 重新下載。
  - **在遊戲執行中刪除才會同步刪除。** remotecache 的 ChangeNumber 從 264 變成 265，條目從 39 變成 37。
  - **（v1 位置）`LoMSaveManager/index.json`（子資料夾）在觀察中沒有上雲。** 有效的觀察只有一次：當時本機確實有 `LoMSaveManager/index.json`，但 remotecache 快照沒有它的條目。這是目前後台設定下的觀察結果，不是保證，開發商改設定的話可能會變。
  - **2026-11-09 補註（v2）**：原因是 Auto-Cloud 規則只同步第一層、不含子資料夾（`docs/steam-cloud-notes.md`）。v2 把名稱和分類改放第一層的 `LoMSaveManager.json`，這一條已不適用；2026-11-10 實測遊戲結束後會上傳。

### 1.3 寫入路徑（`.tmp` / `.bak`）
- `private void ExecuteSaveData(string saveFile, object gameSave)`：
  1. 用 `BinaryFormatter.Serialize` 寫到 `saveFile + ".tmp"`（`FileMode.OpenOrCreate`），然後 `Flush(flushToDisk: true)`。
  2. 如果 `saveFile` 已存在：先刪掉舊的 `.bak`，再把 `saveFile` 改名成 `.bak`。
  3. 把 `.tmp` 改名成 `saveFile`，最後刪掉 `.bak`。
  4. 出錯時：如果 `.bak` 還在，就改名回 `saveFile`；`finally` 一定會刪 `.tmp`。
  5. 會 log `"[SaveSystem]Save success: <path>"` 或 `"Save failure: ..."`（`LogMessage`）。
- `private void ExecuteSaveJsonData(string saveFile, object gameSave)`：`JsonUtility.ToJson(pretty)` 後直接寫到 `saveFile.Replace(".dat", ".json")`。只有 universe 會用。
- `public void CreateDir(string saveFile)`：目錄不存在時建立。
- 上層的寫入 API：
  - `public void SaveGameData()` 等於 `SaveGameData(_currentSlot)`。
  - `public void SaveGameData(string slot)`：`CreateSaveData()`，設定 `IsDemo`，然後 `ExecuteSaveData`。**不會改 `_currentSlot`，也不會寫 universe。**
  - `private void AutoSaveData(string slot)`：包成 `AutoGameSave { Slot = _currentSlot, GameSave = CreateSaveData() }` 再寫入。
    對外的是 `AutoSaveStoryData()`（`"auto"`）、`AutoSaveFreeData()`（`"auto_free"`）、`AutoSaveBattleData()`（`"auto_battle"`）。
    呼叫點：`Mortal.Story/LuaManager.cs`（劇情與養成）、`Mortal.Free/FreeManager.cs:290`（進養成且 `SystemSettings.AutoSaveHome` 開啟時）、`Mortal.Battle/GameLevelManager.cs:499`、`Mortal.Combat/CombatManager.cs:247`。
  - `public void NewGameData()`：重置各 manager，寫入 `GetSaveDataFile(_currentSlot)`。**新遊戲會立刻佔用目前的 slot 檔。**
    `NewActGameData()` 做的事相同，只是起始腳本是 `act1`。
  - `public void SaveUniverseData()`：寫 `Save_universe.json` 和 `Save_universe.dat`，內容包含 `CurrentSlot = _currentSlot`。
  - `public void SaveCurrentSlotData(string slot)`：**參數被忽略**，只呼叫 `SaveUniverseData()`。C# 程式碼裡沒有呼叫點（可能是 UnityEvent 在用）。
- `private GameSave CreateSaveData(bool includeStoryLog = true)`：設定 `Version = Application.version`、`TimeTick = DateTime.Now.Ticks`，再依序呼叫 `_playerStat/_missionData/_itemDatabase/_shopDatabase.Save(gameSave)`，加上 `StoryLogData.Save`，最後設 `RestoreData`。

### 1.4 讀取、顯示資訊、header
- `public GameSave GetSaveData(string slot)`、`public AutoGameSave GetAutoSaveData(string slot)`、`public UniverseSave GetUniverseSaveData()`、`public LegendSave GetLegendSaveData(int slot)`
  都走 `private T GetSaveData<T>(string file) where T : class`：檔案不存在回 `null`，否則 `BinaryFormatter.Deserialize` **整個檔案**。這個方法沒有 try/catch，壞檔會直接丟例外給呼叫端。
- **沒有任何輕量 header 或 metadata 檔。** 存讀檔面板每次 `Setup()` 都會反序列化整份 `GameSave`（70–130 KB 的 BinaryFormatter 物件圖，含 `StoryLogs` 等等），只為了取下面幾個欄位：
  - `GameSave.CurrentYear`（`int`）、`CurrentMonth`（`int`）、`CurrentStage`（`MonthStageType`）：遊戲內日期。用 `new GameTime(y, m, stage)` 取得 `YearText`（`"System/Number_{Year}"`）、`MonthText`（`"System/Month_{Month}"`）、`StageText` 這些 localization key，再套 `"System/GameTimeText"` 的格式（繁中是 `第{0}年{1}{2}`）。[src: GameTime.cs, LoadSlotPanel.cs SetupText]
  - `GameSave.TimeTick`（`long`，等於 `DateTime.Now.Ticks`，是本地時間）：真實時間，顯示格式 `new DateTime(TimeTick).ToString("yyyy/MM/dd HH:mm:ss")`。
  - `GameSave.IsDemo`（`bool`）：在標題前面加 `"[試玩] "`（這段字串寫死在程式裡，沒有 localize）。
  - `GameSave.NewGamePlus`（`bool`）：控制 `_newGamePlusIcon` 那顆星的顯示。
- **「傳奇編號」就是 slot key 本身**，跟 `Legend_*.dat` 無關。`_slotText.text = string.Format(GetString("System/SaveSlotText"), _slot)`；繁中 `傳奇{0}`、簡中 `传奇{0}`、韓文 `전기 {0}`。prefab 上預設的文字是 `"傳奇001"`。
- `GameSave` [src: GameSave.cs] 是 `[Serializable]` 類別，有大約 60 個 public 欄位。開頭幾個：`string Version; long TimeTick; bool IsDemo; bool NewGamePlus; string CurrentStoryScript; string StartStoryScript; string CurrentScene; ...; int CurrentYear; int CurrentMonth; MonthStageType CurrentStage; ...`，後面還有 `List<StorySave> StorySave`、`List<StoryLogSave> StoryLogs`、`string RestoreData`。**裡面沒有「存檔名稱」或「備註」欄位。**
- `AutoGameSave` [src: AutoGameSave.cs]：`[Serializable] { public string Slot = ""; public GameSave GameSave; }`，其中 `Slot` 是寫入當下的 `_currentSlot`（擁有這份自動存檔的手動槽）。
- `UniverseSave` [src: UniverseSave.cs]：`Version`、`long TimeTicks`、`LibraryData1..4`、`List<string> ReadStoryData`、`int TitlePartner`、`string CurrentSlot`。
- `LegendSave` [src: LegendSave.cs]：`long TimeTick`、`string EndKey`、`List<StorySave> Story`。
- 想做輕量索引的話（推論）：比較安全的做法是 mod 自己維護一份 sidecar 索引（slot → TimeTick、年月旬、NewGamePlus、IsDemo、自訂名稱），在 `SaveGameData(string)` 的 postfix 裡更新；或者在面板呼叫 `GetSaveData` 之後順手快取，用 (mtime, length) 判斷快取是否失效。

### 1.5 讀取 API
- `public void LoadGameData()`：`GetSaveData(_currentSlot)`，不是 null 就呼叫 `ExecuteLoadGameData(save)` 再呼叫 `StoryLogData.Load(save)`。
- `public void AutoLoadGameData(string slot)`：同樣流程，但讀 `GetAutoSaveData(slot).GameSave`。
- `private void ExecuteLoadGameData(GameSave gameSave)`：呼叫四個 manager 的 `.Load(gameSave)`，並設 `_restoreData = gameSave.RestoreData`。
- `UpdateRestoreData()` / `LoadRestoreSaveData()`：在記憶體裡做 JSON 快照（`_restoreData`），由 `Mortal.Story/LuaManager.cs` 使用，跟檔案無關。

### 1.6 目前 slot（`_currentSlot`）與 Save_universe
- `public string CurrentSlot => _currentSlot;`，`public void SetSlot(string slot)` 會直接指定。
- 寫入 universe：`SaveUniverseData()`（`CreateUniverseSaveData` 會帶上 `CurrentSlot`）。
  讀回來：`LoadUniverseData()`（由 `UniverseDataLoader.Start()` 觸發 [src: UniverseDataLoader.cs]），會還原 `_currentSlot = saveData.CurrentSlot`；如果沒有 universe 檔，就 Reset 並把 `_currentSlot = ""`。
- **誰會改 `_currentSlot`**：`LoadSlotPanel.OnTitleClick`（讀檔或在空槽開新遊戲）、`RecentSaveSlotPanel.OnTitleClick`、`AutoSaveSlotPanel.OnTitleClick`（改成 `AutoGameSave.Slot`）、`TitleManager.OpenTestPanel/OpenPreviewPanel`（`"001"`）、`StartActGame`（`"act1"`）、`LoadGameAct`。
- **重點**：用遊戲內的 `SaveGamePanel` 存到別的槽，**不會**改 `_currentSlot`，見 2.4。所以「最近存檔」（`RecentSaveSlotPanel`）顯示的是「最後讀取或開新遊戲的槽」，不是「最後寫入的槽」（推論，直接從程式碼讀出來的）。
- 只有 `LoadSlotPanel.OnTitleClick` 讀檔後會呼叫 `SaveUniverseData()` 把 `CurrentSlot` 寫進檔案；Recent/Auto 讀檔不會立即寫，要等之後其他流程（例如 LuaManager 或 StatModifyManager 的 `SaveUniverseData`）才會寫進去。

### 1.7 刪除
- `public void DeleteSaveData(string slot)`：只在 `Save_<slot>.dat` 存在時刪掉它。**不處理 `.bak`、`.tmp`、`.json`，也不會清 `_currentSlot`。**
  所以「最近存檔」可能指向已經刪掉的槽，那時面板會顯示 `System/NoData`。

---

## 2. 存讀檔面板

### 2.1 類別關係（沒有共用基底類別）
全部直接繼承 `MonoBehaviour`，彼此沒有繼承關係：
- `LoadGamePanel`：讀檔面板，也負責刪除確認。[src: LoadGamePanel.cs]
- `SaveGamePanel`：存檔面板，也負責覆蓋確認。[src: SaveGamePanel.cs]
- `LoadSlotPanel`：讀檔面板裡的每一格。`SaveSlotPanel`：存檔面板裡的每一格。
- `AutoSaveSlotPanel : ILoadGameSlot`、`RecentSaveSlotPanel : ILoadGameSlot`：讀檔面板左側。
- `LoadGameSlot : ILoadGameSlot`：掛在每格的 `Slot` 按鈕上，給刪除熱鍵查詢用。
- `interface ILoadGameSlot { string Slot { get; } bool IsEmpty { get; } }`。
- 開關面板用的是 `CommonPanel`（CanvasGroup 淡入淡出加 open/close 的 `GameEvent` 與 `UnityEvent`，見 3.1）。
- 程式裡沒有被任何地方引用、看起來是舊碼的類別：`SaveSlotTitle`、`SaveSlotDeleteButton`、`ConfirmSavePanel`。

**存與讀是兩個不同的面板。** 各場景的配置 [scene]：

| 場景 | 物件路徑 | 用途 |
|---|---|---|
| Title (level1) | `UI/Layer_2/LoadGamePanel` | 「啟程」：`UI/Layer_1/Buttons/StartGame` 的 onClick 是 `CommonPanel.Show`，打開這個 LoadGamePanel。`TitleManager._slotPanel` 指向同一個 CommonPanel。標題畫面**沒有** SaveGamePanel。 |
| Story (level2) | `[UI]/TopPanel/MenuPanel/SaveGamePanel`、`.../MenuPanel/LoadGamePanel` | ESC 選單：`MenuPanel._savePanel` 和 `_loadPanel` 分別指向它們 |
| Free (level3) | `[UI]/TopPanel/MenuPanel/SaveGamePanel`、`.../MenuPanel/LoadGamePanel`，另外還有 `[UI]/TopPanel/SaveGamePanel` | 第二個 SaveGamePanel 是養成主畫面的「存檔」鈕（`Mortal.Free/FreeManager.SaveButtonClick` → `SaveOpen(true)` → `_savePanel`） |
| Battle (level5) | `[UI]/TopPanel/PausePanel/LoadGamePanel` | 只有讀檔 |
| Combat (level6) | MenuPanel 有讀檔，`_saveButton` 是 null | 只有讀檔 |

`MenuPanel` [src: MenuPanel.cs]：Awake 時用 `AddButtonClickListener` 掛上：`_saveButton` → `SaveButtonClick()` → `_savePanel.Show(true)`；`_loadButton` → `LoadButtonClick()` → `_loadPanel.Show(true)`。
另外有一組 `_confirmSaveButton` / `_cancelSaveButton`，位置在 `MenuPanel/SavePanel/ConfirmSave/...`，按下會呼叫 `SaveSystem.SaveGameData()` 快速存進目前的槽。這看起來是舊的快速存檔 UI，是否還看得到 **(未確認)**。
`FreeManager` 也有一個 `_saveConfirmPanel`，`SaveConfirmButtonClick` 同樣呼叫 `SaveGameData()` 存進目前的槽（`Mortal.Free/FreeManager.cs:674`）。

### 2.2 LoadGamePanel 欄位 [src: LoadGamePanel.cs；scene 值取自 level1/2/3/5/6，各場景相同]
```
InputActionReference _deleteAction            // ext sharedassets1 #606
Button _autoSaveButton
AutoSaveSlotPanel[] _autoSaveSlot             // 場景內有 3 個：劇情、養成、戰鬥
LoadSlotPanel[] _saveSlots                    // 場景內是空的，Awake 時才建立
RecentSaveSlotPanel _recentSavePanel
int _slotCount = 20                           // 場景值也是 20
GameObject _slotPrefab                        // sharedassets1.assets PathID 467 "LoadGameSlot"
Transform _slotContainer                      // .../Container/Scroll View/Viewport/Slots
CommonPanel _deletePanel                      // .../Container/DeletePanel
Button _cancelDeleteButton
InputButton _closeButton                      // CloseButton_02
string _currentDeleteSlot; GameObject _currentSelectableObject
public static LoadGamePanel Instance { get; private set; }   // 在 Awake 設定；最後一個 Awake 的會覆蓋前面的
```
- `Awake()`：設 `Instance = this`，然後 `CreateSlot()`、`SetButtonNavigation()`。
- `CreateSlot()`：先 `Destroy` 掉 `_slotContainer` 底下所有子物件（場景裡預先放好的 `Slot_001..Slot (20)` 也會被清掉），然後 `for i = 1.._slotCount`：
  `Instantiate(_slotPrefab, _slotContainer, false).GetComponent<LoadSlotPanel>()`，接著 `SetSlot(i)`、`Setup()`，最後轉成陣列。**沒有 pooling，也沒有虛擬化**，每一格都是一個實際的 GameObject。
- 因為 Awake 時就會跑 `Setup`，N 格就會讀 N 個檔案。
- `OnPanelOpen()`（由 CommonPanel 的 `_openResponse` 呼叫）：選取 `_recentSavePanel.SlotButton`，然後 `UpdateSlot()`。`UpdateSlot()` 會對所有 active 的 Auto 格、**所有** `_saveSlots` 以及 Recent 都呼叫 `Setup()`。**每次打開面板都會把 N+4 個檔案全部重新反序列化。**
- 刪除：
  - `OpenDeletePanel(string slot)`：記下目前選取的物件和 slot，`_deletePanel.Show(true)`，`_closeButton.SetActive(false)`。
  - `ConfirmDeleteSlot()`：`SaveSystem.Instance.DeleteSaveData(_currentDeleteSlot)`，然後 `UpdateSlot()`、`CloseDeletePanel()`，再選取 `_currentSelectableObject.transform.parent.GetChild(0)`（同一格的 Slot 按鈕）。
  - `CloseDeletePanel()`：關閉確認窗並還原選取。
  - DeletePanel 的按鈕 [scene]：`ConfirmDeletePanel/MenuButton_Submit` 的 onClick 是 `LoadGamePanel.ConfirmDeleteSlot`；`MenuButton_Cancel` 的 onClick 是 `LoadGamePanel.CloseDeletePanel`。訊息文字的 key 是 `System/ConfirmDelete`（「確定刪除選擇的紀錄嗎？」）。
  - 熱鍵：`_deleteAction.performed` 時，如果目前選取的物件有 `ILoadGameSlot` 且 `!IsEmpty`，就呼叫 `OpenDeletePanel(component.Slot)`。
- 導覽：`SetLoadPanelNavigation()` 對每一格設定 Explicit 上下導覽，`selectOnLeft` 指向 Recent。`SetAutoPanelNavigation()` 和 `SetRecentPanelNavigation()` 的 `selectOnRight` 指向 `_saveSlots[0]`。

### 2.3 LoadSlotPanel（讀檔面板的每一格）[src: LoadSlotPanel.cs；prefab: sharedassets1 #467]
```
string _slot = "001"; Text _slotText; Text[] _titleText; Text[] _timeText;
Button _button; Button _deleteButton; GameObject _focusObj; GameObject _newGamePlusIcon;
bool _notLoadDemo; LoadGameSlot _loadGameSlot; GameSave _currentData (private)
public Button SlotButton; public Button DeleteButton; public bool HasSaveData
```
- prefab 上的實際值：`_slotText` = `Text@Header`；`_titleText` = [Normal/Title, Hover/Title]；`_timeText` = [Normal/Time, Hover/Time]；`_button` = `Button@Slot`；`_deleteButton` = `Button@Delete`；`_focusObj` = `Focus`（Image，就是「目前槽」的高亮）；`_newGamePlusIcon` = `NewGamePlus`（Image，就是那顆星）；`_notLoadDemo = 1`。
- prefab 階層：`LoadGameSlot`（470×100，anchor 在左上）底下有 `Slot` [Button, MenuToggleButton, Image, LoadGameSlot] 和 `Delete` [Button, MenuToggleButton]（80×80，位置 x=484）。
  `Slot` 底下有 `Focus`、`Normal`{Title, Time, Line}、`Hover`{Title, Time}、`Header`、`NewGamePlus`。
- `Slot` 按鈕的 onClick（persistent）是 `LoadSlotPanel.OnTitleClick`；`Delete` 的 onClick 是 `LoadSlotPanel.DeleteButtonClick`，它會呼叫 `LoadGamePanel.Instance.OpenDeletePanel(_slot)`。
- `SetSlot(int index)`：`_slot = index.ToString().PadLeft(3,'0'); _loadGameSlot.SetSlot(_slot);`
- `Setup()`：
  1. 設定 Header 文字為 `System/SaveSlotText` 格式化後的結果。
  2. `_currentData = SaveSystem.Instance.GetSaveData(_slot)`，然後 `SetupText`。
  3. 依是否有資料切換刪除鈕、星號和 `_loadGameSlot.IsEmpty`。
  4. `_focusObj.SetActive(SaveSystem.Instance.CurrentSlot == _slot)`。
- `OnTitleClick()`：
  1. 先 `SetSlot(_slot)`；如果 `SceneController.Instance.IsPrepare` 就直接 return（注意：這時 slot 已經被改掉了）。
  2. 有資料，而且不是「demo 存檔且 `_notLoadDemo`」的情況：`StopMusic`、`LoadGameData()`、`SaveUniverseData()`、`MissionManagerData.Instance.UpdateCheckMissions()`、`SceneController.Instance.LoadCurrentScene()`。
  3. **沒有資料時會開新遊戲**：`NewGameData()`、`SaveUniverseData()`、`UpdateCheckMissions()`、`LoadStory()`。
     遊戲內的讀檔面板用的是同一個 prefab，所以在遊戲內點空槽也會開新遊戲（推論）。
  4. **讀檔前沒有任何確認窗。**

### 2.4 SaveGamePanel 和 SaveSlotPanel [src: SaveGamePanel.cs, SaveSlotPanel.cs；prefab: sharedassets2 #1492 "SaveGameSlot"]
```
SaveGamePanel: SaveSlotPanel[] _saveSlots; int _slotCount = 20; GameObject _slotPrefab; Transform _slotContainer;
               CommonPanel _confirmPanel; Button _cancelButton; InputButton _closeInputButton;
               string _currentConfirmSlot; GameObject _currentSelectableObject   (沒有 static Instance)
SaveSlotPanel: string _slot="001"; Text _slotText; Text[] _titleText; Text[] _timeText; Button _button;
               GameObject _focusObj; StringGameEvent _confirmEvent; StringGameEvent _saveEvent;
               GameObject _newGamePlusIcon; GameSave _currentData   （沒有刪除鈕）
```
- `SaveGamePanel.Awake` 一樣會 `CreateSlot()`（同樣先 Destroy 再 Instantiate，沒有 pooling），然後 `SetButtonNavigation()`。
  SaveGamePanel 的 GameObject 在場景裡是 inactive，所以 Awake 要等第一次開啟時才跑 [scene: level2 `(inactive)`]。
- `SaveSlotPanel.Awake()` 也會呼叫 `Setup()`。所以 Instantiate 的當下會先用預設的 `_slot="001"` 讀一次 `Save_001.dat`，`SetSlot(i)` 之後再讀一次自己的檔案。**每一格會讀兩次。**
- 點擊流程：`Slot` 按鈕的 onClick 是 `SaveSlotPanel.OnTitleClick()`。有資料時 `_confirmEvent.Raise(_slot)`，沒資料時 `_saveEvent.Raise(_slot)`。
  這兩個事件是 ScriptableObject `StringGameEvent`（`OBB.Framework.ScriptableEvent`，asset 在 sharedassets2 #3749 和 #3748）。
  SaveGamePanel 的 GameObject 上掛了兩個 `StringGameEventListener` [scene]：
  - 3749（confirm）對應 `SaveGamePanel.OpenConfirmPanel(string)`：打開 `ConfirmPanel`，訊息 key 是 `System/ConfirmSave`（「確定覆蓋目前的紀錄嗎?」）。
    `MenuButton_Submit` 呼叫 `SaveGamePanel.ConfirmSaveSlot()`，也就是 `SaveGameData(_currentConfirmSlot)` 再 `UpdateSlot()`、`CloseConfirmPanel()`。
    `MenuButton_Cancel` 呼叫 `SaveGamePanel.CloseConfirmPanel()`。
  - 3748（save）對應 `SaveGamePanel.SaveData(string number)`：直接 `SaveGameData(number)` 再 `UpdateSlot()`。
  - 事件是全域的 ScriptableObject。Free 場景有兩個 SaveGamePanel 都在聽同一組事件。listener 應該只在 enable 時註冊，所以只有開著的那個會反應 (未確認；`StringGameEventListener` 在 OBB.Framework，沒有逐行看)。
- **存檔不會 `SetSlot`，也不會 `SaveUniverseData`。** 存到別的槽之後，`_currentSlot` 和 Focus 高亮都還停在原本的槽。
- `OnPanelOpen()`：`_saveSlots[0].SelectButton()`，然後對每一格 `Setup()`（全部重新反序列化）。
- 關閉：`CloseButton_02` 的 onClick 是 `CommonPanel.Show(false)`（目標是 SaveGamePanel 自己的 CommonPanel）。

### 2.5 左側欄：最近存檔和自動存檔 [src: RecentSaveSlotPanel.cs, AutoSaveSlotPanel.cs；scene: level1 LoadGamePanel/Container]
- 階層：`Container/RecentSave`（VerticalLayoutGroup，spacing 20）底下是 `GameSaveSlot_Recent/Slot` [RecentSaveSlotPanel]；
  `Container/AutoSave`（VLG，spacing 20）底下依序是 `GameAutoSaveSlot_Story`、`GameAutoSaveSlot_Free`、`GameAutoSaveSlot_Battle`，每個的 `/Slot` 掛 [AutoSaveSlotPanel]。
  它們的 `_slot` 序列化值依序是 `"auto"`、`"auto_free"`、`"auto_battle"`，`_notLoadDemo=1`，標籤是 `System/AutoSaveStory/Free/Battle`（「劇情」「養成」「戰鬥」）[scene: level1 dump 確認]。
- 標題文字的 key：`System/Menu/RecentSave`「最近存檔」、`System/Menu/AutoSave`「自動存檔」、`System/Menu/ManualSave`「手動存檔」、面板標題 `System/Menu/LoadGame`「讀取遊戲」或 `System/Menu/SaveGame`「儲存遊戲」。
- `RecentSaveSlotPanel.Setup()`：`_slot = SaveSystem.Instance.CurrentSlot`，然後 `GetSaveData(_slot)`。有資料就顯示 `傳奇{slot}`、日期和時間，沒有就顯示 `System/NoData`。
  它的 Delete 子物件在場景裡是 inactive，**最近存檔不能刪除**。
  `OnTitleClick()`：`SetSlot`、`LoadGameData()`、`UpdateCheckMissions`（只在 Story 或 Free 時）、`LoadCurrentScene()`。不會寫 universe。
- `AutoSaveSlotPanel.Setup()`：`GetAutoSaveData(_slot)`，Header 顯示 `傳奇{AutoGameSave.Slot}`，也就是擁有這份自動存檔的手動槽。
  `OnTitleClick()`：`SetSlot(_currentData.Slot)` 然後 `AutoLoadGameData(_slot)`。可以刪除（`DeleteButtonClick` → `LoadGamePanel.Instance.OpenDeletePanel(_slot)`，最後刪掉的是 `Save_auto*.dat`）。
- Title 場景的「繼續遊戲」：`TitleManager.ContinueGame()` 等於 `AutoLoadGameData("auto")`；`ContinueGameButton` 用 `ExistAutoSaveData()` 判斷是否顯示。

### 2.6 版面（ScrollRect）[scene: level1 / level2]
- `Container/Scroll View` [ScrollRect, ScrollRectAutoScroll]：`m_Vertical=1`、`m_Horizontal=0`、`MovementType=Clamped(2)`、`ScrollSensitivity=15`，有 `Scrollbar Vertical`（visibility=AutoHideAndExpandViewport）。
- `Viewport` 用 [Image, Mask]，`Slots`（就是 `_slotContainer`）上有 [VerticalLayoutGroup（padding left 15, spacing 15, 不控制子物件大小）, ContentSizeFitter（Horizontal 和 Vertical 都是 PreferredSize）]。每格 470×100，20 格的內容高度大約 2,300px。
- `ScrollRectAutoScroll` [src: ScrollRectAutoScroll.cs]：只有在 `navigateAction.action.inProgress`（手把或鍵盤導覽）時，才會呼叫 `ScrollToCenter` 捲到選取的那格。用滑鼠點擊不會自動捲動。

---

## 3. 可重用的 UI 元件

### 3.1 確認窗 / 訊息
- **沒有通用的 MessageBox 或 Dialog API**。每個面板的確認窗都是場景裡預先放好的子物件（`DeletePanel/ConfirmDeletePanel`、`ConfirmPanel/Panel`），結構都是：
  `Message`（Text + LeanLocalizedText）、`MenuButton_Submit`、`MenuButton_Cancel`（Button + MenuToggleButton + InputButton），按鈕的回呼是用 persistent UnityEvent 綁死的。
  - 想重用的話（推論）：`Instantiate` 一份現有的 ConfirmPanel GameObject，清掉 `Button.onClick` 的 persistent call（需要 `onClick = new Button.ButtonClickedEvent()`，因為 `RemoveAllListeners` 清不掉 persistent call），改掛自己的 listener，並把 Message 上的 `LeanLocalizedText` 移除或停用，再設定 `Text.text`。
- `CommonPanel` [src: CommonPanel.cs]：`[RequireComponent(typeof(CanvasGroup))]`，提供 `Show(bool show)` 和 `Show(bool show, bool invokeEvent = true)`。
  開啟時會 SetActive(true)，然後 `DOFade` 0.75 秒並設定 interactable 和 blocksRaycasts。關閉時 fade 之後**立刻** SetActive(false)，所以 fade 其實看不到 (推論)。
  `invokeEvent` 時會 Raise `_openEvent` 或 `_closeEvent`（`GameEvent[]`），並呼叫 `_openResponse` 或 `_closeResponse`。另有 `SetCanvasGroup(alpha, interactable, blocksRaycasts)`。
- `InputButton` [src: InputButton.cs]：把 InputSystem 的 action（`_actionPath`）綁到 `_button.onClick.Invoke()`，可以用 `SetActive(bool)` 暫停。
- `MenuToggleButton` [src: MenuToggleButton.cs]：Normal、Enable、Hover、Disable 四個子物件切換，加上 Wwise 音效（`SoundData _hoverSoundData/_pressSoundData`），有 `Init()`、`SetActive(bool)`、`Toggle(bool)`。
- `GameMessagePanel.DisplayMessage(string)` [src: GameMessagePanel.cs]：浮動提示文字（instantiate `_messagePrefab`，用 `FloatingText.Setup`）。可以拿來顯示「已存檔」之類的 toast，但它只存在於有它的場景 (未確認是哪些場景)。

### 3.2 文字輸入框
- 正式 UI 裡**沒有** `InputField` 或 `TMP_InputField`。整個 decomp 只找到 debug 用的：`TitleManager._scriptTestInput`、`_battleKey`、`_combatKey`（都是 `UnityEngine.UI.InputField`，放在 Title 的測試面板 `TestPanel` 底下的 `ScriptTestInput`、`BattleTestInput`、`CombatTestInput`），以及 `Mortal.Story/StoryMainUI._debugStoryKey`。
- 主角名字是固定的，遊戲沒有命名畫面。要做存檔命名的話，可以 Instantiate Title 場景那幾個 debug InputField（只存在於 Title 場景，而且外觀是 debug 用的），或者在執行期自己 `AddComponent<InputField>` 並套上遊戲字型。
  IME：uGUI InputField 在 Windows standalone 支援 IME（composition string），`Input.imeCompositionMode` 預設是 Auto (未確認實際表現)。

### 3.3 字型與 UI 框架
- 存讀檔面板全部使用 **uGUI 的 `UnityEngine.UI.Text`**（legacy Text），**不是 TMP**。只有關閉鈕裡面那個 inactive 的 `Text (TMP)` 是 TextMeshProUGUI。
- 字型（`globalgamemanagers.assets`）：
  - `#4089 DFT_S5`（字型名 `DFXingShu-B5`，華康行書）：用在 Title、刪除鈕等。這些 Text 上掛了 `LeanLocalizedTextFont`，`translationName="TextFont"`，會依語言換字型。
  - `#4090 SourceHanSerifTC-Bold`（思源宋體 TC Bold）：用在 Header（傳奇編號）和 Time，**沒有**依語言換字型。
  - 兩個都是 dynamic font，內嵌完整的 TTF（大約 9.7 MB 和 25.4 MB，`m_CharacterRects` 是空的），所以任意 CJK 文字都能顯示（推論）。
  - Title 用 BestFit（最大 40）；Time 19pt；Header 25pt，寬 120px，水平 Wrap。
- 其他 TMP font asset（不是這兩個面板用的）：`sharedassets0` 的 `DFPXingShuSC2-W5_SDF_*`、`DFT_S5_SDF_*`，`sharedassets1` 的 `Bakudai-Medium_SDF_System`，`resources.assets` 的 `LiberationSans SDF`、`TMP Settings`。
- 版本：Unity 2020.3.49f1。`Unity.TextMeshPro.dll` 的 AssemblyVersion 是 `0.0.0.0`，`UnityEngine.UI.dll` 是 `1.0.0.0`。**package 版本號沒有寫進 build**（以 2020.3 來說，一般是 TMP 3.0.x 和 ugui 1.0.0，**(未確認)**）。
- 輸入：使用 Unity InputSystem（`InputSystemUIInputModule`），導覽用 `InputActionReference`。

---

## 4. 在地化
- 用的是 **Lean Localization**（`LeanLocalization.dll`、`LeanLocalization.TMP.dll`），不是 I2。
  遊戲自己包了一層：`LocalizationManager`（MonoBehaviour 單例，放在 CoreScenes/level0）提供 `ILocaleResolver LocaleResolver`，實作是 `LeanLocalizationResolver`。
  [src: LocalizationManager.cs, ILocaleResolver.cs, LeanLocalizationResolver.cs]
  - `GetString(key)` 等於 `LeanLocalization.GetTranslationText(key)`（找不到 key 時回 `null`，因為 fallback 預設是 null）。
  - `GetCurrentLang()` 等於 `LeanLocalization.CurrentLanguage`；`SetLang(lang)` 會設 `CurrentLanguage`，進而觸發 `UpdateTranslations()`，再觸發 `OnLocalizationChanged`，最後是 `LocalizationManager.OnLanguageChanged`（static event）。
- 語言清單：`SystemSettings.LeanLanguages = { "ChineseTraditional", "ChineseSimplified", "Korean" }`。PlayerPrefs key 是 `"Systen_Language"`（原文拼錯）。
  `SystemSettings.SetLanguage(int)`；`SystemSettings.IsChineseLanguage`、`IsKoreanLanguage`。[src: SystemSettings.cs]
- 字串來源：level0 上有 147 個 `LeanLanguageCSV` 元件，讀 TextAsset（例如 `sharedassets0` 的 `System_zh-tw`、`System_zh-cn`、`System_kr`），格式是 `Key = Value`。
  已確認的 key：`System/SaveSlotText`（`傳奇{0}`）、`System/GameTimeText`（`第{0}年{1}{2}`）、`System/NoData`（`無資料`）、`System/ConfirmSave`、`System/ConfirmDelete`、`System/DeleteText`（`刪除`）、`System/AutoSaveStory/Free/Battle`、`System/Menu/{Submit,Cancel,LoadGame,SaveGame,AutoSave,ManualSave,RecentSave}`。
- UI 元件上的 `LeanLocalizedText`（`translationName`、`FallbackText`）會自動在語言切換時更新文字。程式設定的文字（例如格子內容）則要靠下一次 `Setup()` 才會更新。
- mod 自己的字串（推論）：最簡單是 mod 自己依 `GetCurrentLang()` 查一個字典，並訂閱 `LocalizationManager.OnLanguageChanged`。
  如果想走 Lean，`LeanLocalization.CurrentTranslations` 是 public static Dictionary，但每次 `UpdateTranslations` 都會清空重建，所以不建議直接塞進去。

---

## 5. 會被大量存檔槽撐破的硬限制與假設
1. **`_slotCount` 是唯一的槽數來源**：`LoadGamePanel._slotCount` 和 `SaveGamePanel._slotCount` 是 `private int`，預設 20，場景值也都是 20。程式碼裡沒有其他寫死 20 的陣列或迴圈。
   要改的話必須在各自的 `Awake()` 之前設定（例如 Harmony prefix 掛在 `Awake`），或者事後重跑 `CreateSlot` 和 `SetButtonNavigation`（兩者都是 private）。LOM_SaveSlotExpander 的做法與此相符。
2. **`_slotCount < 2` 會 IndexOutOfRange**：`SetLoadPanelNavigation` 和 `SaveGamePanel.SetButtonNavigation` 在 `i==0` 時會存取 `[i+1]`；`SetAutoPanelNavigation` 和 `SetRecentPanelNavigation` 會存取 `_saveSlots[0]`。
3. **效能**：沒有 pooling 也沒有虛擬化，每格是一個完整的 GameObject，並且要對整份 `GameSave` 做 BinaryFormatter 反序列化。
   - LoadGamePanel 在 Awake 讀 N 檔，每次開啟又讀 N+4 檔。
   - SaveGamePanel 在 Awake 讀 2N 檔（`SaveSlotPanel.Awake` 會先讀一次 `001`），每次開啟又讀 N 檔。
   - 讀取都在主執行緒同步進行。以每檔大約 100 KB 估，幾百格開一次面板可能要卡幾秒（估計值，**沒有實測**）。
4. **`_focusObj`、Recent 依賴 `CurrentSlot`**；存檔不會改 `CurrentSlot`（2.4）。
5. **Header 寬 120px**：`傳奇1000`（四位數）在 25pt 下大約 2×25+4×13≈102px，應該放得下；五位數以上或自訂長名稱會 Wrap 或被截掉 (未確認)。
6. **檔名沒有清理**（1.2）：自訂 key 不能包含 `/ \ : * ? " < > |`，也要避開 `auto*`、`universe`、`act1`。
7. **Steam Cloud**：已確認（2026-11-09 實測，見 1.2）。
   - `Save_021+.dat` 會上雲。
   - 要在遊戲執行中刪除，雲端才會一起刪；遊戲關閉時刪的，下次啟動會被下載回來。
   - （v1 位置）`LoMSaveManager/index.json` 在一次有效觀察中沒有上雲，所以名稱和分類很可能不跨電腦。v2 已改放第一層的 `LoMSaveManager.json`（2026-11-09），2026-11-10 實測遊戲結束後會上傳（remotecache 出現該條目）；另一台電腦下載未實測。
8. **`GetSaveData<T>` 沒有 try/catch**：只要一個壞檔，該格的 `Setup()` 就會丟例外，可能讓 `CreateSlot` 或 `UpdateSlot` 的迴圈中斷，後面的格子都不會更新 (推論)。mod 要大量列舉時應該自己包 try/catch。
9. `LoadGamePanel.Instance` 是 static，只保留最後一次 Awake 的 instance；`LoadSlotPanel.DeleteButtonClick` 也透過它開刪除窗。在同一場景裡另外 Instantiate 一個 LoadGamePanel 會搶走 `Instance`。
10. Legend（結局紀錄，`Legend_NNN.dat`）的槽數是由 `EndGamePanel._saveSlots` 和 LibraryPanel 決定的，跟存檔槽無關，不受 `_slotCount` 影響。

---

## 6. 可以 hook 的點（簽章，供計畫使用）
所有方法都在 `Mortal.Core`（`Mortal.Core.dll`）：
- `SaveSystem`：
  - `public void SetSlot(string)`
  - `public void SaveGameData()`、`public void SaveGameData(string)`
  - `private void AutoSaveData(string)`
  - `private void ExecuteSaveData(string saveFile, object gameSave)`
  - `public void LoadGameData()`、`public void AutoLoadGameData(string)`
  - `private void ExecuteLoadGameData(GameSave)`
  - `public GameSave GetSaveData(string)`、`public AutoGameSave GetAutoSaveData(string)`
  - `private T GetSaveData<T>(string) where T : class`
  - `private string GetSaveDataFile(string)`、`private string GetUserFolder()`
  - `public void DeleteSaveData(string)`、`public void NewGameData()`、`public void SaveUniverseData()`、`public void LoadUniverseData()`
  - `public void SaveCurrentSlotData(string)`、`public void CreateDir(string)`
  - `public string CurrentSlot { get; }`
- `LoadGamePanel`：
  - `private void Awake()`、`private void CreateSlot()`、`private void SetButtonNavigation()`
  - `public void OnPanelOpen()`、`private void UpdateSlot()`
  - `public void OpenDeletePanel(string)`、`public void ConfirmDeleteSlot()`、`public void CloseDeletePanel()`
  - `public static LoadGamePanel Instance`
- `SaveGamePanel`：
  - `private void Awake()`、`private void CreateSlot()`
  - `public void OnPanelOpen()`、`private void UpdateSlot()`
  - `public void OpenConfirmPanel(string)`、`public void ConfirmSaveSlot()`、`public void SaveData(string)`、`public void CloseConfirmPanel()`
- 每格的類別：
  - `LoadSlotPanel`：`SetSlot(int)`、`Setup()`、`OnTitleClick()`、`DeleteButtonClick()`
  - `SaveSlotPanel`：`SetSlot(int)`、`Setup()`、`OnTitleClick()`、`SelectButton()`
  - `RecentSaveSlotPanel.Setup()`、`AutoSaveSlotPanel.Setup()`
- 選單與標題：
  - `MenuPanel`：`SaveButtonClick()`、`LoadButtonClick()`（private，在 Awake 時以 AddListener 掛上）
  - `TitleManager`：`OpenSlot()`、`CloseSlot()`、`ContinueGame()`
- 其他：`SceneController.Instance.IsPrepare`、`LoadCurrentScene()`、`LoadStory()`、`LoadTitle()`；`MissionManagerData.Instance.UpdateCheckMissions()`；`SoundManager.Instance.StopMusic()`。
