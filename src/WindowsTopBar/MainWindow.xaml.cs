using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsTopBar;

public partial class MainWindow : Window
{
    private const int WmTimeChange = 0x001E;
    private const int WmSettingChange = 0x001A;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmCompositionChanged = 0x031E;
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmResumeAutomatic = 0x0012;
    private const byte AcrylicTintOpacity = 180;
    private const double EdgeTintStrength = 0.12;
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Color LightBackground = Color.FromRgb(241, 239, 235);
    private static readonly Color DarkBackground = Color.FromRgb(38, 39, 41);
    private static readonly Color LightText = Color.FromRgb(229, 230, 232);
    private static readonly Color DarkText = Color.FromRgb(51, 53, 58);

    private readonly DispatcherTimer _clock = new();
    private readonly MessengerWindows _messengers = new();
    private long _lastQqFlash;
    private long _lastWeChatFlash;
    private readonly DispatcherTimer _inputTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly DispatcherTimer _statusTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private readonly DispatcherTimer _tintTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private HwndSource? _source;
    private AppBar? _appBar;
    private DispatcherOperation? _pendingAppearance;
    private GradientStop? _acrylicEdge;
    private Color _acrylicTint;
    private AppearanceSettings _settings;
    private bool _statusRefreshPending;
    private bool _inputRefreshPending;
    private bool _closing;
    private readonly CancellationTokenSource _windowLifetime = new();
    private bool _openingQuickSettings;
    private bool _openingMessenger;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppearanceSettings.Load(AppearanceSettings.FilePath);
        UpdateThemeMenu();
        _clock.Tick += RefreshClock;
        _tintTimer.Tick += RefreshEdgeTint;
        _statusTimer.Tick += RefreshStatus;
        _inputTimer.Tick += RefreshInputIndicator;
        Loaded += (_, _) =>
        {
            _inputTimer.Start();
            RefreshInputIndicator(this, EventArgs.Empty);
            _statusTimer.Start();
            RefreshStatus(this, EventArgs.Empty);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        RefreshClock(this, EventArgs.Empty);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (_source is null)
        {
            return;
        }

        _source.AddHook(OnWindowMessage);
        _messengers.Register(_source.Handle);
        _appBar = new AppBar(_source, Height);
        _appBar.Start();
        RefreshAppearance();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        _closing = true;
        _windowLifetime.Cancel();
        _windowLifetime.Dispose();
        _statusTimer.Stop();
        _statusTimer.Tick -= RefreshStatus;
        _inputTimer.Stop();
        _inputTimer.Tick -= RefreshInputIndicator;
        _clock.Stop();
        _clock.Tick -= RefreshClock;
        _tintTimer.Stop();
        _tintTimer.Tick -= RefreshEdgeTint;
        _pendingAppearance?.Abort();
        _messengers.Dispose();
        _appBar?.Dispose();
        _source?.RemoveHook(OnWindowMessage);
    }

    private void RefreshAppearance()
    {
        var dark = _settings.Theme == TopBarTheme.Dark ||
            (_settings.Theme == TopBarTheme.System && Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", 1) is 0);
        ApplyAppearance(dark, _settings.AcrylicEnabled, SystemParameters.HighContrast);
    }

    private void ApplyAppearance(bool dark, bool acrylicEnabled, bool highContrast)
    {
        if (_source is null)
        {
            return;
        }

        var useTint = acrylicEnabled && !highContrast;
        var nativeAcrylic = WindowBackdrop.Apply(_source, useTint, dark);
        var background = dark ? DarkBackground : LightBackground;
        Foreground = highContrast ? SystemColors.WindowTextBrush : FrozenBrush(dark ? LightText : DarkText);

        if (useTint)
        {
            // Keep the local tint active when Windows uses a solid backdrop.
            background.A = nativeAcrylic ? AcrylicTintOpacity : (byte)255;
            _acrylicTint = background;
            _acrylicEdge = new GradientStop(background, 1);
            var tint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            tint.GradientStops.Add(new GradientStop(background, 0.35));
            tint.GradientStops.Add(_acrylicEdge);
            Background = tint;
            RefreshEdgeTint(this, EventArgs.Empty);
            _tintTimer.Start();
            return;
        }

        _tintTimer.Stop();
        _acrylicEdge = null;
        Background = highContrast ? SystemColors.WindowBrush : FrozenBrush(background);
    }

    private void RefreshEdgeTint(object? sender, EventArgs e)
    {
        if (_source is null || _acrylicEdge is null || !IsVisible)
        {
            return;
        }

        // AppBar reserves its own area, so maximized windows start below the native backdrop.
        var sample = ScreenColorSampler.GetAverageColorBelow(_source.Handle);
        var tint = _acrylicTint;
        if (sample is Color color)
        {
            tint.R = BlendChannel(tint.R, color.R);
            tint.G = BlendChannel(tint.G, color.G);
            tint.B = BlendChannel(tint.B, color.B);
        }

        if (tint != _acrylicEdge.Color)
        {
            _acrylicEdge.Color = tint;
        }
    }

    private static byte BlendChannel(byte baseValue, byte sampledValue) =>
        (byte)Math.Round(baseValue + (sampledValue - baseValue) * EdgeTintStrength);

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_closing && _messengers.ReadFlash(message, wParam, lParam) is { } messenger)
        {
            FlashMessenger(messenger);
        }

        if (message == WmMouseActivate)
        {
            // System shortcuts should act while the previous application still has focus.
            handled = true;
            return MaNoActivate;
        }

        if (message == WmTimeChange || (message == WmPowerBroadcast && wParam == PbtApmResumeAutomatic))
        {
            RefreshClock(this, EventArgs.Empty);
        }

        if (message is WmSettingChange or WmThemeChanged or WmDwmCompositionChanged)
        {
            _pendingAppearance ??= Dispatcher.InvokeAsync(() =>
            {
                _pendingAppearance = null;
                RefreshAppearance();
            }, DispatcherPriority.Background);
        }

        return 0;
    }

    private async void RefreshStatus(object? sender, EventArgs e)
    {
        if (_statusRefreshPending)
        {
            return;
        }

        _statusRefreshPending = true;
        try
        {
            var (battery, wifi, audio, messengers) = await Task.Run(() =>
                (SystemStatus.ReadBattery(), SystemStatus.ReadWifi(), AudioStatus.Read(), MessengerWindows.ReadRunning()));
            if (_closing)
            {
                return;
            }

            QQButton.Visibility = messengers.QQ ? Visibility.Visible : Visibility.Collapsed;
            WeChatButton.Visibility = messengers.WeChat ? Visibility.Visible : Visibility.Collapsed;
            UpdateMessengerSeparator();

            BatteryIndicator.Visibility = battery.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            BatteryText.Text = battery.Percentage is int percentage ? $"{percentage}%" : "—";
            BatteryFill.Width = battery.Charging ? 0 : 14.5 * battery.Percentage.GetValueOrDefault() / 100;
            BatteryCharging.Visibility = battery.Charging ? Visibility.Visible : Visibility.Collapsed;
            BatteryIndicator.ToolTip = $"电池 {BatteryText.Text}{(battery.Charging ? " · 充电中" : "")}";

            WifiDisconnected.Visibility = wifi.Connected == false ? Visibility.Visible : Visibility.Collapsed;
            WifiIcon.ToolTip = wifi.Ssid ?? (wifi.Connected switch
            {
                true => "Wi-Fi 已连接，SSID 不可用",
                false => "Wi-Fi 未连接",
                null => "Wi-Fi 状态不可用"
            });

            var silent = audio is { Muted: true } or { Percentage: 0 };
            VolumeWaves.Visibility = !silent && audio.HasValue ? Visibility.Visible : Visibility.Collapsed;
            VolumeMute.Visibility = silent ? Visibility.Visible : Visibility.Collapsed;
            VolumeIcon.ToolTip = audio is { } output
                ? (output.Muted ? "已静音" : $"音量 {output.Percentage}%")
                : "音量状态不可用";
        }
        finally
        {
            _statusRefreshPending = false;
        }
    }

    private void UpdateMessengerSeparator() =>
        MessengerSeparator.Visibility = QQButton.Visibility == Visibility.Visible || WeChatButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

    private void FlashMessenger(Messenger messenger)
    {
        var isQq = messenger == Messenger.QQ;
        var button = isQq ? QQButton : WeChatButton;
        var glyph = isQq ? QQGlyph : WeChatGlyph;
        button.Visibility = Visibility.Visible;
        UpdateMessengerSeparator();

        ref var lastFlash = ref (isQq ? ref _lastQqFlash : ref _lastWeChatFlash);
        var now = Environment.TickCount64;
        var repeated = lastFlash != 0 && now - lastFlash < 1200;
        lastFlash = now;
        if (!repeated)
        {
            glyph.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.18, TimeSpan.FromMilliseconds(300))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(4),
                    FillBehavior = FillBehavior.Stop
                });
        }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ShortcutsButton, Placement = PlacementMode.Bottom };
        try
        {
            foreach (var path in ShortcutDirectory.List(ShortcutDirectory.Folder))
            {
                // TextBlock preserves underscores in the filename as literal text.
                var name = Path.GetFileNameWithoutExtension(path);
                var item = new MenuItem { Header = new TextBlock { Text = name } };
                AutomationProperties.SetName(item, name);
                item.Click += (_, _) => LaunchShortcut(path);
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "快捷方式目录为空", IsEnabled = false });
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            menu.Items.Add(new MenuItem { Header = "无法读取快捷方式目录", IsEnabled = false });
        }

        menu.IsOpen = true;
    }

    private void LaunchShortcut(string path)
    {
        try
        {
            ShortcutDirectory.Launch(path);
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"无法打开快捷方式「{Path.GetFileNameWithoutExtension(path)}」。",
                "Windows Top Bar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void QQ_Click(object sender, RoutedEventArgs e) => await ToggleMessenger(Messenger.QQ);

    private async void WeChat_Click(object sender, RoutedEventArgs e) => await ToggleMessenger(Messenger.WeChat);

    private async Task ToggleMessenger(Messenger messenger)
    {
        if (_openingMessenger || _closing)
        {
            return;
        }

        if (_messengers.TryMinimizeToTray(messenger))
        {
            return;
        }

        _openingMessenger = true;
        try
        {
            var token = _windowLifetime.Token;
            await Task.Run(() => MessengerWindows.Activate(messenger, token));
        }
        finally
        {
            _openingMessenger = false;
        }
    }

    private async void QuickSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_openingQuickSettings || _closing)
        {
            return;
        }

        _openingQuickSettings = true;
        try
        {
            var corner = PointToScreen(new Point(ActualWidth, ActualHeight));
            var gap = (int)Math.Round(8 * VisualTreeHelper.GetDpi(this).DpiScaleY);
            var token = _windowLifetime.Token;
            await Task.Run(() => SystemShortcuts.OpenQuickSettings((int)corner.X, (int)corner.Y, gap, token));
        }
        finally
        {
            _openingQuickSettings = false;
        }
    }

    private async void RefreshInputIndicator(object? sender, EventArgs e)
    {
        if (_inputRefreshPending)
        {
            return;
        }

        _inputRefreshPending = true;
        try
        {
            var input = await Task.Run(SystemStatus.ReadInputLanguage);
            if (_closing || input is not { } detected)
            {
                return;
            }

            InputText.Text = detected.Label;
            InputIndicator.ToolTip = detected.Name;
        }
        finally
        {
            _inputRefreshPending = false;
        }
    }

    private void RefreshClock(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _clock.Stop();
        _clock.Interval = TimeSpan.FromTicks(TimeSpan.TicksPerMinute - (now.Ticks % TimeSpan.TicksPerMinute));
        _clock.Start();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var theme = sender == DarkThemeItem ? TopBarTheme.Dark
            : sender == LightThemeItem ? TopBarTheme.Light
            : TopBarTheme.System;
        SaveAppearance(_settings with { Theme = theme });
    }

    private void Acrylic_Click(object sender, RoutedEventArgs e) =>
        SaveAppearance(_settings with { AcrylicEnabled = AcrylicItem.IsChecked });

    private void SaveAppearance(AppearanceSettings updated)
    {
        if (updated == _settings)
        {
            UpdateThemeMenu();
            return;
        }

        if (!updated.TrySave(AppearanceSettings.FilePath))
        {
            UpdateThemeMenu();
            MessageBox.Show(this, "无法保存顶栏配置，已恢复之前的选项。", "Windows Top Bar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings = updated;
        UpdateThemeMenu();
        RefreshAppearance();
    }

    private void UpdateThemeMenu()
    {
        DarkThemeItem.IsChecked = _settings.Theme == TopBarTheme.Dark;
        LightThemeItem.IsChecked = _settings.Theme == TopBarTheme.Light;
        SystemThemeItem.IsChecked = _settings.Theme == TopBarTheme.System;
        AcrylicItem.IsChecked = _settings.AcrylicEnabled;
    }
}
