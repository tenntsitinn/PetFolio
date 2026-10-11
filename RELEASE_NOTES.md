# PetFolio for Windows 1.3.2

修正 Bubble 遮住其他應用程式的彈出面板，以及 Default 未隨 Codex 主題更新、缺少外陰影的問題。

## 本次更新

- 氣泡移動、刷新及輔助窗口顯示／隱藏時保持既有層級；被其他面板覆蓋時不喚出關閉按鈕。
- Default 跟隨 Codex 的深淺主題，在每次實際額度查詢及單擊／托盤 Refresh 時同步。主題配置缺失、無效或讀取失敗時保留上次 Codex 配色。
- Default 文字採用原生 Status Bubble 的主次文字色，不額外套用 Pet colour 的文字透明度。
- Default 與 Pet colour 共用外陰影，對齊原生 Status Bubble 的偏移、模糊及深淺色強度；主題同步、隱藏及恢復保持一致。

## 下載與更新

- 桌面可攜版：`PetFolio-1.3.2-win-x64.zip`。
- Codex 插件包：`PetFolio-1.3.2-plugin-win-x64.zip`。
- 更新前退出舊版；使用者資料會保留。若已開啟 **Start when Codex opens**，更新後請取消再重新勾選，以更新獨立副本。

僅支援 Windows x64，需要 .NET Framework 4.8、Codex 桌面端與已登入的 Codex CLI；氣泡需要顯示 Codex Pet。Frosted glass 仍在灰測。執行檔沒有數位簽章，下載附件包含 SHA256 校驗檔。

## 驗證

通過完整本機測試，包括獨立彈窗層級、Codex 配置與額度刷新、真實桌面陰影像素、材質切換及功能生命周期。
