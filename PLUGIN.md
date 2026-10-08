# PetFolio 插件封裝與本機驗證

第一版以操作 skill 與 PowerShell 腳本管理現有 Windows 程式。插件安裝與桌面程式啟動是兩個步驟；使用者可以在 Codex 對話中要求開啟、停止或檢查 PetFolio。

## 建置

在專案根目錄執行：

```powershell
.\test.ps1
.\package-plugin.ps1
```

輸出位於 `dist`：

- `PetFolio-<VERSION>-plugin-win-x64.zip` 與 `.sha256`：單一 `petfolio/` 頂層目錄的插件包。
- `petfolio-marketplace/`：可直接註冊的本機 marketplace，內含 `.agents/plugins/marketplace.json` 及 `plugins/petfolio/`。

包內包含根目錄 `plugin.json`、舊版相容清單 `.codex-plugin/plugin.json`、操作 skill、管理腳本及預先編譯的 Windows x64 執行檔。沒有 MCP、hooks 或第三方執行環境依賴。打包使用明確檔案清單，不包含使用者設定、額度快照或日誌。

## 安裝與使用

目前本機 Codex CLI 支援以下命令：

```powershell
codex plugin marketplace add '.\dist\petfolio-marketplace'
codex plugin add petfolio@petfolio-local
```

命令會修改當前 `CODEX_HOME` 的插件配置及快取。使用隔離的絕對 `CODEX_HOME` 可先驗證安裝，不影響日常配置。註冊後保留 marketplace 目錄；更新時重新建置並從相同來源重新安裝／刷新插件。

在桌面端的 Plugins 選擇 **PetFolio Local** 來源。若界面尚未刷新，重啟客戶端並在新對話中測試：「開啟 PetFolio 額度氣泡」「檢查 PetFolio 狀態」「停止 PetFolio」。較舊客戶端可能只識別相容清單；不同客戶端的本機來源界面與命令支援需現場驗證。

公開官方說明：[Package your plugin](https://developers.openai.com/plugins/build/plugins)。CLI 命令以本機 `codex plugin --help` 顯示的能力為準。

## 資料、升級與卸載

資料預設儲存在 `%LOCALAPPDATA%\PetFolio`，`PETFOLIO_DATA_DIR` 可指定絕對路徑。首次啟動會從 EXE 所在目錄複製舊外觀設定與配色快取，已有目的檔案時不覆寫。舊可攜版位於其他目錄時，可在停止程式後將其 `appearance.json` 與可選的 `pet-palettes.json` 複製到新資料目錄。

升級前退出 PetFolio，再更新插件。卸載前先在對話中要求停止，或執行已安裝插件內的 `scripts/stop.ps1`。之後可執行：

```powershell
codex plugin remove petfolio@petfolio-local
codex plugin marketplace remove petfolio-local
```

插件卸載保留使用者資料，也不會自動終止已啟動的桌面程式。沒有開機自啟動或安裝後自動啟動。

## 驗證範圍

`test.ps1` 覆蓋資料遷移、升級保留偏好、控制信號及原有功能測試；測試使用隔離的 PetFolio 資料目錄。`test-plugin.ps1` 對新包測試清單、ZIP 內容、腳本輸出、含空格路徑、缺少依賴時的錯誤，並透過隔離 `CODEX_HOME` 安裝與卸載插件。`-TestLaunch` 額外使用假 CLI 與假桌面資料，驗證啟動、重複啟動及停止，會建立真實 Windows UI／系統匣。測試透過獨立 `PETFOLIO_INSTANCE_ID` 隔離控制信號，不會停止日常實例；正常使用保持預設單實例即可。

這些檢查不驗證真實帳號額度、桌面 Pet 跟隨、多螢幕 DPI 或桌面 Plugins 界面的刷新。公開上架、授權條款與跨平台支援仍需另外處理。
