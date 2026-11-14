using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsTopBar;

public partial class MainWindow : Window
{
    private const int WmTimeChange = 0x001E;
    private const int WmSettingChange = 0x001A;
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

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppearanceSettings.Load(AppearanceSettings.FilePath);
        UpdateThemeMenu();
        _clock.Tick += RefreshClock;
        _tintTimer.Tick += RefreshEdgeTint;
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

        _clock.Stop();
        _clock.Tick -= RefreshClock;
        _tintTimer.Stop();
        _tintTimer.Tick -= RefreshEdgeTint;
        _pendingAppearance?.Abort();
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
