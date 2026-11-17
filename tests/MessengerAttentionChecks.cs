#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class MessengerAttentionChecks
{
    [STAThread]
    private static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("WindowsTopBar.MainWindow", throwOnError: true)!;
        var messenger = assembly.GetType("WindowsTopBar.Messenger", throwOnError: true)!;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var qq = Enum.Parse(messenger, "QQ");
        var weChat = Enum.Parse(messenger, "WeChat");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = (Window)Activator.CreateInstance(type)!;
        try
        {
            window.Show();
            ((DispatcherTimer)type.GetField("_statusTimer", members)!.GetValue(window)!).Stop();
            var statusPending = type.GetField("_statusRefreshPending", members)!;
            var waitUntil = Environment.TickCount64 + 5000;
            while ((bool)statusPending.GetValue(window)! && Environment.TickCount64 < waitUntil)
            {
                PumpFor(20);
            }
            Check(window.IsVisible && statusPending.GetValue(window) is false, "The top bar did not finish startup.");

            var reminder = type.GetField("_usageReminder", members)!.GetValue(window)!;
            var setReminder = reminder.GetType().GetMethod("SetEnabled")!;
            var renderReminder = type.GetMethod("UpdateUsageReminder", members)!;
            setReminder.Invoke(reminder, [false, 0L]);
            setReminder.Invoke(reminder, [true, 0L]);
            renderReminder.Invoke(window, [0L]);
            var usageButton = (Button)window.FindName("CentralHubButton");
            var usageRing = (UIElement)window.FindName("UsageReminderRing");
            var usageHalf = (UIElement)window.FindName("UsageReminderHalf");
            var usageSolid = (UIElement)window.FindName("UsageReminderSolid");
            Check(usageButton.Visibility == Visibility.Visible && usageRing.Visibility == Visibility.Visible &&
                usageHalf.Visibility == Visibility.Collapsed && usageSolid.Visibility == Visibility.Collapsed,
                "Usage reminder did not start with an empty ring.");
            renderReminder.Invoke(window, [int.MaxValue * 60_000L]);
            Check(usageSolid.Visibility == Visibility.Visible, "Usage reminder did not display the final icon.");
            reminder.GetType().GetMethod("StartRest")!.Invoke(reminder, [1000L]);
            renderReminder.Invoke(window, [int.MaxValue * 60_000L]);
            Check(((UIElement)window.FindName("UsageReminderPause")).Visibility == Visibility.Visible &&
                usageSolid.Visibility == Visibility.Collapsed, "Starting a rest did not replace the reminder icon.");
            setReminder.Invoke(reminder, [false, 0L]);
            renderReminder.Invoke(window, [0L]);
            Check(usageButton.Visibility == Visibility.Visible && usageRing.Visibility == Visibility.Visible &&
                ((UIElement)window.FindName("UsageReminderPause")).Visibility == Visibility.Collapsed,
                "The hub should show a neutral ring when reminders are disabled.");
            usageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpFor(50);
            Check(usageButton.ContextMenu.IsOpen && usageButton.ContextMenu.IsVisible &&
                usageButton.ContextMenu.PlacementTarget == usageButton,
                "The hub menu must open below the button while reminders are disabled.");
            Check(usageButton.ContextMenu.Items.Count == 2 &&
                usageButton.ContextMenu.Items[0] == window.FindName("UsageStatusItem") &&
                usageButton.ContextMenu.Items[1] == window.FindName("UsageStartRestItem"),
                "The hub menu should contain only the usage status and rest action.");
            Check(((UIElement)window.FindName("UsageStartRestItem")).Visibility == Visibility.Collapsed,
                "The hub offered to start a rest while reminders were disabled.");
            usageButton.ContextMenu.IsOpen = false;

            var input = (FrameworkElement)window.FindName("InputIndicator");
            var quickSettings = (FrameworkElement)window.FindName("QuickSettingsButton");
            var clockArea = (FrameworkElement)window.FindName("ClockArea");
            Point Position(FrameworkElement element) => element.TranslatePoint(new Point(), window);
            window.UpdateLayout();
            var systemPositions = (Position(input), Position(quickSettings), Position(clockArea));
            Check(Math.Abs(systemPositions.Item3.X + clockArea.ActualWidth - window.ActualWidth) < 1,
                "The clock area should reach the right edge of the bar.");

            var launcher = (FrameworkElement)window.FindName("ShortcutsButton");
            Check(window.InputHitTest(new Point(0.5, 0.5)) is DependencyObject corner && launcher.IsAncestorOf(corner),
                "Quick Launch should receive mouse input at the upper-left corner.");

            var tunEnabled = type.GetField("_clashVergeTunEnabled", members)!;
            var updateTunDot = type.GetMethod("UpdateClashVergeTunDot", members)!;
            var tunDot = (UIElement)window.FindName("ClashVergeTunDot");
            tunEnabled.SetValue(window, true);
            updateTunDot.Invoke(window, null);
            Check(tunDot.Visibility == Visibility.Visible, "The Clash Verge dot should appear when TUN is enabled.");
            tunEnabled.SetValue(window, false);
            updateTunDot.Invoke(window, null);
            Check(tunDot.Visibility == Visibility.Collapsed, "The Clash Verge dot should disappear when TUN is disabled.");

            var qqGlyph = (UIElement)window.FindName("QQGlyph");
            var weChatGlyph = (UIElement)window.FindName("WeChatGlyph");
            var flash = type.GetMethod("FlashMessenger", members)!;
            var stop = type.GetMethod("StopMessengerFlash", members)!;
            flash.Invoke(window, [qq]);
            flash.Invoke(window, [weChat]);
            setReminder.Invoke(reminder, [true, 0L]);
            renderReminder.Invoke(window, [0L]);
            window.UpdateLayout();
            Check(systemPositions == (Position(input), Position(quickSettings), Position(clockArea)),
                "Showing app icons and the reminder moved the fixed system controls.");
            var qqButton = (FrameworkElement)window.FindName("QQButton");
            var qqTop = qqButton.TranslatePoint(new Point(qqButton.ActualWidth / 2, 0.5), window);
            Check(window.InputHitTest(qqTop) is DependencyObject edge && qqButton.IsAncestorOf(edge),
                "The QQ button should receive mouse input at the top edge.");

            // The old animation finished after four 600 ms cycles.
            PumpFor(2650);
            var stillFlashing = false;
            for (int sample = 0; sample < 10 && !stillFlashing; sample++)
            {
                stillFlashing = qqGlyph.Opacity < 0.9 && weChatGlyph.Opacity < 0.9;
                PumpFor(75);
            }
            Check(stillFlashing, "The icons stopped flashing before acknowledgment.");

            stop.Invoke(window, [qq]);
            Check(!qqGlyph.HasAnimatedProperties && qqGlyph.Opacity == 1, "QQ kept flashing after it reached the foreground.");
            Check(weChatGlyph.HasAnimatedProperties, "Stopping QQ also cleared WeChat.");
            flash.Invoke(window, [qq]);
            Check(qqGlyph.HasAnimatedProperties, "A later flash did not restart QQ.");
            type.GetMethod("UpdateMessengerVisibility", members)!.Invoke(window, [weChat, false]);
            Check(!weChatGlyph.HasAnimatedProperties, "WeChat kept animating after exit.");
            window.Close();
            Check(!window.IsVisible && !qqGlyph.HasAnimatedProperties, "Closing left the bar or animation active.");
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
            app.Shutdown();
        }
        Console.WriteLine("Messenger attention checks passed.");
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
