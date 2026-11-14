using System.Runtime.InteropServices;
using System.Windows.Media;

namespace WindowsTopBar;

internal static class ScreenColorSampler
{
    private const int SampleWidth = 48;
    private const int SampleHeight = 6;
    private const int PrimaryScreenHeight = 1;
    private const uint CopySource = 0x00CC0020;

    public static Color? GetAverageColorBelow(nint window)
    {
        if (!GetWindowRect(window, out var bar) || bar.Right <= bar.Left)
        {
            return null;
        }

        // Sample a strip one bar-height deep, in physical pixels.
        var height = Math.Min(bar.Bottom - bar.Top, GetSystemMetrics(PrimaryScreenHeight) - bar.Bottom);
        if (height <= 0)
        {
            return null;
        }

        var screen = GetDC(0);
        if (screen == 0)
        {
            return null;
        }

        try
        {
            return Sample(screen, bar.Left, bar.Bottom, bar.Right - bar.Left, height);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    private static Color? Sample(nint screen, int left, int top, int width, int height)
    {
        var buffer = CreateCompatibleDC(screen);
        if (buffer == 0)
        {
            return null;
        }

        nint bitmap = 0;
        nint previous = 0;
        try
        {
            var header = new BitmapHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapHeader>(),
                Width = SampleWidth,
                Height = -SampleHeight,
                Planes = 1,
                BitCount = 32
            };
            bitmap = CreateDIBSection(buffer, ref header, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0)
            {
                return null;
            }

            previous = SelectObject(buffer, bitmap);
            // COLORONCOLOR keeps individual source colors when reducing the strip.
            if (previous == 0 || previous == -1 || SetStretchBltMode(buffer, 3) == 0 ||
                !StretchBlt(buffer, 0, 0, SampleWidth, SampleHeight, screen, left, top, width, height, CopySource) ||
                !GdiFlush())
            {
                return null;
            }

            return AverageColor(pixels);
        }
        finally
        {
            if (previous != 0 && previous != -1)
            {
                SelectObject(buffer, previous);
            }

            DeleteDC(buffer);
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }
        }
    }

    private static Color AverageColor(nint pixels)
    {
        int red = 0, green = 0, blue = 0;
        const int count = SampleWidth * SampleHeight;
        for (int offset = 0; offset < count * 4; offset += 4)
        {
            var pixel = Marshal.ReadInt32(pixels, offset);
            red += (pixel >> 16) & 255;
            green += (pixel >> 8) & 255;
            blue += pixel & 255;
        }

        return Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, uint usage, out nint pixels, nint section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int SetStretchBltMode(nint dc, int mode);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool StretchBlt(nint dest, int dx, int dy, int dw, int dh,
        nint source, int sx, int sy, int sw, int sh, uint operation);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool GdiFlush();
}
