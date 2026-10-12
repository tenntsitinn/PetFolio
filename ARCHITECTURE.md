# 架构与扩展约定

PetFolio 的长期方向是围绕 Codex Pet 提供实用功能与互动。Quota Bubble 是第一个功能模块。当前采用单进程内的显式模块注册，先维持清晰边界，再根据第二个实际功能决定进一步抽象。

## 运行结构

```mermaid
flowchart TD
    Entry[Program：单实例与入口] --> Host[CompanionApplication：消息循环、托盘、退出]
    Host --> Pets[PetStateService：共用宠物追踪与配色]
    Host --> Feature[QuotaFeature：生命周期、模式与服务协调]
    Pets -->|PetState / IPetStateSource| Feature
    Feature --> View[QuotaLabel：显示、拖动与外观设置]
    Feature --> Quota[QuotaService：刷新、旧值与错误状态]
    Feature --> System[SystemMetricsService：系统采样生命周期]
    System --> Counters[SystemMetricsSource：系统计数器]
    System -->|SystemMetrics| Feature
    Feature --> Settings[BubbleSettingsStore：偏好读取、校验与保存]
    View -->|模式、刷新、设置与可见性事件| Feature
    Quota -->|IQuotaSource| CLI[CodexQuotaSource：CLI 协议与子进程]
    Quota -->|QuotaState / QuotaSnapshot| Feature
    Feature -->|BubbleContentState / Present / PresentSystem / UpdatePet| View
```

`Application.Run` 运行宿主的 `ApplicationContext`。任何单个功能窗口都不是主窗口；停用额度功能后，托盘、消息循环和共用宠物服务继续运行。

## 职责与所有权

| 层 | 负责 | 不应引入的依赖 |
| --- | --- | --- |
| 宿主 | 注册功能、托盘、功能开关、单实例恢复信号、退出、UI 回呼调度 | 额度解析与绘制细节 |
| 宠物服务 | 唯一一份窗口发现、元数据轮询、鼠标观察、位置校正和配色 | 具体功能窗口、额度状态 |
| 额度功能 | 创建与释放自己的视窗、服务、订阅和菜单命令，管理模式与功能开关，路由刷新与保存偏好 | 宠物配置键或窗口识别规则 |
| 系统服务 | 两秒采样周期、首次采样、停止与恢复时的基线、错误恢复、发布指标快照 | 具体窗口、菜单与显示文案 |
| 设置存储 | 旧偏好读取、字段校验、默认值、完整文件原子替换 | 具体窗口、采样与模式切换规则 |
| 额度服务 | 五分钟刷新、并发请求合并、失败保留旧值、发布类型化状态 | 具体窗口及 CLI 协议 |
| CLI 来源 | stdio 握手、响应处理、超时、子进程终止 | UI、托盘、显示文案 |
| 额度视窗 | 内容显示、面板拖动、吸附、透明度、隐藏与恢复，报告用户意图与可见性 | 设置文件读写、系统采样计时器、查询子进程、宠物发现、应用退出 |

`PetState`、`QuotaSnapshot`、`QuotaState` 是只读快照。额外的额度提醒或消耗分析应使用额度模型，不应解析视窗文字。解析后的额度保留窗口时长与重置时间，积分余额不参与剩余额度计算。

所有生命周期操作、事件订阅和视窗更新在 UI 线程进行。后台查询与取色通过宿主持有的调度窗口回到 UI 线程；它们不依赖额度窗口是否可见。释放服务后，已排队的旧结果也不得发布到新实例。

宠物服务静止时每 100 ms 检查元数据与可见性，实际拖动时以 16 ms 跟随；只在状态字段变化时发布快照。`config.toml` 按修改时间及长度缓存解析，寵物尺寸和选择来自同一遍读取，配置读取失败会重试。视图拥有独立的交互计时器：可见静止时每 100 ms 更新悬停，面板拖动与吸附时为 16 ms，隐藏及托盘菜单展开时暂停。吸附不依赖重复宠物快照；显示布局／DPI 消息触发重新定位。

空闲回调直接检查元数据，避免把 100ms 计时器再叠加一个 100ms 截止条件而跳过抖动的回调；只有实际拖动的 16ms 路径按 100ms 截止时间检查元数据。五组重复测量发现并验证了这处更新连续性问题，源码快照、原始记录与限制见 `docs/CONTROLLED_BENCHMARK.md`。

位置文件与配置变化通过同一份 `FileSystemWatcher` 主动通知宠物服务，包括创建、删除和原子替换。后台通知只排队，界面线程用 16ms 单次计时器合并重复事件，失效对应缓存后更新快照；实际鼠标拖动仍走原来的快速路径。100ms 轮询保留为通知遗漏、读取到未完成写入、目录不存在或监听失败时的兜底。释放服务同时关闭监听和合并计时器，已排队回调检查释放状态。独立通知对照使用 `benchmark.ps1 -WatcherOnly`，只关闭参考构建的监听。

`DiagnosticLog` 独立同步事件写入：每份新日志上限 2 MiB，只保留当前及上一份文件，异常隔离且失败后五秒内跳过重试。`Program.Record` 不向业务传播日志 I/O 或序列化异常。`PetTrackingDiagnostic` 描述状态缺失、读取失败、位置缺失、宠物关闭、窗口缺失／歧义／隐藏等原因；宿主在原因变化时保存诊断，并提供托盘 Diagnostics 与 `--status` 读取。报告包含实际 Windows build、能识别时的 Codex 套件版本及显示器缩放，不复制用户配置或本机源文件路径。

玻璃视窗共用 UI 线程的 Composition dispatcher controller，各自释放自己的合成树、背景源、阴影和窗口。共享 controller 保留至线程／进程退出，避免反复启停或多个面板并存时重复创建。

## 三种不同操作

- **隐藏**：点击 ×；窗口、玻璃与阴影隐藏，额度仍继续刷新，托盘命令可恢复同一视窗。
- **停用内容**：托盘 `Features → Quota remaining / System usage` 独立控制两个模式。停用额度释放 QuotaService，停用 System 停止采样。共享视图保留；两项都关闭时隐藏视图并禁用 Show Bubble / Refresh。重新启用额度创建服务并立即查询。
- **退出应用**：托盘 `Exit` 或 `--stop`；释放所有功能、共用追踪器、鼠标 hook、托盘及调度资源。

`appearance.json` 保存 `quotaEnabled`、`systemEnabled` 与 `bubbleMode`；旧设置缺少开关时默认两项启用。重复启动只恢复已启用内容的气泡，不覆盖功能开关。`ICompanionFeature.FeatureOptions` 由模块持有并同步勾选，宿主负责放入 Features 菜单。只有两项都启用才提供切换；关闭当前模式切换到另一个已启用模式。Refresh 与单击使用同一入口，刷新当前模式的数据和字体颜色。

System Bubble 是同一视图的第二种显示模式，未注册第二个 feature 或创建第二个气泡。`QuotaFeature` 持有不可变的 `BubbleContentState`，统一管理 `bubbleMode`、两个内容开关及不可用模式的回退；视图只接收该状态并报告双击／托盘切换意图，保留额度快照。单击延迟到系统双击判定时间之后，刷新事件由功能层路由到当前模式的数据服务。

`SystemMetricsService` 由功能层创建与释放，仅在 System 功能启用、当前显示 System 且气泡实际可见时持续采样。隐藏、宠物消失、切回额度、停用 System 或停止功能都会停下计时器并清空采样基线。恢复时立即取首次样本，随后每两秒读取 `SystemMetricsSource`；隐藏时手动刷新只读取一次，不保留速率基线。CPU 使用 `GetSystemTimes` 差分（kernel 包含 idle），内存使用 `GlobalMemoryStatusEx`，网络按介面 ID 对字节计数和单调时间作差。额度只在 Quota remaining 启用期间保持轮询。新介面或计数器复位不会产生速率尖峰。

`BubbleSettingsStore` 沿用原有 `appearance.json` 字段，缺少字段使用默认值，无效字段独立回退；保存时先写完整临时文件，再原子替换旧文件，失败保留旧配置并记录诊断。`QuotaLabel` 接收已加载偏好，外观或拖动方位改变后报告事件；`QuotaFeature` 汇总外观偏好及权威内容状态后保存。独立视觉验证窗口使用默认偏好，不读取或保存用户设置。

## 外观策略

托盘菜单由 `TrayMenuStyle` 统一展示：使用 Windows `SystemFonts.MenuFont`，主菜单及子菜单采用 8px 圆角、4px 内边距和 28px 行高（按 DPI 缩放），统一文字、勾选和子菜单箭头列。样式复用原有 ToolStrip 项目，不修改命令、启用状态或事件处理。

菜单颜色跟随 Codex home 下 `config.toml` 的 `appearanceTheme`：dark / light 优先于系统，Codex 明确选择 system 时解析其 Windows 应用主题；配置缺失或无效时保留上次 Codex 配色。打开菜单立即检查，并每两秒同步所有已创建子菜单；读取失败保留上次主题。深浅色分别设置底色、文字、禁用项、分隔线、勾选、箭头及悬停色，不影响气泡的外观选择。

托盘根菜单打开时，宿主通知气泡暂停悬停关闭按钮、窗口跟随和桌面取色，保留最新宠物状态及数据刷新。避免关闭按钮窗口 Show 触发 ToolStrip 的 AppFocusChange，或气泡重新置顶遮住菜单。根菜单关闭后通过 UI 队列恢复，确保所有子菜单已退出；`TrayOverlapTests` 使用真实菜单与层叠气泡重复验证重叠、子菜单及恢复行为。

对其他应用的弹出面板，气泡及玻璃、阴影、关闭按钮的位置更新使用 `SWP_NOZORDER`，不反复提升置顶窗口的顺序。辅助窗口显示或隐藏前记录整组窗口的位置，操作后恢复层级并保留置顶属性；悬停还通过 `WindowFromPoint` 确认鼠标实际指向气泡或关闭按钮，避免隔着覆盖窗口唤出按钮。`BubbleZOrderTests` 使用独立置顶弹窗验证跟随、刷新、外观切换、输入归属与关闭后恢复。

外观与内容模式独立：`appearanceMode` 保存 `default / pet`，缺省为 Default。`BubbleAppearance` 保存客户端 26.1007 的浅色 `#ffffffe6` / 深色 `#202020e6`、文字与描边令牌；固定 alpha 230，使用中性灰进度条和内侧高光。Default 是半透明底色，不模糊桌面；GlassBackdrop 将 Gaussian 背景层的 Opacity 设为 0，切回 Pet colour 恢复为 1。Default 不使用桌面采样或宠物颜色。Default 与 Pet colour 共用同一 ShadowBackdrop，阴影对齐原生 Status Bubble 的 0 3px 16px 黑色阴影：Codex 浅色 alpha 51（20%），深色 alpha 89（约 35%）；两种外观均随确认的 Codex 主题同步阴影强度，切换外观不隐藏阴影。主题复用 `CodexMenuTheme.Read`：Codex home 的 `appearanceTheme` dark / light 优先，仅在 Codex 明确选择 system 时读取 Windows `AppsUseLightTheme`；配置缺失、无效或读取失败时保留上次 Codex 配色，不自行采用 Windows 主题。初始化、选择 Default、单击／托盘 Refresh、Windows 设置／主题消息及每次实际 quota 查询开始时同步；自动同步沿用五分钟 quota 周期，不另设外观轮询计时器。额度查询失败不撤销已同步的主题，隐藏时仍同步但不显示窗口；System 模式手动刷新也同步。Pet colour 保留毛玻璃、`opacityPercent` 和低透明度黑白字适配；切换外观会清除旧桌面样本。`BubbleThemeTests` 使用实际配置文件、quota 计时器和单击处理验证深浅切换、失败保留及隐藏行为；`BubbleMaterialTests` 用真实合成窗口上黑白条纹的像素对比验证材质切换、两种外观的真实外阴影像素一致性、深浅主题强度及隐藏／恢复。

## 添加下一个功能

`Appearance → Frosted glass` 独立控制背景模糊。`defaultFrostedGlass` 与 `petFrostedGlass` 保存各外观的选择，旧设置缺省为 false / true。开关直接修改背景模糊层的 Opacity，不改变底色、字体、透明度限制或外阴影规则。

1. 实现 `ICompanionFeature`，提供 `Start`、`Stop`、`Restore`、`Dispose` 和菜单命令。
2. 在宿主构造阶段显式创建并 `Register`。需要宠物状态时注入现有 `IPetStateSource`，不要再建立一份宠物轮询或鼠标 hook。
3. `Start` 应可重复调用且不重复订阅；`Stop` 应可重复调用并完整释放功能资源；停止后允许重新启动，最终 `Dispose` 后不再启动。
4. 菜单命令及 FeatureOptions 由模块拥有。内容开关独立于模块生命周期；模块 Stop 释放视图命令，Dispose 释放功能选项。宿主只负责把命令加入托盘。
5. 对跨模块边界增加行为测试：停用一个功能不能影响另一个功能，旧异步结果不能污染重启后的实例。

如果未来多个独立模块都需要额度数据，再将 `QuotaService` 提升为宿主共用服务；目前由额度功能拥有，停用即可停止查询。

## 当前边界

- 本次建立的是进程内功能边界，尚无动态加载、第三方插件 SDK 或进程级故障隔离。
- 多面板占位协调尚未实现；现有定位算法仍针对单个面板。第二个实际面板确定后再设计布局协调。
- 玻璃与阴影仍采用现有额度面板的尺寸与圆角；它们不是任意尺寸的通用组件。
- Codex 客户端内部配置键、窗口类名和进程识别集中于宠物服务及其配色辅助组件，仍需跟随客户端版本维护。
- CLI 登录账号与桌面账号的一致性、混合 DPI 多显示器行为仍需现场验证。

## 验证

`test.ps1` 覆盖配色、切换、跟随、位置、额度模型与服务、真实 stdio 假服务器、功能启停与共存。假服务器不读取账号、不访问网络；测试不会查询真实额度。

另外覆盖设置原子替换、系统采样基线、日志轮替／写入失败、元数据缓存、变化快照发布、识别诊断与无新快照时的面板吸附。`.github/workflows/ci.yml` 在 PR 和 main 推送时运行测试、独立构建及插件包验证；Release 发布仍由原有版本标签工作流负责。`profile.ps1` 为本机测量工具，CPU 数值不作为 CI 阈值；方法和原始数据见 `docs/PERFORMANCE.md`。

`build-glass-verification.ps1` 与 `GlassVerification.exe` 验证真实 Windows 玻璃、鼠标命中、关闭／恢复和背景更新。支持 `-OutputDirectory .test-build` 在隔离目录生成程序与截图。

单独验证生产构建可执行 `./build.ps1 -OutputDirectory .test-build`，无需覆盖正在运行的程序。

## Codex 插件封装

`plugins/petfolio` 提供根目录插件清单、旧版兼容清单、操作 skill 与管理脚本。`package-plugin.ps1` 将它们和预编译 Windows x64 程序打包，并生成独立本机 marketplace。脚本通过 `--status`、`--check` 和 `--stop` 管理进程；启动时等待初始化完成的会话信号，重复启动恢复现有实例。安装本身不会启动程序。

`RuntimeData` 统一解析 `%LOCALAPPDATA%\PetFolio` 或绝对 `PETFOLIO_DATA_DIR`，并从程序目录迁移旧偏好与配色元数据。插件缓存只有程序与资源；用户资料不随插件更新或卸载删除。`Program.Folder` 仍用于独立视觉验证产物，业务设置及缓存使用 `Program.DataFolder`。

`test-plugin.ps1` 验证 Windows PowerShell 5.1、中文与空格路径、缓存搬迁，以及隔离 Codex 安装／卸载。可选 `-TestLaunch` 使用假 CLI 验证实际宿主启停。当前没有 MCP 控制界面或生命周期 hooks；客户端内部宠物跟踪仍需现场兼容性测试。


正式气泡布局由 `BubbleLayout` 统一 208×104 尺寸和 25px 圆角，供前景、玻璃裁剪、阴影与命中区域使用。双列 x=20/114、进度条宽 74px，底部信息固定在 (20,82)：Quota 显示更新时间，System 使用 `SystemMetrics.Rate` 并列显示完整单位的上下行网速，不显示更新时间或单位悬停提示；超宽时只舍入数值，保留完整单位。Pet colour 辅助文字 alpha 固定为 204（80%）；Default 使用原生主次文字色。进度条使用数值快照，缺失值仅画轨道；配色 alpha 独立于玻璃透明度，按合成背景调整可见对比度。

Pet colour 的 System 模式在背景不透明度为 10% 或 20% 时随指标刷新重新采样桌面，使黑白字体不依赖五分钟额度刷新。隐藏时不采样，采样失败保留上次背景；常规透明度不增加系统刷新取样。
