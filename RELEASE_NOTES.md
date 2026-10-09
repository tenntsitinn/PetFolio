# PetFolio for Windows 1.2.0

Windows 正式版：新增可選的 Codex 自動啟動功能。

## 新功能

- 在托盤選單勾選 **Start when Codex opens**，即可讓 PetFolio 隨 Codex 桌面端開啟而啟動；預設關閉，安裝插件不會自行啟用。
- 啟用後，Codex 關閉時 PetFolio 會退出；登入時啟動的輕量監看器會等待下一次開啟 Codex。
- 手動退出 PetFolio 或使用插件停止後，本次 Codex 工作階段不會立即重新拉起；重新開啟 Codex 後才會恢復。
- 監看器使用獨立程式副本，避免插件快取路徑變動使自動啟動失效。取消勾選即可移除登入啟動項。
- 插件下載包附帶 PetFolio 圖示。

## 下載與使用

- **一般桌面使用**：下載 `PetFolio-1.2.0-win-x64.zip`，解壓縮後執行 `PetFolio.exe`。
- **Codex 插件使用**：下載 `PetFolio-1.2.0-plugin-win-x64.zip`，依包內 README 安裝。
- 更新前先退出舊版；使用者資料保留。若已啟用自動啟動，更新後取消再重新勾選一次，以更新監看器副本。
- 卸載前請取消自動啟動選項，否則獨立監看器副本仍會運作。

需要 Windows x64、.NET Framework 4.8、Codex 桌面端與已登入的 Codex CLI；額度氣泡需要顯示 Codex Pet。自動啟動辨識目前的 Codex 桌面程序，不會將 CLI 額度查詢程序視為 Codex。

本次只發布 Windows 下載包；macOS native 仍在實驗階段，未包含在此 Release。執行檔目前沒有數位簽章，可使用附帶的 `.sha256` 檔核對下載完整性。
