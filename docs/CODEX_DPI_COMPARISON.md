# Codex 原生布局与 PetFolio 的 DPI 跟随对照

核对日期：2026-10-18。原生来源为本机已安装 Codex 26.1013.3656.0 的 `app.asar`，主进程文件 `main-C4DYk222.js` 与渲染文件 `avatar-overlay-native-page-c35ebc8e522e.js`。提取材料位于 Git 忽略的 `design/status-layout-audit/`，没有修改 Codex 安装文件。

## 结论

两者不采用相同的 DPI 实现。原生在 Electron 的逻辑坐标体系中统一处理宠物和通知布局；PetFolio 在独立 Win32 窗口中，将读取的逻辑坐标转换成物理像素，再跟随宠物。

单屏、相同桌面原点下，逻辑坐标乘缩放比例可以完成我们需要的定位换算；这不代表混合 DPI 多屏与原生等价。原生具体宠物布局代码来自桌面安装包，不能称为公开 harness 仓库中的源码。

## 原生调用链

- 渲染层通过鼠标的 screen/client 坐标发布拖动开始与移动消息。
- 主进程 `Gce` 保存抓取点相对宠物的偏移；`moveDragToPointer` 根据当前指针减去偏移得到 anchor，并选择显示器、调用 `applyLayout`。Windows 路径使用 `ele` 与 `getDisplayMatching`，不是一律以宠物中心选屏；`ele` 还处理当前指针与上次显示器范围的关系。
- `getLayoutForDisplay` 读取所选显示器的工作区及范围，将 anchor、mascotSize、traySize 交给 `ule`。宠物和通知矩形出现在同一个布局结果中，再通过 `setContentBounds` 更新窗口并向同一渲染器发送布局。
- 这一宠物布局区域中出现的 `scaleFactor` 仅用于诊断显示器信息；没有在这些布局计算中逐次把全局 x/y 乘显示器缩放比例。
- `addDisplayChangeListeners` 监听显示器增加、移除和 metrics 变化。`handleDisplayChanged` 在拖动时更新拖动的显示器信息；静止时会处理屏幕恢复、重新约束和必要的显示器切换，而不是把所有 anchor 直接乘新旧 DPI 比值。
- 保存位置包含显示器 ID、显示器范围，以及按显示器和分辨率保存的记录。`restoreBoundsForDisplay` 经 `$ce` 按新旧显示器原点差恢复位置，再重新布局。
- `sie`、`yu` 等原生窗口拖动辅助函数有 darwin 平台门槛，不能把它们作为 Windows DPI 跟随实现来照搬。

Electron 官方文档确认 `getCursorScreenPoint()` 返回 DIP；物理点与 DIP 点的转换相对于点所在显示器执行。文档也将 scaleFactor 列为 display-metrics-changed 的变化项：[screen API](https://www.electronjs.org/docs/latest/api/screen)。这些是框架坐标语义，不能据此声称已经在本机实测混合 DPI 多屏。

## PetFolio 当前实现

| 项目 | 原生布局 | PetFolio |
| --- | --- | --- |
| 宠物和通知 | 同一窗口内的共同布局 | 观察外部宠物，更新独立气泡窗口 |
| 布局坐标 | Electron 逻辑坐标 | PetState 发布物理像素 |
| 输入 | 渲染器事件与 Electron 指针坐标 | Win32 鼠标输入的物理像素 |
| 缩放查询 | 框架与显示器 API 提供统一坐标环境 | GetDpiForWindow 查询宠物窗口 DPI |
| 位置转换 | 选显示器后在逻辑空间重布局 | 原始保存坐标乘 dpi / 96 |
| DPI 改变 | 显示器事件驱动布局调整；拖动时有专门分支 | 窗口消息、轮询及鼠标入口检测；活动拖动按新旧 DPI 比例换算后重建抓取起点 |
| 多屏恢复 | 显示器记录、原点差恢复、移除与重连处理 | 尚未实现完整的混合 DPI 显示器原点映射 |
| 气泡尺寸 | 逻辑尺寸交给框架显示 | BubbleLayout 显式使用 208×104，侧边间距 12，未按宠物 DPI 显式缩放 |

对应代码：`PetStateService.UpdatePhysicalCoordinates`、`PetDragFollower.ChangeDpi`、`PetDpi.Scale`、`QuotaLabel.WndProc / Follow`、`BubbleLayout`、`PetPanelPlacement.Dock`。应用清单声明 PerMonitorV2，但这本身不能替代坐标转换或统一视觉尺寸的缩放规则。

## 需要区分的改进方向

1. 坐标准确性：混合 DPI 多屏需要知道源逻辑显示器、目标物理显示器及各自原点，不能将全局坐标一律乘目标缩放比例。拖动中切屏也需要按当前屏幕映射重建抓取偏移。
2. 视觉缩放：若要求像原生那样保持逻辑大小，气泡尺寸、字体、圆角、间距、关闭按钮和阴影应采用一致的 DPI 缩放规则。只修改气泡宽高会破坏绘制、命中和合成层之间的一致性。
3. 原生共窗与外部跟随的边界：原生不需要从另一个进程的保存位置重建实时位置；我们的鼠标跟随和松手等待落盘是外部窗口同步机制，原生布局函数不能直接消除这层需求。

本次仅调查上述缩放差异，没有修改现有 DPI 或跟随算法。本机当前检测到一块 1920×1080、100% 的显示器；混合 DPI 多屏结论来自代码和框架文档核对，未经硬件实测。
