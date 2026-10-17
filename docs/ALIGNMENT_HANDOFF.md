# PetFolio：Bubble 对齐问题协作交接

记录日期：2026-10-12。本文可直接交给测试及修复合作者。目标是记录另一台电脑上的真实错位，定位原因，并在合作者自己的分支完成修复。

## 先读这一页

- 产品短期保持两项功能：**Quota remaining** 和 **System Usage**，共用一个可切换内容的 Bubble。
- Default / Pet colour 是两种外观，不是两项功能。外观跟随 Codex；Default 和 Pet colour 共用与原生 Status Bubble 对齐的阴影设计。
- 已知事实：合作者电脑复现了 Bubble 对齐位置问题。尚未拿到完整的复现步骤、环境和偏移数据，**未确认根因是 DPI、多显示器、拖动同步还是边界计算**。
- 合作者负责对齐修复；维护者后续做性能与代码优化。双方改动 `QuotaLabel.cs`、`PetStateService.cs`、`PetDragFollower.cs`、`PetPanelPlacement.cs` 前先说明范围，避免同时改同一条定位路径。
- 第一轮交付只需：准确版本、最短复现步骤、一段复现录像、当时的屏幕配置和相关日志。先保存原始现场，再用干净设置对照。

## 1. 固定测试版本

以下是本文编写时的基线，后续以实际 commit 和 EXE 哈希为准：

| 基线 | 标识 | 含义 |
| --- | --- | --- |
| 已发布程序 | Release `1.3.2`；commit `e012043ac2370319741f5bff9209c00546b14294` | 适合复现用户正在使用的正式版本 |
| main 的文档更新 | `226ff4d158ca1ba04375d27554e7e0898f3ca974` | 提交标题以 1.3.3 开头，但只改 README，程序版本仍是 1.3.2；不是新的程序 Release |
| 开发基线 | `codex/ui-responsibility-and-position-latency`；`36ef26d13ae8803bb5c93bd509a34aa77103a17c` | 程序 1.4.1，含文件通知、职责拆分、格式整理和新增诊断，尚未发布 |

先记录出问题的 EXE 来自哪里、实际执行路径、文件版本和 SHA256。只报告“最新版”无法区分这些基线。若已有修复分支，保留它并记录起点，不必为了本文重建。

新开始修复时，建议从固定开发基线建自己的分支，以减少之后与格式整理、职责拆分的冲突：

```powershell
git clone https://github.com/tenntsitinn/PetFolio.git
Set-Location PetFolio
git switch -c codex/fix-bubble-alignment 36ef26d13ae8803bb5c93bd509a34aa77103a17c
git rev-parse HEAD
git status --short
```

若只在 Release 1.3.2 上复现，先保留该版本的现场；在相同环境、相同设置下再测开发版。文件通知可能改变症状出现的时间，不能把两个版本的日志混作一次复现。

## 2. 最小复现记录

复制并填写：

```text
报告编号 / 当地时间及时区：
PetFolio commit / EXE 文件版本 / SHA256：
实际运行 EXE 路径 / 启动方式（快捷方式、插件、手动）：
Codex 桌面版本（About 页面）：
Windows 版本及 build：
显示器数量、每台分辨率 / 缩放百分比 / 主屏：
显示器排列（副屏在左、右、上；是否有负坐标）：
任务栏位置、自动隐藏：
是否刚经历睡眠恢复、远程桌面或显示器插拔：
Pet（内置 / 自定义）、Pet 尺寸：
内容：Quota remaining / System Usage：
外观：Default / Pet colour；透明度；Frosted glass：
偏好方位：top-start / top-end / bottom-start / bottom-end：
Codex 数据目录是否自定义 / PetFolio 数据目录是否自定义：
最短步骤（启动 → 操作 → 错位）：
期望结果 / 实际结果：
偏移方向、约多少像素；持续偏移还是拖动时滞后：
松手后立即、约 0.5 秒、超过 2 秒时分别怎样：
手动刷新、切换模式、重新显示或重启是否恢复：
复现次数 / 尝试次数：
```

录像建议保留鼠标、Pet、Bubble 和原生 Status Bubble，包含出错前后各几秒。配一张 Windows 显示设置的排列及缩放截图。若方便，保存未缩放的局部截图，标出比较的是气泡主体边缘、阴影还是字符；透明窗口边界不一定等于可见 Pet 边缘。

优先测试实际触发问题的操作，每次只改变一个条件；无须一开始穷举所有组合：

| 场景 | 观察 |
| --- | --- |
| 屏幕中央，启动后静止 | 是启动就错，还是操作后才错 |
| 拖动 Pet，松手并等待超过 2 秒 | 区分拖动估算、保存坐标回写与稳定位置 |
| 拖动 Bubble 到四个方位 | 区分偏好方位与实际避让方位 |
| 靠近屏幕四边，然后移回中央 | 是否正确避让、是否回到偏好方位 |
| 跨屏拖动，尤其不同缩放的屏幕 | 偏差是否在跨屏后出现、是否随比例变化 |
| 切换两个内容模式及两种外观 | 是否只发生在某种尺寸、材质或图层上 |
| Pet 隐藏再显示、睡眠后恢复 | 生命周期变化是否触发错位 |

设备只有单屏时，标记跨屏场景“未验证”，不要把模拟坐标测试写成真实多屏通过。

## 3. 构建及确认运行对象

使用 Windows x64 和项目现有 PowerShell 构建脚本；不需要安装 .NET SDK。`build.ps1` 使用系统 .NET Framework 编译器、Windows WinMetadata 与指定程序集；缺失时保留具体错误。

```powershell
./build.ps1 -OutputDirectory .test-build/alignment
$alignmentExe = (Resolve-Path '.test-build/alignment/PetFolio.exe').Path
(Get-Item -LiteralPath $alignmentExe).VersionInfo |
    Select-Object FileVersion, ProductVersion
Get-FileHash -LiteralPath $alignmentExe -Algorithm SHA256
& $alignmentExe --check | Out-String
Get-Process -Name PetFolio -ErrorAction SilentlyContinue |
    Select-Object Id, Path, StartTime
```

`--check` 只检查路径和依赖，不验证登录，也不验证 Pet 可见。自动发现了错误的 Codex 数据目录时，可对本节命令使用 `--check --data-directory '实际 Codex 数据目录绝对路径'`。

更换构建之前，从原实例托盘选择 Exit，并确认它已经退出。程序默认单实例：直接双击另一份 EXE，可能只是唤回原实例。开了 “Start when Codex opens” 时，也要记录是否有监看器启动了另一份程序。

然后从同一 PowerShell 启动并核对：

```powershell
Start-Process -FilePath $alignmentExe -WindowStyle Hidden
Get-Process -Name PetFolio -ErrorAction SilentlyContinue |
    Select-Object Id, Path, StartTime
& $alignmentExe --status | Out-String
```

如需自定义桌面数据目录，可改用以下启动行，替换路径示例：

```powershell
Start-Process -FilePath $alignmentExe -WindowStyle Hidden -ArgumentList '--data-directory "C:\path\to\.codex"'
```

**`--status` 的顶层 `version` 属于执行这个命令的 EXE，不是向后台进程查询到的版本。** 必须结合进程路径及该文件版本、哈希核对。开发版诊断里另有 `petfolioVersion`，但诊断文件也有生成时间，可能不是本次现场。

## 4. 保存诊断，避免先丢失现场

默认 PetFolio 数据目录是 `%LOCALAPPDATA%\PetFolio`，`PETFOLIO_DATA_DIR` 可覆盖它。Codex 的 `--data-directory` 是另一回事：它指向读取宠物状态的目录。额度子进程的账号来自继承的 CLI 环境，不能用这两个参数推断账号。

对齐问题优先收集：

| 文件 / 记录 | 能回答什么 |
| --- | --- |
| `appearance.json` | 偏好方位、外观、两个内容开关和当前模式 |
| `probe-events.jsonl` 与 `probe-events.previous.jsonl`（若存在） | 出错前后的定位、拖动、目标窗口切换和异常 |
| `pet-diagnostic.json`（开发版） | Windows / Codex / PetFolio 版本、屏幕 bounds / scale、目标识别状态 |
| 截图、录像和时间点 | 可见 Pet 与气泡主体的实际关系 |

开发版复现后，打开托盘 **Diagnostics** 可生成新的诊断文件。`--status` 只读取现有诊断，不会生成新快照。Release 1.3.2 没有这一项新增诊断，缺少该文件并非复现失败。

诊断显示 `ready` 只表示 Pet 窗口识别且可见，**不表示对齐正确**。自动诊断按识别状态变化保存；显示器变化后最好手动刷新 Diagnostics。现有诊断没有完整的每帧坐标、窗口 DPI 和各图层 bounds。

日志事件可重点看：

- `target`：绑定窗口及候选数量。
- `drag-start` / `drag-release`：Pet 拖动起止锚点。
- `panel-drag-start` / `panel-drag-end`：Bubble 自身拖动、取消与偏好方位。
- `placement`：`anchorX/Y`、实际 `labelX/Y`、`source`、`panelDragging`、`corner`、`preferredCorner` 和时间。
- `render-error`、`feature-error`、`pet-diagnostic`：相关错误或识别快照（部分事件仅开发版）。

每次位置改变会记录 `placement`，静止时不保证持续输出；日志没有新行不等于程序停止。开发版每份日志上限 2 MiB，保留上一份；长时间拖动可能覆盖早期记录，复现后及时复制两份文件。

只分享与本次复现有关的片段和设置，检查其中的个人路径与 Pet 标识。无需提供 `.codex` 全目录、登录文件、会话内容或额度快照。这是控制交接材料范围，不要求运行密钥扫描工具。

原始现场已保存后，才考虑干净设置对照。在新的 PowerShell 中，将 `PETFOLIO_DATA_DIR` 指向一个全新的绝对路径，并使用上面 `.test-build/alignment` 的构建，避免程序旁旧设置被首次迁移。先退出原实例，再启动；不同数据目录本身不会绕开单实例限制。对照结束后关闭该 PowerShell，原设置无需删除。

## 5. 定位路径和需要检验的假设

下表以开发基线为准，可在 GitHub 按固定 commit 查看；本仓库已有 [架构说明](../ARCHITECTURE.md)。

| 文件 / 入口 | 作用与排查重点 |
| --- | --- |
| `PetStateService.cs`：`ReadAnchor`、`Tick`、`MoveDrag` | 从 `.codex-global-state.json` 读取 `electron-avatar-overlay-bounds` / `open`；窗口发现；发布 Pet 状态 |
| `PetDragFollower.cs`：`Begin`、`Move`、`Resolve` | 拖动时用鼠标增量估算锚点；松手后旧保存坐标未更新时最多保留估算位置约 2 秒 |
| `QuotaLabel.cs`：`PetBounds`、`Follow` | 从 Pet 锚点选屏幕工作区，求目标位置，移动前景/玻璃/关闭按钮，并记录 placement |
| `PetPanelPlacement.cs`：`Resolve`、`Dock`、`Constrain` | 四方位停靠、溢出评分、边缘约束；偏好方位和实际方位分别保存 |
| `BubbleLayout.cs`、`app.manifest` | 固定布局常量和 PerMonitorV2 声明；声明本身不证明各坐标来源的单位已经一致 |
| `GlassBackdrop.cs`、`ShadowBackdrop.cs`、`CloseBubbleButton.cs`、`Native.cs` | 多层窗口的位置、阴影边界和显示层级 |
| `PetDiagnostics.cs` | 当前诊断内容与需要补充的观测数据 |

需要验证而非直接认定的假设：

1. 保存坐标、鼠标坐标、屏幕工作区和 HWND 坐标的单位是否相同。现有 `ReadAnchor` 直接将 JSON 的 x/y 转成整数；不能未经测量就统一乘缩放比例。
2. Pet 宽高估算是否匹配当前客户端及宠物素材。现有高度比例为 `208 / 192`；拖动边界另有底部预留 40。确认这些假设是否仍适用。
3. 跨屏选择是否一致。拖动边界按鼠标所在屏幕，Bubble 定位按锚点所在屏幕；跨越接缝时两者可能不同。
4. 松手回写时序是否导致跳回。记录估算锚点、保存锚点及松手前后时间，区分算法误差与状态迟到。
5. 是否只是某一图层错位。分别比较文字前景、玻璃主体、阴影和关闭按钮；阴影有设计上的外扩，不能按阴影边缘判断主体间距。

Pet 的透明顶层窗口可能大于可见精灵，**不要直接把整块 `GetWindowRect` 当作宠物矩形**。应将它作为额外观测，与保存锚点、素材尺寸和截图对照。

若现有数据不足，可在合作者分支加一个临时、按需的坐标诊断快照：保存锚点、解析后的锚点、Pet 尺寸、鼠标位置、选中的显示器 bounds / working area、窗口 DPI、目标/实际 Bubble 位置、各图层 bounds、偏好/实际方位和统一时间戳。此能力目前没有完整实现；先明确缺哪项再加，避免常驻逐帧打印扩大开销。

## 6. 验证与回传

现有测试入口：

```powershell
./test.ps1
```

窗口测试需要可交互桌面。它没有按套件选择的参数；`-CheckLocalPalettes` 是可选的本机配色检查，并非对齐测试开关。重点阅读 `PetPanelTests.cs`、`PetDragTests.cs`、`PetTrackingTests.cs`，涉及层级或窗口移动还要看 `BubbleZOrderTests.cs`、`TrayOverlapTests.cs`。运行中不要同时做其他性能测量。

已有测试覆盖部分负坐标、边界和拖动行为，但不能替代真实的混合 DPI、多显示器和当前 Codex 客户端验证。性能夹具结论也有限，见 [位置通知测量](FILE_NOTIFICATION_BENCHMARK.md)。该测量是保存坐标到窗口位置的等待，不证明静态对齐正确。

修复验收：

- 原来的最短步骤，修复前能复现，修复后连续多次通过，附次数和同环境对照。
- 新增一个能在旧逻辑下失败的相关回归测试；难以自动化的部分保留手工步骤和录像。
- 检查拖动、松手、四边避让及返回偏好方位；具备多屏时再检查跨屏。
- 两个内容模式、两种外观均验证；阴影/玻璃/关闭按钮跟随一致，仍不抢焦点、不盖住后来打开的弹出菜单。
- 列出未测设备与场景，避免将一台电脑上的通过推广到全部环境。

回传格式：

```text
分支 / 基线 commit / 修复 commit：
复现步骤及环境：
根因（直接证据 / 尚属假设）：
修改范围与坐标单位说明：
修复前后截图或录像：
自动测试结果 / 手动测试次数：
未验证场景、可能影响：
是否改到维护者同时优化的文件：
```

尽量让定位修复独立成提交，便于评审及按需移植。涉及新坐标换算时，在代码旁写明输入来源、单位和转换边界；大范围格式整理、性能改造与此修复分开交付。
