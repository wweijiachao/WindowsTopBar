# WindowsTopBar 架构

解决方案包含一个 WPF 应用项目。应用通过 `StartupUri` 打开主窗口，随主窗口关闭退出。XAML 定义控件和样式，窗口代码负责交互、生命周期与模块协调，系统调用由各功能模块封装。 [应用入口](src/WindowsTopBar/App.xaml) · [主窗口布局](src/WindowsTopBar/MainWindow.xaml) · [窗口代码](src/WindowsTopBar/MainWindow.xaml.cs)

## 模块职责

| 模块 | 职责 |
| --- | --- |
| `AppBar` | 主屏幕停靠、工作区、全屏层级、显示器中点偏移、最大化和左右贴靠窗口的激活 |
| `AppStatusPanel` | 给中央入口让出空间，从右向左排列放得下的完整应用图标 |
| `UsageReminder`、`UsageSystemEvents` | 连续使用计时、手动休息和恢复，处理会话和电源事件 |
| `AppearanceSettings`、`ShortcutDirectory` | 本地设置读写、快捷方式目录解析和启动 |
| `PinnedShortcuts` | 创建 `Pinned`，分别读取根目录和固定目录，监听变更并缓存目标信息 |
| `ShellShortcut` | 在后台 STA 解析 `.lnk` 的直接可执行目标并释放 COM 对象 |
| `ShortcutApplications`、`ShortcutWindows` | 按完整路径识别运行进程，记录最近窗口，封装窗口枚举、恢复和激活 |
| `SystemStatus`、`AudioStatus`、`SystemShortcuts` | 输入法、Wi-Fi、电池、音量，以及系统快速设置与快捷键调用 |
| `MessengerWindows` | QQ、微信进程及窗口识别，接收 Shell 闪动和前台切换事件，唤出或关闭窗口 |
| `ClashVergeTray`、`ClashVergeSettings` | 运行状态、托盘提示与配置读取，执行仪表板、TUN 和退出操作 |
| `WindowBackdrop`、`ScreenColorSampler` | 原生桌面亚克力与栏底的轻微染色 |

## 窗口与布局

- 主窗口在 `SourceInitialized` 时取得 `HwndSource` 并安装消息钩子。`AppBar` 用 `ABM_NEW` 注册，经 `ABM_QUERYPOS` 和 `ABM_SETPOS` 确定主屏顶部的停靠矩形。显示器、DPI 或工作区发生变化时重新定位，收到全屏通知时调整层级。AppBar 定位失败时回退到现有工作区，正常退出时用 `ABM_REMOVE` 释放预留空间。 [AppBar](src/WindowsTopBar/AppBar.cs)
- 顶栏高 28 DIP。快速启动贴左，其右侧的固定快捷方式每个占 33 DIP，只显示左侧空间能容纳的完整按钮。输入法、快速设置和时钟占用独立的右侧列。中央按钮按主屏幕的物理中点定位，AppBar 实际范围有偏移时换算成 DIP。动态应用图区使用中央按钮与右侧常驻区之间的空间。 [中央入口设计](docs/design/central-hub.md) · [应用区布局](src/WindowsTopBar/AppStatusPanel.cs)
- 固定按钮复用现有 `StackPanel` 与按钮模板，按中央入口左边界和系统区前的可用宽度取整；溢出的按钮折叠并显示在菜单「固定项目」，可见项目不重复。布局变化同步打开的菜单，无额外自定义布局类。
- 左键点击顶栏空白处时，AppBar 按窗口层级和工作区边界选择最大化窗口或能识别的左右贴靠窗口。右键点击空白处打开主题、亚克力、使用提醒、配置目录和退出菜单。 [窗口选择](src/WindowsTopBar/AppBar.cs) · [右键菜单](src/WindowsTopBar/MainWindow.xaml)

## 事件与刷新

| 功能 | 触发与工作 |
| --- | --- |
| 时钟 | 启动时立即更新，此后对齐到本地时间的整分钟。系统时间变化和自动唤醒时立即重新同步。 |
| 输入法 | 每 300 毫秒在后台读取前台线程的键盘布局。中文布局通过带超时的 IME 消息进一步判断中英文模式。读取失败时保留最近的显示。 |
| 系统状态与应用图标 | 每 2 秒在后台获取 Wi-Fi、音量、电池、聊天客户端运行状态及 Clash Verge 的 TUN 配置。 |
| 使用提醒 | 复用 2 秒状态刷新，并根据锁屏、熄屏和睡眠事件即时更新。手动休息且经过 5 秒缓冲后，才在这个刷新周期内检查最近的键鼠活动。 |
| 快捷方式目录 | 启动和打开菜单时后台读取根目录及 `Pinned` 第一层，错误独立处理。`FileSystemWatcher` 的名称、目录和 LastWrite 事件只标记变更，复用 2 秒状态刷新合并事件。干净目录不读盘、不解析目标；固定列表不变时保留按钮。打开菜单时重新读取配置并切换监听，失败时再开菜单重试。异步读取串行执行，退出只锁定并释放监听，不等待磁盘或 COM 工作。 |
| 快捷方式运行状态 | 在现有 2 秒后台状态任务中检查固定项目的进程，空列表直接返回；不增加计时器。按可执行文件完整路径及当前会话匹配，仅查询有限进程信息，不读进程内存。目标在目录读取时缓存，点击只重新检查进程与窗口。最近窗口记录复用现有前台事件，最多保留 128 个句柄；窗口枚举只在点击时执行。 |
| 主题与栏底染色 | 主题、系统设置或 DWM 状态变化时重新应用外观。亚克力开启且系统处于常规对比度时，窗口可见期间每秒采样。栏下约一栏高的横带缩为 48 × 6 像素取平均色，采样后释放 GDI 资源。 |
| QQ 和微信提醒 | Windows Shell 的任务栏闪动信号触发提醒，窗口前台事件或状态检查识别到对应应用后停闪。 |
| 按需操作 | 快捷菜单打开时读取目录，Clash Verge 悬停时读取托盘提示或本地配置。单击 Clash Verge 的等待取自 Windows 双击间隔。快速设置定位仅在用户点击打开后做有限次尝试。 |

输入法和状态读取通过后台任务执行，每类刷新最多保留一个正在执行的读取。WPF 控件由窗口线程更新。关闭窗口时取消等待中的操作，停止计时器，解除消息钩子和事件订阅，并释放原生资源。 [窗口代码](src/WindowsTopBar/MainWindow.xaml.cs) · [屏幕采样](src/WindowsTopBar/ScreenColorSampler.cs) · [快速设置](src/WindowsTopBar/SystemShortcuts.cs)

## 系统集成边界

- QQ 和微信从托盘唤起时使用用户配置的快捷键。闪动提醒使用 Shell 窗口事件及前台状态，应用只处理客户端和窗口状态。 [聊天窗口集成](src/WindowsTopBar/MessengerWindows.cs)
- 快捷方式仅对直接 `.exe` 目标识别应用。同一程序参数不区分，文档、URL、脚本宿主和无法解析的目标走原启动方式。有窗口时用公开 API 恢复并申请前台焦点，拒绝时提示而不重新启动；没有可用窗口时让原链接处理启动或托盘唤醒。运行阴影是模板内的淡背景，无模糊效果。窗口关闭会取消待执行的激活等待，目录和状态任务完成后不再更新 UI。
- 快速设置使用任务栏的系统按钮，调用失败时发送 `Win+A`。识别到系统面板后尝试对齐顶栏右端下方。 [快速设置集成](src/WindowsTopBar/SystemShortcuts.cs)
- Clash Verge 的 TUN 指示读取已保存的开关，悬浮信息优先取系统托盘，回退到本地配置。仪表板由运行中的应用响应再次启动，TUN 和退出使用其已配置的全局快捷键。 [托盘集成](src/WindowsTopBar/ClashVergeTray.cs) · [配置读取](src/WindowsTopBar/ClashVergeSettings.cs)

## 验证

Release 构建后运行自动检查，覆盖设置、Clash Verge 配置、QQ 和微信闪动、窗口选择、使用计时、中央布局及固定快捷方式目录变更。 [验证命令](README.md#验证)

在 Windows 11 上手动检查不同缩放下的顶栏与时钟、最大化和全屏切换、正常退出后的工作区、锁屏或睡眠恢复后的计时，以及快速设置和各客户端的实际窗体行为。
