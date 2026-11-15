#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property UseAppHost=false

using System;
using System.IO;
using System.Reflection;
using System.Windows;
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

            var qqGlyph = (UIElement)window.FindName("QQGlyph");
            var weChatGlyph = (UIElement)window.FindName("WeChatGlyph");
            var flash = type.GetMethod("FlashMessenger", members)!;
            var stop = type.GetMethod("StopMessengerFlash", members)!;
            flash.Invoke(window, [qq]);
            flash.Invoke(window, [weChat]);

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
