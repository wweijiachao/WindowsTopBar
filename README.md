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
- **快速启动与固定按钮**。左键点击「快速启动 ▾」展开菜单，再点一次收起。根目录第一层的 `.lnk` 和 `.url` 显示在菜单中。应用自动创建英文子目录 `Pinned`，移入快捷方式即固定，移回根目录即取消固定；默认按文件名排序，固定项目可通过 `Pinned/order.json` 自定义顺序，不读取更深的子目录。固定按钮横排在快速启动右侧，每个宽 33 DIP，显示文件名（不含扩展名）的首个字符，英文小写转大写，悬停显示完整名称。右键可选择「打开快速启动目录」或「打开固定目录（Pinned）」。不提供拖动排序或应用内固定管理。
- **固定区域与刷新**。中央入口和右侧系统区位置保持固定，放不下的完整按钮进入菜单「固定项目」分组；空间恢复后自动返回顶栏，可见的固定项目不在菜单中重复。启动时后台读取，新增、移动、删除、重命名和链接修改会在现有 2 秒刷新周期内合并处理。每次打开菜单立即请求后台重读；根目录与 `Pinned` 读取失败分别显示，再次打开菜单会重试。
- **启动与切换窗口**。直接指向 `.exe` 的 `.lnk` 在点击时检查进程和窗口，不显示运行阴影，也不定时查询快捷方式对应的进程。未运行时启动；已有可用窗口但不在前台时，恢复并切到前台，效果类似 Alt+Tab；该应用已在前台时，最小化当前窗口。多窗口应用优先处理实际前台窗口，否则选择最近使用的窗口。普通菜单与「固定项目」采用相同行为，菜单以打开前的前台应用为判断依据。没有可用窗口时执行原快捷方式，由应用处理启动或托盘恢复；系统拒绝操作时提示，不因此重复启动，也不设置永久置顶。

点击导致顶栏或其菜单获得焦点时，沿用 QQ／微信的处理方式，根据此前的外部前台窗口判断是否最小化。

同一 `.exe` 的参数、文档和浏览器配置按同一个应用处理。`.url`、文档、脚本／Explorer 等已知宿主及无法解析的快捷方式保留原启动行为。间接启动器和权限不足的进程可能无法可靠识别。
- **时间与系统状态**。时间区域悬停约 500 毫秒会显示当天日期和星期。输入法每 300 毫秒刷新，中文输入法内的中英文模式分别显示「中」和「英」，读取失败时保留上次状态。Wi-Fi、音量和电池每 2 秒刷新，共用一个快速设置按钮，各自显示悬停提示。能读取时，Wi-Fi 显示已连接网络的 SSID。电池图标旁显示百分比，无电池时隐藏该项。识别到系统快速设置面板后，顶栏尝试将它移到右端下方。
- **QQ 和微信**。客户端运行时显示对应图标。点击可唤回窗口，主窗口已在前台时再次点击会调用客户端自己的关闭行为。从托盘唤起使用配置中的快捷键。收到 Windows 任务栏闪动信号后，顶栏图标持续闪动，直到对应客户端的普通窗口切到前台。通过任务栏、快捷键、Alt+Tab 或顶栏唤起均可停止闪动。
- **Clash Verge**。客户端运行时显示随顶栏主题变色的猫头图标。右下角的圆点表示已保存的 TUN 开关为开启，状态每 2 秒刷新。悬停可查看版本、系统代理、TUN 和当前订阅。单击切换 TUN，双击打开仪表板。右键可直接选择「仪表板」「TUN」或「退出」。
- **中央入口与连续使用提醒**。圆圈固定在主屏幕水平中点。开启提醒后，亮屏且已解锁的时间都计入本轮，包括阅读等无需操作键鼠的时段。默认到 60 分钟显示半实心圆，90 分钟起显示琥珀色实心圆。悬停可查看本轮用时，左键菜单可选「开始休息」。休息中显示暂停标记，5 秒防误触缓冲结束后的键鼠活动开启新一轮。锁屏、熄屏或睡眠会中断计时。关闭提醒时，中央入口显示空心圆，悬停显示「WindowsTopBar」，点击可查看关闭状态。 [中央入口设计](docs/design/central-hub.md)

屏幕较窄或缩放较高时，放不下的 QQ、微信或 Clash Verge 图标及其提醒会暂时隐藏，为中央入口和常驻系统区留出空间。

### 固定快捷方式排序

在 `Pinned` 中创建 UTF-8 编码的 `order.json`，数组项使用快捷方式的完整文件名（含 `.lnk` 或 `.url`）：

```json
[
  "ChatGPT.lnk",
  "浏览器.lnk",
  "微信.lnk"
]
```

固定按钮按列表顺序从左到右排列，靠前的项目优先留在顶栏；溢出项目按相同顺序进入「固定项目」。名称匹配不区分大小写，重复名称只取第一次，不存在的文件以及空项忽略。未列出的快捷方式追加到末尾并按文件名排序。配置缺失、无法读取或格式错误时回退文件名排序，根目录菜单不受影响。移动数组中的行即可调整顺序，无需改快捷方式名称或同名矢量图标。

修改、删除或重命名 `order.json` 会沿用现有目录监听自动刷新，也可重新打开快速启动菜单立即读取。应用不自动改写排序文件，不增加计时器或空闲读盘。

### 固定快捷方式矢量图标

在 `Pinned` 中为快捷方式放入同名的 `.icon.xaml` 文件，即可用单色矢量图标替代首字符；名称不包含快捷方式原扩展名：

```text
Pinned/
  ChatGPT.lnk
  ChatGPT.icon.xaml
  微信.lnk
```

此时 ChatGPT 显示矢量图标，微信继续显示「微」。图标同时用于固定按钮和菜单「固定项目」，统一缩放至 16 DIP，并跟随主题前景色。改名、修改或删除图标后按现有目录刷新机制更新；缺失、格式错误或不支持的图标会退回首字符，快捷方式仍可正常启动。

格式是单个填充式 WPF `Path`，只读取 `Data` 属性：

```xml
<Path xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      Data="M 2,2 L 14,2 L 14,14 L 2,14 Z" />
```

不加载完整 XAML 界面、属性元素、SVG 或多色图形；颜色和大小由顶栏模板决定，文件中的其他属性不执行。仅在目录加载／变化时后台读取和解析，不增加计时器。仓库提供 [ChatGPT.icon.xaml](assets/shortcut-icons/ChatGPT.icon.xaml) 示例，其几何路径由 [Simple Icons v15.0.0 的 OpenAI 图标](https://github.com/simple-icons/simple-icons/blob/15.0.0/icons/openai.svg) 转换，源项目采用 CC0-1.0；使用时复制到快捷方式旁，无运行时网络请求或新增依赖。

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

手动编辑 `ShortcutFolder` 后，下次打开快速启动菜单时切换目录并重新监听。QQ 和微信的快捷键应与各客户端的唤起设置一致，保存后下次从托盘唤起时读取。格式无效的组合会被跳过。

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

在仓库根目录构建 Release 后，可单独运行不操作桌面的窗口选择逻辑检查：

```powershell
$dll = (Resolve-Path "artifacts/$(git branch --show-current)/bin/WindowsTopBar/release/WindowsTopBar.dll").Path
dotnet run --artifacts-path artifacts/verification/ShortcutSelectionChecks --file tests/ShortcutSelectionChecks.cs -- $dll
dotnet run --artifacts-path artifacts/verification/ShortcutIconChecks --file tests/ShortcutIconChecks.cs -- $dll
dotnet run --artifacts-path artifacts/verification/PinnedShortcutOrderChecks --file tests/PinnedShortcutOrderChecks.cs -- $dll
```

以下完整回归包含桌面交互检查，由用户按需运行，代理不批量执行。

快捷方式检查覆盖目录分组、移动／重命名／删除、目录切换、异常恢复、缓存、首字图标、溢出与恢复、精确进程路径和窗口恢复。交互式检查会短暂显示测试窗口，需要已解锁的 Windows 桌面；Windows 拒绝焦点切换时验证正常降级。最终手动确认未启动时启动、后台／最小化时切到前台、已在前台时最小化，以及菜单和多窗口行为。

```powershell
$branch = git branch --show-current
$dll = (Resolve-Path "artifacts/$branch/bin/WindowsTopBar/release/WindowsTopBar.dll").Path
$checks = @(
    "CentralHubChecks", "ClashVergeChecks", "MessengerAttentionChecks",
    "PinnedShortcutChecks", "ShortcutSelectionChecks", "ShortcutIconChecks", "PinnedShortcutOrderChecks",
    "SettingsChecks", "UsageReminderChecks", "WindowSelectionChecks"
)
foreach ($check in $checks) {
    dotnet run --artifacts-path "artifacts/verification/$check" --file "tests/$check.cs" -- $dll
    if ($LASTEXITCODE -ne 0) { throw "$check 验证失败" }
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
  PinnedShortcuts.cs                     固定快捷方式、首字信息和目录变更监听
  PinnedShortcutOrder.cs                 读取 order.json，稳定排列固定快捷方式
  ShortcutIcon.cs                        读取同名 Path 图标、验证几何并回退首字符
  ShellShortcut.cs                       目录变化时在后台 STA 解析 .lnk 的目标
  ShortcutApplications.cs                运行状态、最近窗口和启动／激活协调
  ShortcutWindows.cs                     公开 Win32 进程查询、窗口枚举和恢复
  MessengerWindows.cs                    QQ、微信窗口操作和系统事件
  ClashVergeTray.cs / ClashVergeSettings.cs  Clash Verge 状态与菜单操作
  SystemShortcuts.cs                     快速设置和快捷键调用
  SystemStatus.cs / AudioStatus.cs       输入法、网络、电池和音量状态
  UsageReminder.cs / UsageSystemEvents.cs  连续使用计时与 Windows 会话、电源事件
  WindowBackdrop.cs                      Windows 原生桌面亚克力
  ScreenColorSampler.cs                  栏底染色用的屏幕窄带取色
  app.manifest                           DPI 感知与运行权限
tests/                                   中央布局、计时、配置、消息提醒与窗口回归检查
docs/design/central-hub.md               中央入口的设计与验证要点
ARCHITECTURE.md                          模块职责、更新频率与生命周期
AGENTS.md                                开发、验证和安全约定
.gitignore                               构建产物及本机文件的忽略规则
```

## 开发说明

本项目在开发过程中使用 OpenAI Codex 辅助编写代码、排查问题和整理文档。
