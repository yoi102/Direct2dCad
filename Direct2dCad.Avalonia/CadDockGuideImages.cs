using Avalonia;
using Avalonia.Media;

namespace Direct2dCad.Avalonia;

// Replace only the library Image sources; Dock retains hit testing, validation,
// preview lifetime and the native control templates.
public static class CadDockGuideImages
{
    public static DrawingImage Left { get; } = Create(new Rect(7,7,8,20));
    public static DrawingImage Right { get; } = Create(new Rect(19,7,8,20));
    public static DrawingImage Top { get; } = Create(new Rect(7,7,20,8));
    public static DrawingImage Bottom { get; } = Create(new Rect(7,19,20,8));
    public static DrawingImage Tabs { get; } = Create(new Rect(7,7,20,5),true);
    private static DrawingImage Create(Rect slot,bool tabs=false)
    {
        var drawing = new DrawingGroup();
        void Rectangle(Rect rect,IBrush fill,IPen? pen=null) => drawing.Children.Add(new GeometryDrawing {Geometry=new RectangleGeometry(rect),Brush=fill,Pen=pen});
        var blue=new SolidColorBrush(Color.Parse("#007ACC"));
        var outline=new Pen(new SolidColorBrush(Color.Parse("#D4DCE5")),1);
        Rectangle(new Rect(0,0,34,34),new SolidColorBrush(Color.Parse("#202A35")),new Pen(blue,2));
        Rectangle(new Rect(6,6,22,22),new SolidColorBrush(Color.Parse("#303B47")),outline);
        Rectangle(slot,blue);
        if(tabs)Rectangle(new Rect(15,7,1,5),Brushes.White);
        return new DrawingImage {Drawing=drawing};
    }
}
