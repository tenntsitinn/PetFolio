# 1.0.1 修正 UTF-8 環境下額度查詢失敗

- 提供 Windows x64 免安裝包，解壓後雙擊 `PetFolio.exe`。
- 自動尋找 Codex CLI 與桌面資料；缺少必要環境時顯示提示。
- Quota Bubble 跟隨寵物，支援手動刷新、配色、透明度與位置調整。
- 套用 PetFolio 書本與書簽圖標。
- 修正 .NET Framework 在 UTF-8 主控台環境下插入 BOM，導致 CLI 通訊失敗；新增編碼回歸測試。
- README 提供完整繁中與英文說明。

下載 `PetFolio-1.0.1-win-x64.zip`，不要選 GitHub 自動產生的 Source code 壓縮檔。
需要 Windows x64、.NET Framework 4.8、Codex 桌面端、可見的 Pet 與已登入的 Codex CLI。
可用 `.sha256` 檔核對下載完整性；目前執行檔沒有數位簽章。
