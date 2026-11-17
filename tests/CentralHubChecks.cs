#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false
#:property Nullable=enable

using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

internal static class CentralHubChecks
{
    [STAThread]
    public static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var windowType = assembly.GetType("WindowsTopBar.MainWindow", throwOnError: true)!;
        var window = (Window)Activator.CreateInstance(windowType)!;
        var layout = Find<FrameworkElement>("BarLayout");
        var hub = Find<Button>("CentralHubButton");
        var clock = Find<FrameworkElement>("ClockArea");
        var qq = Find<Button>("QQButton");
        var weChat = Find<Button>("WeChatButton");
        var clash = Find<Button>("ClashVergeButton");
        var apps = Find<FrameworkElement>("AppStatusArea");
        Find<TextBlock>("ClockText").Text = "23:59";

        qq.Visibility = weChat.Visibility = clash.Visibility = Visibility.Visible;
        Arrange(1000);
        Check(Math.Abs(LeftOf(hub) + hub.ActualWidth / 2 - 500) < 0.01, "Hub moved away from the midpoint.");
        Check(LeftOf(qq) >= LeftOf(hub) + hub.ActualWidth && LeftOf(clash) < RightOf(apps),
            "Application buttons should fit to the right of the hub.");

        clash.Visibility = Visibility.Collapsed;
        Arrange(1000);
        Check(Math.Abs(LeftOf(hub) + hub.ActualWidth / 2 - 500) < 0.01, "App visibility moved the hub.");
        clash.Visibility = Visibility.Visible;

        var systemAreaWidth = 1000 - RightOf(apps);
        var narrowWidth = 2 * (systemAreaWidth + hub.ActualWidth / 2 + 60);
        Arrange(narrowWidth);
        Check(Math.Abs(RightOf(clock) - narrowWidth) < 0.01, "The clock must stay at the right edge.");
        Check(LeftOf(qq) >= RightOf(apps), "An overflowing icon must be clipped in full.");
        Check(LeftOf(clash) >= RightOf(hub) && RightOf(clash) <= RightOf(apps),
            "An icon that fits should remain visible.");

        windowType.GetMethod("ApplyCentralHubOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [20.0]);
        Arrange(narrowWidth);
        Check(Math.Abs(LeftOf(hub) + hub.ActualWidth / 2 - (narrowWidth / 2 + 20)) < 0.01,
            "The hub must track the display offset.");
        Check(Math.Abs(RightOf(clock) - narrowWidth) < 0.01, "The display offset must not move the clock.");

        Console.WriteLine("Central hub layout checks passed.");

        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        double LeftOf(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), layout).X;
        double RightOf(FrameworkElement element) => LeftOf(element) + element.ActualWidth;

        void Arrange(double width)
        {
            layout.Measure(new Size(width, 28));
            layout.Arrange(new Rect(0, 0, width, 28));
            layout.UpdateLayout();
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
