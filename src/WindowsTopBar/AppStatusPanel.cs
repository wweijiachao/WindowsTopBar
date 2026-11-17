using System.Windows;
using System.Windows.Controls;

namespace WindowsTopBar;

internal sealed class AppStatusPanel : Panel
{
    private double _reservedLeft;

    public void ReserveLeft(double width)
    {
        if (width != _reservedLeft)
        {
            _reservedLeft = width;
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

        // The optional buttons must never set a minimum width for the bar.
        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        // Place whole buttons from right to left, reserving space for their trailing separator.
        var separatorIndex = InternalChildren.Count - 1;
        var separator = InternalChildren[separatorIndex];
        var separatorWidth = separator.DesiredSize.Width;
        var nextRight = finalSize.Width - separatorWidth;
        var hasVisibleButton = false;
        for (var i = separatorIndex - 1; i >= 0; i--)
        {
            var button = InternalChildren[i];
            var width = button.DesiredSize.Width;
            var left = nextRight - width;
            if (width > 0 && left >= _reservedLeft)
            {
                button.Arrange(new Rect(left, 0, width, finalSize.Height));
                nextRight = left;
                hasVisibleButton = true;
            }
            else
            {
                button.Arrange(new Rect(finalSize.Width, 0, width, finalSize.Height));
            }
        }

        separator.Arrange(new Rect(hasVisibleButton ? finalSize.Width - separatorWidth : finalSize.Width,
            0, separatorWidth, finalSize.Height));
        return finalSize;
    }
}
