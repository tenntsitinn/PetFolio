# PetFolio for Windows 1.3.1

將 Windows 更新整理回 main，加入繁體中文與英文產品圖，以及操作與設定圖解。macOS native 實驗保留在原分支。

此版本承接 1.3.0 的 Windows 功能；本次整理不新增 macOS 支援。

## Windows 1.3.0 功能記錄

加入系統監控、Default 外觀與托盤調整；Frosted glass 正在進行灰測。

## 本次更新

- Quota remaining 與 System usage 共用 208 × 104 px 氣泡，雙擊切換；系統模式顯示 CPU、RAM 與完整上下行網速，每兩秒採樣。
- Features 可分別停用兩種模式；Show Bubble 統一控制顯示，Refresh 更新當前模式及文字配色。
- 加入 Default 外觀，跟隨 Windows 深淺色並使用固定不透明度；Pet colour 保留寵物配色與透明度調整。
- **Frosted glass 正在灰測**：在 Appearance 選單啟用或停用背景模糊。Default 預設關閉、Pet colour 預設開啟，兩種外觀分別記住選擇。歡迎回報顯示及效能問題。
- 托盤選單加入圓角與統一排版，深淺色跟隨 Codex 外觀設定；修正選單與氣泡重疊時意外關閉的問題。
- 統一字體與進度條布局；System usage 底部顯示網速，Quota remaining 保留更新時間。

## 下載與更新

- 桌面可攜版：`PetFolio-1.3.0-win-x64.zip`。
- Codex 插件包：`PetFolio-1.3.0-plugin-win-x64.zip`。
- 更新前退出舊版；使用者資料會保留。若已開啟 **Start when Codex opens**，更新後請取消再重新勾選，以更新獨立副本。

本次僅發佈 Windows x64，需要 .NET Framework 4.8、Codex 桌面端與已登入的 Codex CLI；氣泡需要顯示 Codex Pet。執行檔沒有數位簽章，下載附件包含 SHA256 校驗檔。
