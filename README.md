# PetFolio

**圍繞 Codex Pet，增加實用功能與互動的 Windows 桌面伴侶工具。**

PetFolio 讓桌面寵物成為工作時有用的小夥伴。第一個功能 **Quota Bubble** 會在你已有的 Codex Pet 旁顯示剩餘額度，跟隨寵物移動，並提供即時刷新與外觀調整。

可擴充性是專案的基本要求：應用宿主、共用寵物狀態與個別功能已分開，後續功能可以沿用同一套追蹤與生命週期管理。

> 目前是開發中的獨立 Windows 程式，尚未封裝為可安裝的 Codex 插件。應用程式名稱為 PetFolio，Quota Bubble 是其中的額度功能。

[快速開始](#快速開始) · [使用方式](#使用方式) · [開發與擴充](#開發與擴充) · [已知限制](#已知限制)

## 目前功能：Quota Bubble

- **查看剩餘額度**：透過 Codex CLI 的 `account/rateLimits/read` 查詢，以回傳的計算週期顯示剩餘百分比；充值積分不混入訂閱額度。
- **自動與手動刷新**：啟用時立即查詢，之後每五分鐘刷新；單擊氣泡可立即更新。查詢中保留已有數值，失敗時保留最近一次結果並標示更新失敗。
- **跟隨與定位**：拖動寵物時氣泡持續跟隨；拖動氣泡可選擇寵物左上、右上、左下或右下方位。靠近螢幕邊緣時自動調整，空間恢復後回到偏好方位。
- **配色與玻璃背景**：配色優先使用寵物源圖快取，無有效快取時嘗試從寵物視窗取色。支援圓角、陰影及 10%～100% 背景不透明度；玻璃初始化失敗時回退為半透明背景。
- **減少操作干擾**：氣泡不搶走編輯器焦點，透明圓角及背景、陰影區域可讓滑鼠穿透；寵物隱藏或關閉時，氣泡也會隱藏。
- **獨立功能開關**：可暫時隱藏氣泡，或停用整個額度功能；透明度與相對方位會保存在本機。

額度來源是 **CLI 登入帳號**。目前不會檢查它是否與 Codex 桌面端帳號一致，使用多帳號時請自行確認。

## 快速開始

### 環境需求

- **Windows x64**。目前依賴 Windows Forms、Win32 與 Windows Composition，沒有 macOS 或 Linux 實作。
- 已安裝 Codex 桌面端，並已開啟可見的 Pet。
- 已登入、可回傳額度資料的 Codex CLI（`codex.exe`）。查詢額度需要 CLI 能連線至服務。
- .NET Framework 4.8（一般 Windows 10／11 已具備）。從原始碼建置另需 PowerShell、`build.ps1` 指定的編譯器、組件與 Windows WinMetadata。
- 解壓後的程式資料夾可寫入，供儲存本機設定、快照與診斷紀錄。

目前使用腳本直接編譯 C#，沒有 `.csproj` 或 NuGet 還原步驟。一般建置與執行不需要 Python；Windows 版本及不同安裝環境的相容性仍需驗證。

### 下載即用（推薦）

1. 前往 [最新版本下載](https://github.com/tenntsitinn/PetFolio/releases/latest)，下載 Assets 中的 `PetFolio-版本號-win-x64.zip`。
2. 解壓到可寫入的資料夾，開啟 Codex 並顯示 Pet。
3. 雙擊解壓後的 `PetFolio.exe`。可為它建立桌面快捷方式，图標已內嵌。

不需編譯、Python 或執行啟動腳本。GitHub 自動提供的 **Source code** 壓縮檔只包含原始碼，請下載上面的 Windows 包。請先解壓，勿在 ZIP 裡直接執行。系統匣選擇 **Exit** 即可退出。

若找不到 Codex，程式會顯示啟動提示；使用自訂路徑時可在終端指定：

```powershell
.\PetFolio.exe 'C:\path\to\codex.exe' 'C:\path\to\.codex'
```

下載附帶的 `.sha256` 可與 `Get-FileHash .\PetFolio-版本號-win-x64.zip -Algorithm SHA256` 比對。目前執行檔沒有數位簽章。

### 從原始碼啟動

下載原始碼後，在專案根目錄開啟 PowerShell：

```powershell
.\build.ps1
.\run.ps1
```

建置會產生 `PetFolio.exe`，並嵌入 `assets/PetFolio.ico` 作為程式及系統匣圖標。啟動後，系統匣會出現標示為 **PetFolio** 的圖示；寵物可見且被成功識別時，旁邊會出現額度氣泡。快捷方式也可選用此 ICO 檔作為圖標。

日常使用也可雙擊根目錄的 **Open PetFolio.vbs**。它會隱藏命令視窗，並在執行檔不存在時自動建置；需要系統允許執行 VBScript。若已有執行個體，再次啟動會啟用額度功能並恢復氣泡，不會另開一份。修改原始碼後須重新執行 `build.ps1`，啟動器不會自動重建已存在的執行檔。

### 指定 CLI 與桌面資料目錄

程式優先從正在執行且位於 `OpenAI\Codex\bin` 的 `codex` 程序找出 CLI，其次搜尋 `%LOCALAPPDATA%\OpenAI\Codex\bin`，最後查詢 `PATH`。找不到時，可明確指定路徑。`run.ps1` 也使用同一套程式內的偵測。

以下兩個路徑都是佔位範例，執行前請替換為自己的實際路徑：

```powershell
.\run.ps1 -CodexExecutable 'C:\path\to\codex.exe' -DataDirectory 'C:\path\to\.codex'
```

`-DataDirectory` 應指向包含 `.codex-global-state.json` 與 `config.toml` 的 **桌面端資料目錄**。自動偵測會先檢查 `CODEX_HOME`，再檢查 `%USERPROFILE%\.codex`；判斷依據是 `.codex-global-state.json` 是否存在。腳本的兩個路徑參數須一起提供。自訂安裝建議明確指定。

**`-DataDirectory` 只決定寵物狀態的讀取位置，不會設定 CLI 子程序的 `CODEX_HOME` 或切換其登入帳號。** CLI 子程序沿用啟動環境。

### 停止

在系統匣選擇 **Exit**，或於專案根目錄執行：

```powershell
.\PetFolio.exe --stop
```

程式不會自行加入開機啟動。

## 使用方式

| 操作 | 效果 |
| --- | --- |
| 單擊氣泡內部 | 立即刷新額度；查詢中重複點擊不會增加請求 |
| 拖動氣泡 | 選擇相對於寵物的方位，放開後以彈簧動畫吸附 |
| 拖動時按 Esc | 取消本次氣泡拖動 |
| 系統匣 `Refresh quota` | 手動刷新額度 |
| 系統匣 `Opacity` | 以 10% 為間隔調整背景不透明度 |
| 懸停後點擊左上角 × | 隱藏氣泡，背景額度刷新繼續執行 |
| 系統匣 `Show Quota Bubble` | 恢復被隱藏的氣泡 |
| 系統匣 `Features → Quota Bubble` | 停用／啟用額度功能；停用會釋放視窗、停止刷新並終止在途查詢 |
| 系統匣 `Exit` | 結束整個應用程式 |

滑鼠移動達到 4 px 才視為拖動；拖回原點也不會誤觸刷新。取消拖動、失去滑鼠捕獲、在圓角主體外放開或點擊 ×，均不觸發刷新。

更新時顯示 `Updating...`，成功後顯示更新時間。若已有結果但查詢失敗，顯示 `Failed · last HH:mm`；首次查詢失敗則顯示重試提示。舊數值不代表當前額度。

隱藏及功能停用狀態僅限目前執行階段，重新啟動預設顯示並啟用額度功能；背景不透明度與偏好方位則會保留。

## 本機資料與設定

PetFolio 讀取 Codex 的寵物設定與視窗狀態，不修改桌面端設定或寵物素材。額度查詢透過 CLI app-server 子程序進行，不會啟動模型任務。拖動追蹤使用滑鼠觀察 hook，不攔截或合成使用者輸入。

以下檔案儲存在程式所在目錄，已列入 [.gitignore](.gitignore)：

| 檔案 | 內容 |
| --- | --- |
| `appearance.json` | 背景不透明度與偏好方位 |
| `pet-palettes.json` | 寵物配色、寵物 ID、源圖路徑及有效性資訊 |
| `quota-snapshot.json` | 最近一次成功查詢的額度快照 |
| `probe-events.jsonl` | 額度、寵物切換、位置及錯誤等診斷事件 |

分享原始碼或回報問題時，請避免附上含個人路徑、寵物識別資訊或帳號使用資料的原始快取與紀錄。診斷紀錄目前以附加方式寫入，尚未實作自動輪替。

### 可選：產生寵物配色快取

沒有快取仍可啟動；建立快取可以優先使用源圖配色，避免依賴視窗取色。此步驟需要 Python、Pillow 與 NumPy，一般執行不需要它們。

在專案根目錄執行；先將範例中的安裝包與資料目錄換成實際路徑：

```powershell
python -m pip install Pillow numpy
python .\build-pet-palettes.py --asar 'C:\path\to\resources\app.asar' --home 'C:\path\to\.codex'
```

腳本讀取本機安裝包精靈圖、自訂寵物精靈圖及本機保存的雲端遷移對照，產生 `pet-palettes.json`。快取包含源檔路徑、大小與修改時間，不包含圖片本身。客戶端升級、源圖變更或新增寵物後可重新產生；下一次需要解析寵物配色時會重新讀取快取。源檔不匹配的舊快取不會被採用。

## 開發與擴充

目前以 **單一程序內的顯式功能註冊** 組織專案：

| 層次 | 主要檔案 | 職責 |
| --- | --- | --- |
| 應用宿主 | `Program.cs`、`CompanionApplication.cs` | 單一執行個體、訊息迴圈、系統匣與功能註冊 |
| 功能生命週期 | `ICompanionFeature.cs`、`QuotaFeature.cs` | 功能的啟用、停用、恢復與資源釋放 |
| 共用寵物狀態 | `PetStateService.cs` | 視窗識別、寵物位置、可見性與配色狀態 |
| 額度資料 | `QuotaData.cs`、`QuotaService.cs`、`CodexQuotaSource.cs` | 型別化快照、刷新狀態、CLI 協定與子程序 |
| 額度視窗 | `QuotaLabel.cs`、`CloseBubbleButton.cs` | 內容顯示、點擊、拖動及關閉操作 |
| 跟隨與配色 | `PetDragFollower.cs`、`PetPanelPlacement.cs`、`PetTheme.cs`、`PetColourSwitch.cs` | 位置解析、吸附、取色與過期結果丟棄 |
| 平台與效果 | `Native.cs`、`GlassBackdrop.cs`、`BackdropGaussian.cs`、`ShadowBackdrop.cs` | Windows 互操作、玻璃與陰影 |

新增功能應實作 `ICompanionFeature`，由宿主註冊，需要寵物狀態時使用共用的 `IPetStateSource`，避免重複建立輪詢或滑鼠 hook。功能必須能反覆啟停，並在停止時清理訂閱、計時器與非同步工作。

完整資料流、資源所有權及新增模組約定見 [ARCHITECTURE.md](ARCHITECTURE.md)。目前沒有動態第三方插件載入、插件 SDK 或多面板占位協調；這些不屬於已支援能力。後續實用功能與互動會沿用現有邊界，依實際需求擴充。

### 測試

在專案根目錄執行：

```powershell
.\test.ps1
```

腳本在 `.test-build` 建置並執行配色、寵物切換、拖動跟隨、相對位置、額度模型／服務／stdio 協定、功能生命週期及單擊刷新測試，結束後清理本次產生的測試執行檔。

協定測試使用本機假 CLI，不連接真實帳號或網路。部分測試建立獨立驗證視窗，不操作 Codex 客戶端；診斷紀錄可能留在測試目錄。有本機配色快取時，測試也會核對源圖有效性。

若程式正在執行，可將建置輸出放到獨立目錄，避免覆蓋正在使用的執行檔：

```powershell
.\build.ps1 -OutputDirectory .test-build
```

需要檢查實際玻璃、滑鼠命中及隱藏／恢復效果時，可手動執行：

```powershell
.\build-glass-verification.ps1 -OutputDirectory .test-build
.\.test-build\GlassVerification.exe
```

此工具會短暫顯示測試背景，並在輸出目錄產生驗證截圖；不查詢真實額度，也不修改 Codex 設定。自動化測試不能取代真實拖動、重啟後設定保留、混合 DPI 多螢幕或客戶端升級後的現場驗證。

### 設計與診斷

目前額度面板為 190 × 96 px，採 32 px 圓角，位於寵物左右兩側並保留 12 px 間距；上下區域留給原生 Status Bubble 與 Action Bar。跟隨計時器設定為 16 ms，這不是固定幀率保證。

[design/](design/) 保存選定的設計素材。[描邊設計稿](design/quota-bubble-outline-preview.png) 與 [SVG 原稿](design/quota-bubble-outline-preview.svg) 是設計過程資料，並非目前版本的實機截圖；部分文字與版面已落後於程式實作。應用使用 [PetFolio 圖標原稿](design/petfolio-symbol-v12.svg)，程式及系統匣已嵌入書本、爪印與黃色書簽圖標。

`Inspect.cs` 與 `inspect-windows.py` 是保留的唯讀視窗診斷工具原始碼，日常使用不需要執行。

## 已知限制

- **依賴客戶端內部實作**：寵物視窗識別、選擇及保存位置依賴內部配置鍵、程序與視窗特徵，可能隨 Codex 更新而失效；目前只在找到唯一符合條件的視窗時綁定。
- **帳號尚未對齊驗證**：顯示 CLI 帳號的額度，不保證與桌面端帳號一致，也不是逐次消耗的即時串流。
- **Windows 相容性待擴大驗證**：玻璃使用內部 host-backdrop 介面，不同 Windows 版本與混合 DPI 多螢幕行為仍需實測。
- **定位並非原生事件整合**：沒有訂閱 Status Bubble 的內部位置事件，客戶端特殊吸附動畫或布局變更仍可能需要適配。
- **目前提供免安裝 ZIP**：沒有安裝器、自動更新或開機自啟動；升級時退出舊版再解壓新版，可自行保留本機 `appearance.json`。

### 維護版本與下載包

版本以 `VERSION` 為準，需同步 `Program.cs` 的兩個組件版本。提交描述以三段版本號開頭，初始為 `1.0.0`；未明確指定新 version 時保持首位，較多變更升第二位，較少變更升第三位。

執行 `./package.ps1` 可在 `dist` 建立免安裝 ZIP 與 SHA256；包內只包含 EXE、ICO、README 和 VERSION，不帶本機設定、額度資料或配色快取。推送與 VERSION 相同的版本標籤後，GitHub Actions 會先測試，再建置並發布 Release。首次發布說明位於 `RELEASE_NOTES.md`，後續發布應同步更新。

## 常見問題

| 情況 | 檢查方式 |
| --- | --- |
| 顯示 `Codex CLI was not found` | 用 `-CodexExecutable` 指定真正的 CLI `codex.exe` |
| 顯示 `Codex desktop data was not found` | 先在 Codex 開啟 Pet，確認 `-DataDirectory` 包含 `.codex-global-state.json` |
| 系統匣有圖示但沒有氣泡 | 確認寵物可見、功能已啟用，並選擇 `Show Quota Bubble`；仍無法顯示時檢查客戶端版本與視窗識別 |
| 額度讀取失敗或與桌面端不同 | 確認 CLI 登入帳號、啟動環境與網路狀態，再點擊氣泡重試 |
| 找不到編譯器或參考組件 | 對照 `build.ps1` 中的 Framework、GAC 與 WinMetadata 路徑；目前建置腳本尚未自動適配其他安裝位置 |
| PowerShell 拒絕執行腳本 | 依所在環境的執行原則處理；受管理裝置需遵循其政策 |

回報問題時，請附上 Windows 與 Codex 版本、重現步驟、是否使用多螢幕及縮放比例；如需診斷紀錄，只提供已去除個人資訊的相關片段。

## 授權與參考

目前原始碼目錄尚未提供 `LICENSE`，授權條款待確定。

- [Codex app-server](https://learn.chatgpt.com/docs/app-server)：額度查詢協定參考。
- [Windows Forms 與 Visual Layer](https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-windows-forms)：Windows Composition 整合參考。
- [Direct2D 高斯模糊](https://learn.microsoft.com/en-us/windows/win32/direct2d/gaussian-blur)：背景模糊參考。
- [Adaptive Tab Bar Colour](https://github.com/atbc-org/Adaptive-Tab-Bar-Colour/blob/main/src/utils/colour.ts)：`PetTheme.cs` 註明其源色與對比修正分離思路受此啟發，取色程式為獨立實作。
