# Windows Top Bar

面向 Windows 11 的轻量化顶部栏，计划替代部分 Magic 视界功能。项目使用 C# 和 WPF，优先考虑低资源占用、运行流畅、稳定性和按需扩展。

## 当前进度

左键点击「快速启动 ▾」可打开 Windows 快捷方式（`.lnk` 或 `.url`），右键可打开快捷方式目录。默认目录是 `%LOCALAPPDATA%\WindowsTopBar\Shortcuts`。如需更改，在 `settings.json` 中编辑 `ShortcutFolder`。支持绝对路径和相对于配置文件目录的路径。菜单每次打开时重新读取设置和目录。

顶栏在主屏幕顶部显示，右侧显示本地时间。Windows AppBar 注册成功时为最大化窗口预留顶部空间。

- 右键点击顶栏空白处，可打开配置文件目录或通过「主题」选择深色、浅色或跟随主题。默认跟随 Windows 的系统颜色模式。浅色为暖灰底和深灰字，深色为炭灰底和浅灰字。
- 「亚克力效果」默认勾选，可单独关闭。原生亚克力可用时使用 Windows 毛玻璃。关闭 Windows 透明效果或原生材质不可用时，应用仍会用栏下窄带的平均色轻微染色。取消勾选后使用实色，高对比度模式使用系统颜色。
- 每次修改主题或亚克力开关都会立即保存到 `%LOCALAPPDATA%\WindowsTopBar\settings.json`，下次启动自动读取。配置文件缺失或无法读取时使用默认设置。
- 输入法状态每 300 毫秒刷新，以顶栏字体显示「中」或「英」。读取失败时保留上次成功识别的状态，首次读到状态前留空。
- Wi-Fi、音量和电池每 2 秒刷新，共用一个按钮，悬停时分别显示状态。点击打开 Windows 快速设置，并在可识别系统面板时将它对齐到顶栏右下方。电池旁显示百分比，无电池的设备会隐藏该项。
- QQ 和微信运行时显示在状态区左侧。点击图标可唤出客户端，主窗口已在前台时再点会按客户端的关闭行为退回托盘。收到 Windows 任务栏闪动事件时，对应图标会闪动。
- 后续考虑加入系统状态显示和快捷控制。

## 开发环境

在 Windows 11 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。在仓库根目录执行以下命令。

```powershell
dotnet build WindowsTopBar.slnx -c Release
dotnet run --project src/WindowsTopBar/WindowsTopBar.csproj
```

运行后可在主屏幕顶部看到顶栏。右键单击顶栏并选择「退出」即可关闭应用和释放工作区。

## 项目结构

```text
WindowsTopBar.slnx                       解决方案
src/WindowsTopBar/
  WindowsTopBar.csproj                   WPF 应用项目
  App.xaml / App.xaml.cs                 应用入口和启动窗口配置
  MainWindow.xaml / MainWindow.xaml.cs   顶栏布局、时钟与窗口生命周期
  AppBar.cs                              Windows 停靠与工作区管理
  WindowBackdrop.cs                      Windows 原生桌面亚克力
  ScreenColorSampler.cs                  栏底染色用的屏幕窄带取色
  app.manifest                           DPI 感知与运行权限
AGENTS.md                                开发、验证和安全约定
.gitignore                               构建产物及本机文件的忽略规则
```
