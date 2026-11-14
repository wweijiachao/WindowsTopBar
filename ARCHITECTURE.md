# Windows Top Bar 架构审查

## 现状

解决方案只有一个 WPF 项目。[App.xaml](src/WindowsTopBar/App.xaml) 用 `StartupUri` 打开主窗口，并在主窗口关闭时退出应用。[MainWindow.xaml](src/WindowsTopBar/MainWindow.xaml) 定义了空白窗口，[窗口代码](src/WindowsTopBar/MainWindow.xaml.cs) 只有初始化。当前骨架适合直接开始顶栏开发，按功能出现的顺序增加少量类即可。此次审查基于源码，构建与窗口启动仍待验证。

## 职责划分

| 组件 | 职责 | 引入时机 |
| --- | --- | --- |
| `App.xaml` / `App.xaml.cs` | 管理应用级启动和退出。沿用现有 `StartupUri` 和 `OnMainWindowClose`。 | 已有 |
| `MainWindow.xaml` | 定义顶栏布局、时钟文字、背景与文字颜色，以及消息提示点的显示样式。 | 随界面功能增加 |
| `MainWindow.xaml.cs` | 管理窗口生命周期，持有顶栏所需的模块，订阅事件并更新控件。时钟可直接用一个 `DispatcherTimer`。 | 已有 |
| `AppBar` | 管理本窗口的 Windows AppBar 注册、定位、全屏层级和注销。 | 确认需要预留工作区后 |
| `WindowBackdrop` / `ScreenColorSampler` | 启用原生桌面亚克力，并为栏底轻微染色提供屏幕窄带平均色。 | 已有 |
| `NotificationWatcher` | 管理通知授权、订阅及应用筛选，向窗口提供微信、QQ 各自的提示状态。 | 实机验证通知来源后 |

窗口直接创建所需模块，通过普通方法和 .NET 事件连接。少量 P/Invoke 声明和原生结构可以放在对应模块内部，出现共享调用时再提取。控件状态少时，直接由窗口代码更新。出现多处共享状态或复杂绑定后，再考虑轻量 ViewModel。

## 窗口管理

- **先确认是否预留工作区。** 如果最大化窗口应给顶栏让位，采用 Windows AppBar。此前的首版建议采用这个行为。如果选择悬浮，贴顶定位和窗口层级仍有单一归属，AppBar 模块随工作区预留需求引入。[Windows AppBar 文档](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)
- **窗口接入，AppBar 定位。** WPF 窗口在 `SourceInitialized` 后接入 `HwndSource` 消息钩子，将停靠相关消息交给 AppBar。AppBar 使用 `ABM_NEW` 注册，通过 `ABM_QUERYPOS` 和 `ABM_SETPOS` 协商位置，再应用 Shell 返回的矩形。它也负责在位置或 DPI 变化时重新定位，并在全屏期间调整窗口层级。XAML 的尺寸使用 DIP，由停靠模块按当前 DPI 转换所需的尺寸。[WPF 窗口初始化](https://learn.microsoft.com/dotnet/api/system.windows.window.sourceinitialized) · [AppBar 位置与通知](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars) · [Windows DPI 文档](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- **退出时恢复工作区。** 确定关闭后，在窗口句柄销毁前用 `ABM_REMOVE` 注销 AppBar，并移除窗口消息钩子。注册成功但定位失败时也要注销，避免给桌面留下已预留的空间。[AppBar 注册与注销](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars#registration)

## 事件监听与 UI 更新

- **时钟**由窗口持有一个 `DispatcherTimer`，启动时立即读取本地时间，每次刷新后按下一个分钟边界安排更新，关闭时停止并解除订阅。每次刷新读取实际时间，以便休眠恢复后重新同步。[WPF DispatcherTimer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchertimer?view=windowsdesktop-10.0)
- **颜色适配**由系统颜色模式决定底色与文字配色。窗口收到系统设置变化消息时重新应用配色。启用 DWM 亚克力时，每秒读取栏下窄带的平均色，仅轻微调整栏底渐变。关闭透明效果、使用高对比度或无法启用亚克力时使用实色。[Windows 颜色设置](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common#personalization---colors) · [DWM 桌面亚克力](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- **消息提醒**先在 Windows 11 上分别验证目标微信和 QQ 版本能否产生可获取的系统通知。若采用 `UserNotificationListener`，需在实际 WPF 发布方式下验证包清单能力、用户授权，以及其他应用有焦点时能否收到 `NotificationChanged`。验证通过后，由监听器在 UI 线程请求授权，在常驻进程内处理通知增减，并仅向 UI 提供每个客户端是否存在可观察通知的状态。授权被撤销或来源不可用时清除对应圆点，其他顶栏功能继续运行。[Windows 通知监听与授权](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)

系统模块负责检查 API 的返回值、释放各自的订阅和句柄。窗口关闭时先停止接收更新，再释放这些模块。需要修改 WPF 控件的更新统一回到窗口的 `Dispatcher`，已经排队的更新在关闭开始后直接丢弃。

## 按阶段验证

| 阶段 | 重点检查 |
| --- | --- |
| 构建基线 | 在 Windows 11 上完成 Release 构建，验证骨架窗口可以打开和关闭。 |
| 顶栏与时钟 | 验证主屏幕在 100% 和 150% 缩放下贴顶准确、启动保留当前应用焦点、跨分钟和休眠恢复后时间正确。若采用 AppBar，额外验证最大化让位、全屏结束后恢复、正常退出后工作区复原。 |
| 系统配色与亚克力 | 验证浅色和深色下的文字对比度、栏底的轻微透色，以及关闭透明效果后的实色回退。 |
| 微信与 QQ 提醒 | 分别验证实际来信、系统通知移除、权限拒绝和撤销。提示点随可观察的系统通知状态更新。 |
