using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowsTopBar;

internal static class WindowBackdrop
{
    private const int UseImmersiveDarkMode = 20;
    private const int SystemBackdropType = 38;
    private const int NoBackdrop = 1;
    private const int DesktopAcrylic = 3;

    public static bool Apply(HwndSource source, bool enabled, bool dark)
    {
        if (source.CompositionTarget is not HwndTarget target)
        {
            return false;
        }

        var hwnd = source.Handle;
        if (enabled)
        {
            int darkMode = dark ? 1 : 0;
            var glass = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (SetAttribute(hwnd, UseImmersiveDarkMode, darkMode) &&
                SetAttribute(hwnd, SystemBackdropType, DesktopAcrylic) &&
                DwmExtendFrameIntoClientArea(hwnd, in glass) >= 0)
            {
                target.BackgroundColor = Colors.Transparent;
                return true;
            }
        }

        SetAttribute(hwnd, SystemBackdropType, NoBackdrop);
        var none = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, in none);
        target.BackgroundColor = Colors.Black;
        return false;
    }

    private static bool SetAttribute(nint hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, in value, sizeof(int)) >= 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, in Margins margins);
}
