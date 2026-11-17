using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private const byte AcrylicTintOpacity = 180;
    private const double EdgeTintStrength = 0.12;
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Color LightBackground = Color.FromRgb(241, 239, 235);
    private static readonly Color DarkBackground = Color.FromRgb(38, 39, 41);
    private static readonly Color LightText = Color.FromRgb(229, 230, 232);
    private static readonly Color DarkText = Color.FromRgb(51, 53, 58);

    private readonly DispatcherTimer _clock = new();
    private readonly UsageReminder _usageReminder = new();
    private UsageSystemEvents? _usageSystemEvents;
    private UsagePhase _usagePhase;
    private readonly MessengerWindows _messengers = new();
    private long _qqFlashVersion;
    private long _weChatFlashVersion;
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
    private Task<string?>? _clashVergeToolTipRead;
    private readonly DispatcherTimer _clashVergeClickTimer = new();
    private bool _clashVergeDoubleClick;
    private bool _clashVergeTunEnabled;
    private ContextMenu? _shortcutsMenu;
    private int? _shortcutsDismissPress;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppearanceSettings.Load(AppearanceSettings.FilePath);
        UpdateThemeMenu();
        _clock.Tick += RefreshClock;
        _tintTimer.Tick += RefreshEdgeTint;
        _statusTimer.Tick += RefreshStatus;
        _inputTimer.Tick += RefreshInputIndicator;
        _clashVergeClickTimer.Tick += ClashVergeSingleClick;
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
        _usageSystemEvents = new UsageSystemEvents(_source.Handle);
        _usageReminder.SetEnabled(_settings.UsageReminderEnabled, UsageSystemEvents.AwakeMilliseconds);
        _messengers.ForegroundWindowChanged += OnMessengerForegroundChanged;
        _messengers.Register(_source.Handle);
        _appBar = new AppBar(_source, Height);
        _appBar.CenterOffsetChanged += ApplyCentralHubOffset;
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
        CentralHubMenu.IsOpen = false;
        _usageSystemEvents?.Dispose();
        _clashVergeClickTimer.Stop();
        _clashVergeClickTimer.Tick -= ClashVergeSingleClick;
        StopMessengerFlash(Messenger.QQ);
        StopMessengerFlash(Messenger.WeChat);
        if (_shortcutsMenu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }

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
        _messengers.ForegroundWindowChanged -= OnMessengerForegroundChanged;
        _messengers.Dispose();
        if (_appBar is not null)
        {
            _appBar.CenterOffsetChanged -= ApplyCentralHubOffset;
            _appBar.Dispose();
        }
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
        UsageReminderSolid.Fill = highContrast ? SystemColors.WindowTextBrush : FrozenBrush(dark
            ? Color.FromRgb(217, 173, 106)
            : Color.FromRgb(148, 99, 30));
        ClashVergeTunDot.Stroke = highContrast ? SystemColors.WindowBrush : FrozenBrush(background);

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

        if (!_closing && UsageSystemEvents.ReadInterruption(message, wParam, lParam) is { } change)
        {
            var now = UsageSystemEvents.AwakeMilliseconds;
            _usageReminder.SetInterrupted(change.Reason, change.Interrupted, now, Environment.TickCount64);
            UpdateUsageReminder(now);
        }

        if (message == WmTimeChange || (message == UsageSystemEvents.WmPowerBroadcast && wParam == UsageSystemEvents.PbtApmResumeAutomatic))
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
        RefreshUsageReminder();
        if (_statusRefreshPending)
        {
            return;
        }

        _statusRefreshPending = true;
        try
        {
            var qqFlashAtStart = _qqFlashVersion;
            var weChatFlashAtStart = _weChatFlashVersion;
            var (battery, wifi, audio, messengers, clashVerge) = await Task.Run(() =>
                (SystemStatus.ReadBattery(), SystemStatus.ReadWifi(), AudioStatus.Read(),
                    MessengerWindows.ReadRunning(), ClashVergeTray.ReadStatus()));
            if (_closing)
            {
                return;
            }

            // A new flash can arrive while the visibility snapshot is being read.
            if (_qqFlashVersion == qqFlashAtStart)
            {
                UpdateMessengerVisibility(Messenger.QQ, messengers.QQ);
            }
            if (_weChatFlashVersion == weChatFlashAtStart)
            {
                UpdateMessengerVisibility(Messenger.WeChat, messengers.WeChat);
            }
            ClashVergeButton.Visibility = clashVerge.Running ? Visibility.Visible : Visibility.Collapsed;
            _clashVergeTunEnabled = clashVerge.TunEnabled;
            UpdateClashVergeTunDot();
            // A window can still be restoring when its foreground event arrives.
            if (HasMessengerFlash && MessengerWindows.ReadForegroundMessenger() is { } foreground)
            {
                StopMessengerFlash(foreground);
            }

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

    private void UpdateMessengerVisibility(Messenger messenger, bool running)
    {
        var isQq = messenger == Messenger.QQ;
        var button = isQq ? QQButton : WeChatButton;
        button.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running)
        {
            StopMessengerFlash(messenger);
        }
    }

    private void StopMessengerFlash(Messenger messenger) =>
        (messenger == Messenger.QQ ? QQGlyph : WeChatGlyph).BeginAnimation(OpacityProperty, null);

    private bool HasMessengerFlash => QQGlyph.HasAnimatedProperties || WeChatGlyph.HasAnimatedProperties;

    private void OnMessengerForegroundChanged(nint window)
    {
        if (!_closing && HasMessengerFlash && MessengerWindows.ReadAttentionWindow(window) is { } messenger)
        {
            StopMessengerFlash(messenger);
        }
    }

    private void FlashMessenger(Messenger messenger)
    {
        // A queued flash can be delivered after the app switches to the foreground.
        if (MessengerWindows.ReadForegroundMessenger() == messenger)
        {
            StopMessengerFlash(messenger);
            return;
        }

        var isQq = messenger == Messenger.QQ;
        var button = isQq ? QQButton : WeChatButton;
        var glyph = isQq ? QQGlyph : WeChatGlyph;
        button.Visibility = Visibility.Visible;

        ref var flashVersion = ref (isQq ? ref _qqFlashVersion : ref _weChatFlashVersion);
        flashVersion++;
        if (!glyph.HasAnimatedProperties)
        {
            glyph.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.18, TimeSpan.FromMilliseconds(300))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }
    }

    private void BarBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _appBar?.ActivateWindowBelow((int)PointToScreen(e.GetPosition(this)).X);

    private void ApplyCentralHubOffset(double offset)
    {
        CentralHubOffset.X = offset;
        ReserveCentralHubSpace();
    }

    private void BarLayout_SizeChanged(object sender, SizeChangedEventArgs e) => ReserveCentralHubSpace();

    private void ReserveCentralHubSpace()
    {
        var buttonRight = (BarLayout.ActualWidth + CentralHubButton.Width) / 2 + CentralHubOffset.X;
        AppStatusArea.ReserveLeft(Math.Max(0, buttonRight - ShortcutsButton.Width));
    }

    private void Shortcuts_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_shortcutsDismissPress == e.Timestamp)
        {
            e.Handled = true;
        }

        _shortcutsDismissPress = null;
    }

    private void ShortcutsMenu_PreviewMouseDownOutside(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // WPF forwards the press that dismissed the menu to the underlying button.
            _shortcutsDismissPress = e.Timestamp;
        }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (_shortcutsMenu is { IsOpen: true } openMenu)
        {
            openMenu.IsOpen = false;
            return;
        }

        var menu = new ContextMenu { PlacementTarget = ShortcutsButton, Placement = PlacementMode.Bottom };
        menu.AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent,
            new MouseButtonEventHandler(ShortcutsMenu_PreviewMouseDownOutside), handledEventsToo: true);
        try
        {
            foreach (var path in ShortcutDirectory.List(ShortcutDirectory.GetConfiguredFolder()))
            {
                // TextBlock preserves underscores in the filename as literal text.
                var name = Path.GetFileNameWithoutExtension(path);
                var initial = name.Length == 0 ? string.Empty : StringInfo.GetNextTextElement(name);
                if (initial.Length == 1 && initial[0] is >= 'a' and <= 'z')
                {
                    initial = initial.ToUpperInvariant();
                }

                var item = new MenuItem
                {
                    Header = new TextBlock { Text = name },
                    Icon = new TextBlock
                    {
                        Text = initial,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                AutomationProperties.SetName(item, name);
                item.Click += (_, _) => LaunchShortcut(path);
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "快捷方式目录为空", IsEnabled = false });
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            menu.Items.Add(new MenuItem { Header = "无法读取快捷方式目录", IsEnabled = false });
        }

        _shortcutsMenu = menu;
        menu.IsOpen = true;
    }

    private void OpenShortcutsFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(ShortcutDirectory.GetConfiguredFolder, "快速启动目录");

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        AppearanceSettings.PrepareForEditing(AppearanceSettings.FilePath);
        OpenFolder(() => AppearanceSettings.Folder, "配置文件目录");
    }

    private void OpenFolder(Func<string> path, string label)
    {
        try
        {
            ShortcutDirectory.OpenFolder(path());
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, $"无法打开{label}。", "Windows Top Bar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private void UpdateClashVergeTunDot() =>
        ClashVergeTunDot.Visibility = _clashVergeTunEnabled ? Visibility.Visible : Visibility.Collapsed;

    private async void ClashVerge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _clashVergeDoubleClick = false;
        if (!_clashVergeClickTimer.IsEnabled)
        {
            return;
        }

        // Cancel at the second press so a double-click cannot toggle TUN before the release.
        _clashVergeClickTimer.Stop();
        _clashVergeDoubleClick = e.ClickCount == 2;
        if (!_clashVergeDoubleClick)
        {
            await RunClashVergeCommand("toggle_tun_mode");
        }
    }

    private async void ClashVerge_Click(object sender, RoutedEventArgs e)
    {
        if (_clashVergeDoubleClick)
        {
            _clashVergeDoubleClick = false;
            await RunClashVergeCommand("dashboard");
            return;
        }

        _clashVergeClickTimer.Interval = ClashVergeTray.DoubleClickInterval;
        _clashVergeClickTimer.Start();
    }

    private async void ClashVergeSingleClick(object? sender, EventArgs e)
    {
        _clashVergeClickTimer.Stop();
        await RunClashVergeCommand("toggle_tun_mode");
    }

    private async void ClashVergeToolTip_Opened(object sender, RoutedEventArgs e)
    {
        ClashVergeToolTipText.Text = "Clash Verge";
        var read = _clashVergeToolTipRead ??= Task.Run(ClashVergeTray.ReadTooltip);
        var text = await read;
        if (_clashVergeToolTipRead == read)
        {
            _clashVergeToolTipRead = null;
        }
        if (!_closing && ClashVergeToolTip.IsOpen)
        {
            ClashVergeToolTipText.Text = text ?? "Clash Verge\n托盘状态暂不可用";
        }
    }

    private void ClashVerge_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        ClashVergeToolTip.IsOpen = false;

    private async void ClashVergeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string command })
        {
            await RunClashVergeCommand(command);
        }
    }

    private async Task RunClashVergeCommand(string command)
    {
        if (_closing)
        {
            return;
        }

        var token = _windowLifetime.Token;
        var error = await Task.Run(() => ClashVergeTray.RunCommand(command, token));
        if (!_closing && error is not null)
        {
            MessageBox.Show(this, error, "Clash Verge", MessageBoxButton.OK, MessageBoxImage.Information);
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
        ClockArea.ToolTip = now.ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
        _clock.Stop();
        _clock.Interval = TimeSpan.FromTicks(TimeSpan.TicksPerMinute - (now.Ticks % TimeSpan.TicksPerMinute));
        _clock.Start();
    }

    private void RefreshUsageReminder()
    {
        if (!_usageReminder.IsAvailable)
        {
            return;
        }

        var now = UsageSystemEvents.AwakeMilliseconds;
        if (_usageReminder.IsResting && _usageReminder.ShouldCheckInput(Environment.TickCount64) &&
            UsageSystemEvents.ReadLastInputMilliseconds() is long lastInput)
        {
            _usageReminder.ObserveInput(lastInput, now);
        }

        UpdateUsageReminder(now);
    }

    private void UpdateUsageReminder(long now)
    {
        var phase = _usageReminder.GetPhase(now, _settings.UsageReminderFirstMinutes, _settings.UsageReminderSecondMinutes);
        if (phase != _usagePhase)
        {
            _usagePhase = phase;
            UsageReminderRing.Visibility = phase is UsagePhase.Hidden or UsagePhase.Normal or UsagePhase.FirstReminder
                ? Visibility.Visible : Visibility.Collapsed;
            UsageReminderHalf.Visibility = phase == UsagePhase.FirstReminder ? Visibility.Visible : Visibility.Collapsed;
            UsageReminderSolid.Visibility = phase == UsagePhase.SecondReminder ? Visibility.Visible : Visibility.Collapsed;
            UsageReminderPause.Visibility = phase == UsagePhase.Resting ? Visibility.Visible : Visibility.Collapsed;
            UsageStartRestItem.Visibility = _usageReminder.CanStartRest ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CentralHubMenu.IsOpen)
        {
            var status = !_settings.UsageReminderEnabled ? "使用提醒已关闭" : DescribeUsage(now);
            if (!Equals(UsageStatusItem.Header, status))
            {
                UsageStatusItem.Header = status;
            }
        }
    }

    private string DescribeUsage(long now)
    {
        return _usagePhase switch
        {
            UsagePhase.Hidden => "计时已暂停",
            UsagePhase.Resting => "休息中，下一次键鼠操作后重新计时",
            UsagePhase.FirstReminder or UsagePhase.SecondReminder =>
                $"本轮已使用 {_usageReminder.ElapsedMinutes(now)} 分钟，建议适当活动",
            _ => $"本轮已使用 {_usageReminder.ElapsedMinutes(now)} 分钟"
        };
    }

    private void CentralHub_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        var now = UsageSystemEvents.AwakeMilliseconds;
        UpdateUsageReminder(now);
        CentralHubTip.Content = _settings.UsageReminderEnabled ? DescribeUsage(now) : "WindowsTopBar";
    }

    private void CentralHub_Click(object sender, RoutedEventArgs e)
    {
        CentralHubMenu.PlacementTarget = CentralHubButton;
        CentralHubMenu.IsOpen = true;
    }

    private void CentralHubMenu_Opened(object sender, RoutedEventArgs e) =>
        UpdateUsageReminder(UsageSystemEvents.AwakeMilliseconds);

    private void UsageStartRest_Click(object sender, RoutedEventArgs e)
    {
        _usageReminder.StartRest(Environment.TickCount64);
        UpdateUsageReminder(UsageSystemEvents.AwakeMilliseconds);
    }

    private void UsageReminderEnabled_Click(object sender, RoutedEventArgs e) =>
        SaveAppearance(_settings with { UsageReminderEnabled = UsageReminderEnabledItem.IsChecked });

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

        // Preserve edits to other preferences in settings.json while the bar is running.
        var saved = AppearanceSettings.Load(AppearanceSettings.FilePath);
        updated = saved with
        {
            Theme = updated.Theme != _settings.Theme ? updated.Theme : saved.Theme,
            AcrylicEnabled = updated.AcrylicEnabled != _settings.AcrylicEnabled ? updated.AcrylicEnabled : saved.AcrylicEnabled,
            UsageReminderEnabled = updated.UsageReminderEnabled != _settings.UsageReminderEnabled
                ? updated.UsageReminderEnabled : saved.UsageReminderEnabled
        };
        if (!updated.TrySave(AppearanceSettings.FilePath))
        {
            UpdateThemeMenu();
            MessageBox.Show(this, "无法保存顶栏配置，已恢复之前的选项。", "Windows Top Bar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var appearanceChanged = updated.Theme != _settings.Theme || updated.AcrylicEnabled != _settings.AcrylicEnabled;
        _settings = updated;
        var now = UsageSystemEvents.AwakeMilliseconds;
        _usageReminder.SetEnabled(_settings.UsageReminderEnabled, now);
        UpdateUsageReminder(now);
        UpdateThemeMenu();
        if (appearanceChanged)
        {
            RefreshAppearance();
        }
    }

    private void UpdateThemeMenu()
    {
        DarkThemeItem.IsChecked = _settings.Theme == TopBarTheme.Dark;
        LightThemeItem.IsChecked = _settings.Theme == TopBarTheme.Light;
        SystemThemeItem.IsChecked = _settings.Theme == TopBarTheme.System;
        AcrylicItem.IsChecked = _settings.AcrylicEnabled;
        UsageReminderEnabledItem.IsChecked = _settings.UsageReminderEnabled;
    }
}
