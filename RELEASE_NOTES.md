# 1.1.0 支援 Codex 插件啟停與升級後的 CLI 自動尋找

- 提供 Windows x64 可攜版及 Codex 本機插件包；插件透過 skill 與 PowerShell 管理啟動、恢復、停止及環境檢查。
- 外觀偏好與配色快取移至 `%LOCALAPPDATA%\PetFolio`，首次啟動遷移程式旁的舊資料；插件更新或卸載保留偏好。
- 桌面捷徑可只指定 Codex 資料目錄，自動尋找更新後的 CLI，避免綁死舊版本路徑。
- Quota Bubble 跟隨寵物，支援手動刷新、配色、透明度與位置調整。
- 增加資料遷移、單實例控制、背景啟動及插件安裝／卸載驗證。
- 新增 `PLUGIN.md` 安裝與驗證說明；目前仍只支援 Windows，尚無 MCP 控制介面。

正式發佈時可攜版檔名為 `PetFolio-1.1.0-win-x64.zip`，插件包為 `PetFolio-1.1.0-plugin-win-x64.zip`。原始碼提交不會自動建立下載包；可執行 `package.ps1` 或 `package-plugin.ps1` 自行建置。
需要 Windows x64、.NET Framework 4.8、Codex 桌面端、可見的 Pet 與已登入的 Codex CLI。
可用 `.sha256` 檔核對下載完整性；目前執行檔沒有數位簽章。
