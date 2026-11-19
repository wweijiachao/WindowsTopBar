# Windows Top Bar

面向 Windows 11 的轻量化顶部栏，用 C#、WPF 和 .NET 10 开发，提供快捷启动、窗口切换、连续使用提醒、系统状态、QQ 和微信提醒，以及 Clash Verge 控制。

## 构建与运行

在 Windows 11 上安装 .NET 10 SDK，然后在仓库根目录构建并启动 Release 版本。重新构建同一分支前，先从顶栏空白处右键退出正在运行的版本。 [下载 .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
$branch = git branch --show-current
dotnet build WindowsTopBar.slnx -c Release --artifacts-path "artifacts/$branch"
if ($LASTEXITCODE -ne 0) { throw "Release 构建失败" }
& "artifacts/$branch/bin/WindowsTopBar/release/WindowsTopBar.exe"
```

各分支的 Release 分别存放在 `artifacts/<分支名>`。有现成的 Release 目录时，可用 .NET 10 Windows 桌面运行时直接双击其中的 `WindowsTopBar.exe`。复制到其他目录时请随 EXE 保留配套文件。

## 使用

- **顶栏与窗口**。顶栏高 28 DIP，固定在主屏幕顶部。Windows AppBar 注册成功后为顶栏预留工作区空间，正常退出时释放。顶栏从任务栏和 Alt+Tab 列表中隐藏。左键点击空白处可唤回最靠前的最大化窗口，或同时唤回可识别的左右贴靠窗口，焦点落在点击位置对应的一侧。右键点击空白处可调整主题、亚克力和使用提醒，也可打开配置文件目录或退出。
- **快速启动与固定按钮**。左键点击「快速启动 ▾」展开菜单，再点一次收起。快速启动目录第一层的 `.lnk` 和 `.url` 文件按文件名排序，显示在菜单中。应用会自动创建英文子目录 `Pinned`，将快捷方式移入其中即可固定，移回根目录即可取消固定；两处均不递归读取更深的目录。固定按钮横向排列在「快速启动 ▾」右侧，每个宽 33 DIP，显示文件名（不含扩展名）的首个字母或汉字，英文小写转大写。单击启动，悬停显示完整名称。右键「快速启动」可选择「打开快速启动目录」或「打开固定目录（Pinned）」。第一版通过文件管理器管理，不提供拖动排序或应用内固定管理。
- **固定按钮空间与刷新**。固定区域为中央入口和右侧系统状态留出空间，放不下的完整按钮进入快速启动菜单的「固定项目」分组；空间恢复时自动返回顶栏，已显示在顶栏的项目不在菜单中重复。启动时加载，目录中的新增、移动、删除和重命名会自动刷新；每次打开菜单也会重新读取。目录事件合并后在后台读取，读取期间菜单显示加载提示。根目录和 `Pinned` 的读取错误分别显示，其他顶栏功能继续可用，再次打开菜单会重试。
- **时间与系统状态**。时间区域悬停约 500 毫秒会显示当天日期和星期。输入法每 300 毫秒刷新，中文输入法内的中英文模式分别显示「中」和「英」，读取失败时保留上次状态。Wi-Fi、音量和电池每 2 秒刷新，共用一个快速设置按钮，各自显示悬停提示。能读取时，Wi-Fi 显示已连接网络的 SSID。电池图标旁显示百分比，无电池时隐藏该项。识别到系统快速设置面板后，顶栏尝试将它移到右端下方。
- **QQ 和微信**。客户端运行时显示对应图标。点击可唤回窗口，主窗口已在前台时再次点击会调用客户端自己的关闭行为。从托盘唤起使用配置中的快捷键。收到 Windows 任务栏闪动信号后，顶栏图标持续闪动，直到对应客户端的普通窗口切到前台。通过任务栏、快捷键、Alt+Tab 或顶栏唤起均可停止闪动。
- **Clash Verge**。客户端运行时显示随顶栏主题变色的猫头图标。右下角的圆点表示已保存的 TUN 开关为开启，状态每 2 秒刷新。悬停可查看版本、系统代理、TUN 和当前订阅。单击切换 TUN，双击打开仪表板。右键可直接选择「仪表板」「TUN」或「退出」。
- **中央入口与连续使用提醒**。圆圈固定在主屏幕水平中点。开启提醒后，亮屏且已解锁的时间都计入本轮，包括阅读等无需操作键鼠的时段。默认到 60 分钟显示半实心圆，90 分钟起显示琥珀色实心圆。悬停可查看本轮用时，左键菜单可选「开始休息」。休息中显示暂停标记，5 秒防误触缓冲结束后的键鼠活动开启新一轮。锁屏、熄屏或睡眠会中断计时。关闭提醒时，中央入口显示空心圆，悬停显示「WindowsTopBar」，点击可查看关闭状态。 [中央入口设计](docs/design/central-hub.md)

屏幕较窄或缩放较高时，放不下的 QQ、微信或 Clash Verge 图标及其提醒会暂时隐藏，为中央入口和常驻系统区留出空间。

## 配置

右键点击顶栏空白处，选择「打开配置文件目录」可打开 `%LOCALAPPDATA%\WindowsTopBar`。应用从其中的 `settings.json` 读取配置。文件缺失时使用默认值，首次通过菜单打开配置目录时会生成默认配置文件。

| 配置项 | 默认值 | 用途 |
| --- | --- | --- |
| `Theme` | `System` | 跟随 Windows 主题，也可设为 `Light` 或 `Dark` |
| `AcrylicEnabled` | `true` | 显示亚克力效果 |
| `ShortcutFolder` | `Shortcuts` | 快捷方式目录，支持绝对路径和相对于配置文件目录的路径 |
| `QqOpenShortcut` | `Ctrl+Alt+X` | 从托盘唤起 QQ |
| `WeChatOpenShortcut` | `Ctrl+Alt+W` | 从托盘唤起微信 |
| `UsageReminderEnabled` | `true` | 启用连续计时和图标提醒，可从顶栏空白处右键切换 |
| `UsageReminderFirstMinutes` | `60` | 首次提醒的累计分钟数 |
| `UsageReminderSecondMinutes` | `90` | 再次提醒的累计分钟数，需大于首次 |

右键菜单中的主题、亚克力和使用提醒改动会立即保存。浅色主题为暖灰底和深灰字，深色主题为炭灰底和浅灰字。开启亚克力时会轻微采样并混合栏下颜色，原生亚克力可用时叠加系统材质。关闭亚克力后使用实色，高对比度模式使用系统颜色。

手动编辑 `ShortcutFolder` 后，在下次打开快速启动菜单时切换目录、创建 `Pinned` 并重新监听。QQ 和微信的快捷键应与各客户端的唤起设置一致，保存后下次从托盘唤起时读取。格式无效的组合会被跳过。

关闭使用提醒会结束本轮计时，重新开启后从零开始。手动修改提醒时点后，重启顶栏即可使用新值。无效时点回退至默认的 60 和 90 分钟。

### Clash Verge 快捷键

在 Clash Verge 中开启全局快捷键，为「开启／关闭 TUN 模式」绑定一个组合键，例如 `Ctrl+Alt+Shift+F9`。顶栏会直接读取已保存的绑定。单击图标时会等待 Windows 的双击判定结束，再发送快捷键。

如需从顶栏退出 Clash Verge，在 `%APPDATA%\io.github.clash-verge-rev.clash-verge-rev\verge.yaml` 中将 `- quit,CTRL+ALT+SHIFT+F11` 加入 `hotkeys` 列表，然后重启 Clash Verge。

## 登录自启

打开 Windows「任务计划程序」，在「任务计划程序库」中创建或查看 `WindowsTopBar` 任务。

1. 在「常规」中选择「只在用户登录时运行」，使用普通权限。
2. 在「触发器」中选择当前账户「登录时」，延迟留空。
3. 在「操作」中选择「启动程序」，指向 Release 目录的 `WindowsTopBar.exe`。
4. 在「设置」中关闭运行时限，让顶栏保持运行。

可在「任务计划程序库」中禁用任务来关闭登录自启。

## 验证

在仓库根目录构建 Release 后，运行 `tests` 中的全部 7 项回归检查。快捷方式检查覆盖目录分组、移动和重命名、首字图标、完整按钮溢出与恢复、目录异常和监听释放；在桌面上另行验证实际启动、菜单交互和正常退出。

```powershell
$branch = git branch --show-current
$dll = (Resolve-Path "artifacts/$branch/bin/WindowsTopBar/release/WindowsTopBar.dll").Path
foreach ($check in Get-ChildItem tests -Filter '*Checks.cs') {
    dotnet run --artifacts-path "artifacts/verification/$($check.BaseName)" --file $check.FullName -- $dll
    if ($LASTEXITCODE -ne 0) { throw "$($check.Name) 验证失败" }
}
```

## 项目结构

```text
WindowsTopBar.slnx                       解决方案
src/WindowsTopBar/
  WindowsTopBar.csproj                   WPF 应用项目
  App.xaml / App.xaml.cs                 应用入口和启动窗口配置
  MainWindow.xaml / MainWindow.xaml.cs   顶栏布局、时钟与窗口生命周期
  AppBar.cs                              Windows 停靠、工作区管理与窗口切换
  AppStatusPanel.cs                      右侧动态应用图标的排列与溢出处理
  AppearanceSettings.cs                  本地配置读取与保存
  ShortcutDirectory.cs                   快捷方式目录与启动
  ShortcutCatalog.cs                     根目录及 Pinned 的独立读取、目录监听与释放
  PinnedShortcutPanel.cs                 固定按钮布局与完整按钮溢出
  MessengerWindows.cs                    QQ、微信窗口操作和系统事件
  ClashVergeTray.cs / ClashVergeSettings.cs  Clash Verge 状态与菜单操作
  SystemShortcuts.cs                     快速设置和快捷键调用
  SystemStatus.cs / AudioStatus.cs       输入法、网络、电池和音量状态
  UsageReminder.cs / UsageSystemEvents.cs  连续使用计时与 Windows 会话、电源事件
  WindowBackdrop.cs                      Windows 原生桌面亚克力
  ScreenColorSampler.cs                  栏底染色用的屏幕窄带取色
  app.manifest                           DPI 感知与运行权限
tests/                                   快捷方式、中央布局、计时、配置、消息提醒与窗口回归检查
docs/design/central-hub.md               中央入口的设计与验证要点
ARCHITECTURE.md                          模块职责、更新频率与生命周期
AGENTS.md                                开发、验证和安全约定
.gitignore                               构建产物及本机文件的忽略规则
```

## 开发说明

本项目在开发过程中使用 OpenAI Codex 辅助编写代码、排查问题和整理文档。
