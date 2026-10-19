# 多屏开发记录

2026-10-19，开发基线 1.4.3；当前验收设备只有单屏。新跟踪链路已进入本地测试构建，本轮准备发布 1.5.0 测试版。

## 范围与设计

实际技术栈为 C# / WinForms / Win32 / .NET Framework 4.8。沿用原生 Pet 的 DIP 坐标与拖动布局规则，用 Windows API 实现平台边界，不引入 Electron，也不迁移到 C++。

PETFOLIO_MULTIMONITOR_HANDOFF.md 补充的范围已采纳：PetFolio 只跟随 Pet，不修复宿主拖拽、不自行恢复 Pet 位置、不写 Codex 状态。原生 `$ce` 位置恢复只作为调查背景，先前试写的 RestoreAnchor helper 已移除。显示器移除后以宿主新发布的位置为准。

后续外观迭代已加入 0–100% 滑块和菜单整理，Default 禁用毛玻璃。气泡的整体视觉缩放尚未统一，本轮完成的是宠物跟踪坐标链路。

## 当前运行链路

1. `WindowsPetDesktop` 直接枚举 Windows 显示器，读取物理范围、工作区和 DPI，避免 WinForms Screen 缓存残留。多屏按各显示器读取 DPI，不依赖可能滞后的宠物 HWND DPI；单屏保留已有窗口 DPI 查询行为。
2. `PetDesktopSnapshot` 以主屏为根，按相邻显示器关系建立逻辑桌面，处理左右、上下、负坐标、边缘对齐、错位排列和串联屏幕。按 Chromium 的 enclosing rectangle 规则计算范围与工作区内边距。
3. `PetStateService.ReadAnchor` 读取宿主保存的 x/y 和可选 displayBounds。本机当前记录已核实包含 x/y、displayId 和 displayBounds；本实现以几何范围匹配，未把 Win32 设备名称当作 Electron displayId。
4. 静止时优先匹配保存的逻辑显示器范围；旧记录没有 displayBounds 时才按 DIP 点选择显示器。存在 displayBounds 但与当前桌面不匹配时，隐藏气泡并报告 display-position-unavailable，等待宿主更新，不猜位置。
5. 鼠标物理输入先按所在屏幕转换为 DIP（指针向下取整），进入 `PetLogicalDrag`。抓取偏移、边界约束及 24 DIP 选屏缓冲均在逻辑空间处理。
6. 最后通过 `物理原点 + round((逻辑位置 - 逻辑原点) × DPI / 96)` 发布 PetState；尺寸单独转换。QuotaLabel 继续消费物理坐标，其已有左右停靠行为不变。

显示器快照缓存一秒；显示变化通知、窗口 DPI 变化和目标窗口变化立即刷新。读屏失败也限速重试，不在 16 ms 跟随循环中反复枚举。失败时清除不可靠映射并隐藏气泡，诊断为 display-unavailable。

现有松手后最多 2000 ms 等待宿主保存的机制保留，只是其位置改为 DIP。不会增加一套持久化位置；不再使用旧 ChangeDpi 全局像素缩放。显示器移除或等待落盘期间逻辑原点发生变化时，取消过时预测，等待宿主记录。

## 原生对照与适配限制

原生 Pet 证据来自本机 Codex 26.1013.3656.0 安装包，不是公开 harness 仓库。`ele` 的 24 DIP 缓冲是选屏规则，不是 Bubble 左右停靠的中线滞回。`Wp` 使用工作区左/上/右和屏幕底边；本产品继续保留底部 8 + 32 DIP 预留。

平台原点和相邻边偏移参考 Chromium [screen_win.cc](https://chromium.googlesource.com/chromium/src/+/master/ui/display/win/screen_win.cc) 与 [scaling_util.cc](https://github.com/chromium/chromium/blob/main/ui/display/win/scaling_util.cc)。本轮实现了相邻屏幕树的核心规则，**不声称与所有 Electron/Chromium 版本完全等价**。尚未复刻复杂布局的重叠消解，也未核定当前宿主 Chromium 的精确版本及特性开关。

断开的屏幕布局、重复镜像矩形、生成重叠逻辑矩形的复杂布局目前明确拒绝映射。气泡会隐藏并给出诊断；不将这些场景描述为已支持。未来是否扩展须依据实际需求与宿主证据。

DPI 读取沿用项目既有 GetDpiForMonitor 路径；[微软文档](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)列出进程 awareness 对返回值的影响，也提示 per-monitor 线程优先使用 GetDpiForWindow。当前清单是 PerMonitorV2；各屏 DPI 和自定义缩放仍须双屏实机核验。

## 验证

- PetDisplayCoordinatesTests：1506 项坐标断言。
- PetLogicalDesktopTests：20 项 DIP 行为检查。
- PetDesktopSnapshotTests：19 项显示器布局检查。
- PetMultiMonitorTrackingTests：16 项服务集成检查，覆盖保存位置到发布位置、物理指针到 DIP 拖动、跨屏 DPI、24 DIP 边界、松手待落盘、负坐标、缓存与失败限速、拔屏等待宿主更新，以及不写宿主文件。
- 已用本地安装包的纯几何函数核对选屏与约束的关键值；未执行整个客户端脚本。调查脚本位于 Git 忽略的 design/status-layout-audit。
- 单屏既有跟踪、点击、窗口层级及材质测试保留。旧 ChangeDpi 的测试随旧运行路径移除，DPI 变化改由新坐标和跟踪集成测试覆盖。

此前 BubbleSettingsTests 的失败已定位为沙箱内 File.Replace 无法替换临时文件；沙箱外可通过。设置测试仅补充诊断，没有放宽断言或修改设置逻辑。

最终沙箱外 `test.ps1` 全套通过（包括 38 项既有宠物跟踪、16 项多屏服务集成、123 项点击、153 项窗口层级检查）。`git diff --check` 通过。开发构建输出为 `.test-build/multi-screen/PetFolio.exe`，没有覆盖正在使用的程序。

## 剩余工作与验收

1. 双屏真机：左右互换、上下布局、100%/150%、125%/200%、快速跨屏往返、边缘松手、任务栏移位、拔插和运行中缩放变化。当前只有单屏，模拟测试不替代实机结论。
2. 验证当前宿主版本的显示器逻辑范围与本适配层输出一致，尤其是错位排列、特殊缩放与三屏布局。
3. 如多屏版本要求保持统一逻辑视觉尺寸，再统一气泡字体、尺寸、间距、圆角、关闭按钮、阴影和命中区域；不能只缩放窗口宽高。

后续菜单迭代与原有关闭按钮配色改动共同纳入 1.5.0 测试版；本文此前测试数量是多屏阶段记录。
