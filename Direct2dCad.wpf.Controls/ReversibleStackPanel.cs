using System.Windows;
using System.Windows.Controls;

namespace Direct2dCad.wpf.Controls;


public class ReversibleStackPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty =
        StackPanel.OrientationProperty.AddOwner(typeof(ReversibleStackPanel));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty ReverseOrderProperty =
        DependencyProperty.Register(
            nameof(ReverseOrder),
            typeof(bool),
            typeof(ReversibleStackPanel),
            new PropertyMetadata(false, (d, e) => ((ReversibleStackPanel)d).InvalidateArrange()));

    public bool ReverseOrder
    {
        get => (bool)GetValue(ReverseOrderProperty);
        set => SetValue(ReverseOrderProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            if (Orientation == Orientation.Horizontal)
            {
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            else
            {
                height += child.DesiredSize.Height;
                width = Math.Max(width, child.DesiredSize.Width);
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        double x = 0;
        double y = 0;

        IEnumerable<UIElement> children = ReverseOrder ? InternalChildren.Cast<UIElement>().Reverse() : InternalChildren.Cast<UIElement>();
        foreach (UIElement child in children)
        {
            Size size;

            if (Orientation == Orientation.Horizontal)
            {
                size = new Size(child.DesiredSize.Width, Math.Max(arrangeSize.Height, child.DesiredSize.Height));
                child.Arrange(new Rect(new Point(x, y), size));
                x += size.Width;
            }
            else
            {
                size = new Size(Math.Max(arrangeSize.Width, child.DesiredSize.Width), child.DesiredSize.Height);
                child.Arrange(new Rect(new Point(x, y), size));
                y += size.Height;
            }
        }

        return Orientation == Orientation.Horizontal ? new Size(x, arrangeSize.Height) : new Size(arrangeSize.Width, y);
    }
}
