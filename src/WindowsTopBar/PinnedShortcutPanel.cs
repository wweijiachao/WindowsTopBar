using System.Windows;
using System.Windows.Controls;

namespace WindowsTopBar;

internal sealed class PinnedShortcutPanel : Panel
{
    private double _contentWidth;

    public int VisibleCount { get; private set; }
    public event EventHandler? VisibleCountChanged;

    public void SetContentWidth(double width)
    {
        if (_contentWidth != width)
        {
            _contentWidth = width;
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        // Pinned shortcuts must not push the clock or the central hub away from their fixed positions.
        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var previousCount = VisibleCount;
        var availableWidth = Math.Max(0, Math.Min(finalSize.Width, _contentWidth));
        var left = 0.0;
        var overflow = false;
        VisibleCount = 0;
        foreach (UIElement child in InternalChildren)
        {
            var width = child.DesiredSize.Width;
            overflow |= left + width > availableWidth;
            child.Visibility = overflow ? Visibility.Hidden : Visibility.Visible;
            child.Arrange(new Rect(left, 0, width, finalSize.Height));
            if (!overflow)
            {
                VisibleCount++;
            }
            left += width;
        }
        if (previousCount != VisibleCount)
        {
            VisibleCountChanged?.Invoke(this, EventArgs.Empty);
        }
        return finalSize;
    }
}
