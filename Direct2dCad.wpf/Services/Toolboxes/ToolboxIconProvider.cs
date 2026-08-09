using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Toolboxes;

internal sealed class ToolboxIconProvider : IToolboxIconProvider
{
    public object Explorer => CreateExplorerIcon();
    public object Layers => CreateLayersIcon();
    public object Blocks => CreateBlocksIcon();
    public object Terminal => CreateTerminalIcon();
    public object Search => CreateSearchIcon();
    public object Filter => CreateFilterIcon();
    public object Git => CreateGitIcon();
    public object Problems => CreateProblemsIcon();
    public object Assistant => CreateAssistantIcon();
    public object Messages => CreateMessagesIcon();

    private static Binding ForegroundBinding() => new()
    {
        Path = new PropertyPath(TextElement.ForegroundProperty),
        RelativeSource = new RelativeSource(RelativeSourceMode.Self)
    };

    private static Viewbox CreateAssistantIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        var bubble = new Path
        {
            Data = Geometry.Parse("M2,2.5 C2,1.7 2.7,1 3.5,1 L12.5,1 C13.3,1 14,1.7 14,2.5 L14,10 C14,10.8 13.3,11.5 12.5,11.5 L7,11.5 L3.5,14.5 L4.1,11.5 L3.5,11.5 C2.7,11.5 2,10.8 2,10 Z"),
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        bubble.SetBinding(Shape.StrokeProperty, ForegroundBinding());
        var sparkle = new Path
        {
            Data = Geometry.Parse("M8,3 L8.7,5.3 L11,6 L8.7,6.7 L8,9 L7.3,6.7 L5,6 L7.3,5.3 Z"),
            StrokeThickness = 0.7,
            Fill = Brushes.Transparent
        };
        sparkle.SetBinding(Shape.StrokeProperty, ForegroundBinding());
        canvas.Children.Add(bubble);
        canvas.Children.Add(sparkle);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateMessagesIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        var bubble = new Path
        {
            Data = Geometry.Parse("M2,2.5 C2,1.7 2.7,1 3.5,1 L12.5,1 C13.3,1 14,1.7 14,2.5 L14,10 C14,10.8 13.3,11.5 12.5,11.5 L7,11.5 L3.5,14.5 L4.1,11.5 L3.5,11.5 C2.7,11.5 2,10.8 2,10 Z"),
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        bubble.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        foreach (var left in new[] { 5.0, 8.0, 11.0 })
        {
            var dot = new Ellipse { Width = 1.4, Height = 1.4 };
            Canvas.SetLeft(dot, left - 0.7);
            Canvas.SetTop(dot, 6 - 0.7);
            dot.SetBinding(Shape.FillProperty, ForegroundBinding());
            canvas.Children.Add(dot);
        }

        canvas.Children.Insert(0, bubble);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateFilterIcon()
    {
        var path = new Path
        {
            Data = Geometry.Parse("M1.5,2 L14.5,2 L9.5,7.8 L9.5,12.2 L6.5,14 L6.5,7.8 Z"),
            StrokeThickness = 1.1,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        path.SetBinding(Shape.StrokeProperty, ForegroundBinding());
        return new Viewbox { Width = 16, Height = 16, Child = path };
    }

    private static Viewbox CreateExplorerIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var folder = new Path
        {
            Data = Geometry.Parse(
                "M1.5,1 L6,1 L7.5,3 L14.5,3 C15.3,3 15.5,3.5 15.5,4 L15.5,13 C15.5,13.5 15,14 14.5,14 L1.5,14 C1,14 0.5,13.5 0.5,13 L0.5,2 C0.5,1.5 1,1 1.5,1 Z"),
            StrokeThickness = 0.8,
            Fill = Brushes.Transparent
        };
        folder.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var line = new Line { X1 = 0.5, Y1 = 5.5, X2 = 15.5, Y2 = 5.5, StrokeThickness = 0.6 };
        line.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        canvas.Children.Add(folder);
        canvas.Children.Add(line);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateLayersIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var back = CreateLayerPath();
        Canvas.SetTop(back, 4);
        var middle = CreateLayerPath();
        Canvas.SetTop(middle, 2);
        var front = CreateLayerPath(strokeThickness: 1.1);

        canvas.Children.Add(back);
        canvas.Children.Add(middle);
        canvas.Children.Add(front);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateBlocksIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        foreach (var (left, top) in new[] { (1.0, 1.0), (8.5, 1.0), (1.0, 8.5), (8.5, 8.5) })
        {
            var rectangle = new Rectangle
            {
                Width = 6.5,
                Height = 6.5,
                StrokeThickness = 0.9,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            rectangle.SetBinding(Shape.StrokeProperty, ForegroundBinding());
            canvas.Children.Add(rectangle);
        }
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Path CreateLayerPath(double strokeThickness = 0.9)
    {
        var path = new Path
        {
            Data = Geometry.Parse("M2,6 L8,3 L14,6 L8,9 Z"),
            StrokeThickness = strokeThickness,
            Fill = Brushes.Transparent
        };
        path.SetBinding(Shape.StrokeProperty, ForegroundBinding());
        return path;
    }

    private static Viewbox CreateTerminalIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var rect = new Rectangle
        {
            Width = 15,
            Height = 13,
            RadiusX = 1.5,
            RadiusY = 1.5,
            StrokeThickness = 0.8,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rect, 0.5);
        Canvas.SetTop(rect, 1.5);
        rect.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var prompt = new Path
        {
            Data = Geometry.Parse("M4,6 L7,8.5 L4,11"),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent
        };
        prompt.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var cursor = new Rectangle { Width = 4, Height = 1.2 };
        Canvas.SetLeft(cursor, 8.5);
        Canvas.SetTop(cursor, 10.5);
        cursor.SetBinding(Shape.FillProperty, ForegroundBinding());

        canvas.Children.Add(rect);
        canvas.Children.Add(prompt);
        canvas.Children.Add(cursor);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateSearchIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var circle = new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.2, Fill = Brushes.Transparent };
        Canvas.SetLeft(circle, 2);
        Canvas.SetTop(circle, 2);
        circle.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var handle = new Line { X1 = 10, Y1 = 10, X2 = 14, Y2 = 14, StrokeThickness = 1.5 };
        handle.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        canvas.Children.Add(circle);
        canvas.Children.Add(handle);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateGitIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var c1 = new Ellipse { Width = 3.5, Height = 3.5, StrokeThickness = 1, Fill = Brushes.Transparent };
        Canvas.SetLeft(c1, 2.5);
        Canvas.SetTop(c1, 2);
        c1.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var c2 = new Ellipse { Width = 3.5, Height = 3.5, StrokeThickness = 1, Fill = Brushes.Transparent };
        Canvas.SetLeft(c2, 9);
        Canvas.SetTop(c2, 2);
        c2.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var c3 = new Ellipse { Width = 3.5, Height = 3.5, StrokeThickness = 1, Fill = Brushes.Transparent };
        Canvas.SetLeft(c3, 2.5);
        Canvas.SetTop(c3, 10.5);
        c3.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var stem = new Line { X1 = 4.25, Y1 = 5.5, X2 = 4.25, Y2 = 10.5, StrokeThickness = 1 };
        stem.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var branch = new Path
        {
            Data = Geometry.Parse("M10.75,5.5 C10.75,8.5 4.25,8.5 4.25,10.5"),
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        branch.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        canvas.Children.Add(c1);
        canvas.Children.Add(c2);
        canvas.Children.Add(c3);
        canvas.Children.Add(stem);
        canvas.Children.Add(branch);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static Viewbox CreateProblemsIcon()
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        var triangle = new Path
        {
            Data = Geometry.Parse("M8,1.5 L15,13.5 L1,13.5 Z"),
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        triangle.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var excl = new Line { X1 = 8, Y1 = 6, X2 = 8, Y2 = 10, StrokeThickness = 1.2 };
        excl.SetBinding(Shape.StrokeProperty, ForegroundBinding());

        var dot = new Ellipse { Width = 1.6, Height = 1.6 };
        Canvas.SetLeft(dot, 7.2);
        Canvas.SetTop(dot, 11);
        dot.SetBinding(Shape.FillProperty, ForegroundBinding());

        canvas.Children.Add(triangle);
        canvas.Children.Add(excl);
        canvas.Children.Add(dot);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }
}
