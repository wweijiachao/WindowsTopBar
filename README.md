# Windows Top Bar

面向 Windows 11 的轻量化顶部栏，用 C#、WPF 和 .NET 10 开发，提供快捷启动、窗口切换、系统状态和 QQ、微信提醒。

## 构建与运行

在 Windows 11 上安装 .NET 10 SDK，然后在仓库根目录构建并启动 Release 版本。[下载 .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
dotnet build WindowsTopBar.slnx -c Release
.\src\WindowsTopBar\bin\Release\net10.0-windows\WindowsTopBar.exe
```

已有 Release 产物时，可以直接双击上述 EXE。运行它需要 .NET 10 Windows 桌面运行时。

## 使用

- **顶栏与窗口**。顶栏高 28 DIP，固定在主屏幕顶部。Windows AppBar 注册成功后为顶栏预留工作区空间，正常退出时释放。顶栏从任务栏和 Alt+Tab 列表中隐藏。左键点击空白处可唤回最靠前的最大化窗口，或同时唤回可识别的左右贴靠窗口，焦点落在点击位置对应的一侧。右键点击空白处可设置主题、打开配置文件目录或退出。
- **快速启动**。左键点击「快速启动 ▾」展开菜单，再点一次收起。右键可打开快捷方式目录。放入 `.lnk` 或 `.url` 文件后，菜单以文件名显示，点击即可启动。每次打开菜单时重新读取目录。
- **时间与系统状态**。时间区域悬停约 500 毫秒会显示当天日期和星期。输入法每 300 毫秒刷新「中」或「英」，读取失败时保留上次状态。Wi-Fi、音量和电池每 2 秒刷新，共用一个快速设置按钮，各自显示悬停提示。Wi-Fi 在能读取时显示已连接网络的 SSID。电池图标旁显示百分比，无电池时隐藏该项。识别到系统快速设置面板后，顶栏尝试将它移到右下方。
- **QQ 和微信**。客户端运行时显示对应图标。点击可唤回窗口，主窗口已在前台时再次点击会调用客户端自己的关闭行为。从托盘唤起使用配置中的快捷键。收到 Windows 任务栏闪动信号后，顶栏图标持续闪动，直到对应客户端的普通窗口切到前台。通过任务栏、快捷键、Alt+Tab 或顶栏唤起均可停止闪动。

## 配置

右键点击顶栏空白处，选择「打开配置文件目录」可打开 `%LOCALAPPDATA%\WindowsTopBar`。应用从其中的 `settings.json` 读取配置。文件缺失时使用默认值，首次通过菜单打开配置目录时会生成默认配置文件。

| 配置项 | 默认值 | 用途 |
| --- | --- | --- |
| `Theme` | `System` | 跟随 Windows 主题，也可设为 `Light` 或 `Dark` |
| `AcrylicEnabled` | `true` | 显示亚克力效果 |
| `ShortcutFolder` | `Shortcuts` | 快捷方式目录，支持绝对路径和相对于配置文件目录的路径 |
| `QqOpenShortcut` | `Ctrl+Alt+X` | 从托盘唤起 QQ |
| `WeChatOpenShortcut` | `Ctrl+Alt+W` | 从托盘唤起微信 |

右键菜单中的主题和亚克力改动会立即保存。浅色主题为暖灰底和深灰字，深色主题为炭灰底和浅灰字。原生亚克力不可用时会用栏下屏幕颜色轻微染色，关闭亚克力后用实色，高对比度模式使用系统颜色。

手动编辑的快捷方式目录在下次打开菜单时读取。QQ 和微信的快捷键应与各客户端的唤起设置一致，保存后下次从托盘唤起时读取。格式无效的组合会被跳过。

## 登录自启

打开 Windows「任务计划程序」，在「任务计划程序库」中创建或查看 `WindowsTopBar` 任务。

1. 在「常规」中选择「只在用户登录时运行」，使用普通权限。
2. 在「触发器」中选择当前账户「登录时」，延迟留空。
3. 在「操作」中选择「启动程序」，指向 Release 目录的 `WindowsTopBar.exe`。
4. 在「设置」中关闭运行时限，让顶栏保持运行。

可在「任务计划程序库」中禁用任务来关闭登录自启。

## 验证

Release 构建后可运行以下回归检查。

```powershell
dotnet run --file tests/SettingsChecks.cs -- src/WindowsTopBar/bin/Release/net10.0-windows/WindowsTopBar.dll
dotnet run --file tests/MessengerAttentionChecks.cs -- src/WindowsTopBar/bin/Release/net10.0-windows/WindowsTopBar.dll
dotnet run --file tests/WindowSelectionChecks.cs -- src/WindowsTopBar/bin/Release/net10.0-windows/WindowsTopBar.dll
```

## 项目结构

```text
WindowsTopBar.slnx                       解决方案
src/WindowsTopBar/
  WindowsTopBar.csproj                   WPF 应用项目
  App.xaml / App.xaml.cs                 应用入口和启动窗口配置
  MainWindow.xaml / MainWindow.xaml.cs   顶栏布局、时钟与窗口生命周期
  AppBar.cs                              Windows 停靠、工作区管理与窗口切换
  AppearanceSettings.cs                  本地配置读取与保存
  ShortcutDirectory.cs                   快捷方式目录与启动
  MessengerWindows.cs                    QQ、微信窗口操作和系统事件
  SystemShortcuts.cs                     快速设置和快捷键调用
  SystemStatus.cs / AudioStatus.cs       输入法、网络、电池和音量状态
  WindowBackdrop.cs                      Windows 原生桌面亚克力
  ScreenColorSampler.cs                  栏底染色用的屏幕窄带取色
  app.manifest                           DPI 感知与运行权限
tests/                                   配置、消息闪动和窗口选择回归检查
AGENTS.md                                开发、验证和安全约定
.gitignore                               构建产物及本机文件的忽略规则
```

## 开发说明

本项目在开发过程中使用 OpenAI Codex 辅助编写代码、排查问题和整理文档。
