# Windows Top Bar

面向 Windows 11 的轻量化顶部栏，计划替代部分 Magic 视界功能。项目使用 C# 和 WPF，优先考虑低资源占用、运行流畅、稳定性和按需扩展。

## 当前进度

顶栏在主屏幕顶部显示，右侧显示本地时间。Windows AppBar 注册成功时为最大化窗口预留顶部空间。

- 顶栏跟随 Windows 的系统颜色模式。浅色为暖灰底和深灰字，深色为炭灰底和浅灰字。
- Windows 支持且开启透明效果时，顶栏启用桌面亚克力，并每秒取顶栏下方窄带的平均色，轻微染在栏底。关闭透明效果或系统不支持亚克力时使用实色，高对比度模式使用系统颜色。
- 微信和 QQ 消息提醒取决于系统或应用能否提供用户已授权的公开信号。
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
