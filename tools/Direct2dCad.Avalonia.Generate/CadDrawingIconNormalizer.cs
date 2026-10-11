using System.Globalization;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

internal static class CadDrawingIconNormalizer
{
    private const string Brush = "{DynamicResource MaterialBodyBrush}";
    private const double Stroke = 1.65;
    private sealed record Shape(Geometry Geometry, double Left, double Top, bool Stroked, double Weight, string? Dash, string? Opacity);

    internal static string? Material(string? kind)
    {
        var data = kind switch
        {
            "ArrowAll" => "M12,2V22 M2,12H22 M8,6L12,2L16,6 M8,18L12,22L16,18 M6,8L2,12L6,16 M18,8L22,12L18,16",
            "RectangleOutline" => "M3,4H21V20H3Z",
            "TextShadow" => "M4,4H20 M12,4V20 M8,20H16",
            "Target" => "M12,2V22 M2,12H22 M19,12A7,7 0 1 1 5,12A7,7 0 1 1 19,12",
            "Undo" => "M4,8H12C17,8 20,11 20,16V20 M4,8L8,4 M4,8L8,12",
            "Redo" => "M20,8H12C7,8 4,11 4,16V20 M20,8L16,4 M20,8L16,12",
            "ContentCopy" => "M8,8H20V21H8Z M16,8V3H3V16H8",
            "ContentCut" => "M9,7L20,20 M9,17L20,4 M9,6A3,3 0 1 1 3,6A3,3 0 1 1 9,6 M9,18A3,3 0 1 1 3,18A3,3 0 1 1 9,18",
            "ContentPaste" => "M9,4H5V21H19V4H15 M9,2H15V6H9Z",
            "Delete" => "M3,5H21 M8,5V2H16V5 M6,5L7,21H17L18,5 M10,9V17 M14,9V17",
            "SelectAll" => "M7,7H17V17H7Z M3,7V3H7 M17,3H21V7 M21,17V21H17 M7,21H3V17 M3,10V14 M10,3H14 M21,10V14 M10,21H14",
            "SelectionOff" => "M3,7V3H7 M17,3H21V7 M21,17V21H17 M7,21H3V17 M3,10V14 M10,3H14 M21,10V14 M10,21H14 M3,3L21,21",
            "FitToScreen" => "M3,8V3H8 M16,3H21V8 M21,16V21H16 M8,21H3V16 M8,8H16V16H8Z",
            "ContentSave" or "ContentSaveOutline" => "M4,3H17L21,7V21H4Z M8,3V9H16V3 M8,21V14H17V21",
            "ContentSaveAdd" => "M4,3H17L21,7V12 M4,3V21H12 M8,3V9H16V3 M8,21V14H12 M18,14V22 M14,18H22",
            "ShapePlus" => "M3,3H9V9H3Z M21,6A3,3 0 1 1 15,6A3,3 0 1 1 21,6 M6,14L10,21H2Z M18,14V22 M14,18H22",
            "ImagePlus" => "M14,21H3V3H21V14 M3,17L8,12L13,17L16,14 M9,7A1.5,1.5 0 1 1 6,7A1.5,1.5 0 1 1 9,7 M18,15V23 M14,19H22",
            "Settings" or "Cog" => "M10,2H14L14.5,5L17,6L20,4.8L22,8.2L19.5,10V14L22,15.8L20,19.2L17,18L14.5,19L14,22H10L9.5,19L7,18L4,19.2L2,15.8L4.5,14V10L2,8.2L4,4.8L7,6L9.5,5Z M15.5,12A3.5,3.5 0 1 1 8.5,12A3.5,3.5 0 1 1 15.5,12",
            _ => null
        };
        return data is null ? null : GeometryIcon(new XElement("DataTemplate", new XElement("Path", new XAttribute("Data", data), new XAttribute("Stroke", Brush), new XAttribute("StrokeThickness", Stroke), new XAttribute("Fill", "Transparent"))));
    }

    internal static string? GeometryIcon(XElement source)
    {
        var elements = source.Descendants().Where(e => e.Name.LocalName is "Path" or "Ellipse" or "Rectangle" or "Line" or "Polyline" or "Polygon" or "TextBlock").ToArray();
        // Paper/template icons contain real text and retain their original layout.
        if (elements.Length == 0 || elements.Any(e => e.Name.LocalName == "TextBlock")) return null;
        var shapes = elements.Select(Read).ToArray();
        var bounds = Rect.Empty;
        foreach (var shape in shapes)
        {
            var rect = shape.Geometry.Bounds; rect.Offset(shape.Left, shape.Top); bounds.Union(rect);
        }
        if (bounds.IsEmpty || Math.Max(bounds.Width, bounds.Height) <= 0) return null;
        var scale = 20 / Math.Max(bounds.Width, bounds.Height);
        var x = 12 - (bounds.X + bounds.Width / 2) * scale;
        var y = 12 - (bounds.Y + bounds.Height / 2) * scale;
        var primaryWeight = shapes.Where(s => s.Stroked).Select(s => s.Weight).DefaultIfEmpty(1).Max();
        var markup = new StringBuilder();
        foreach (var shape in shapes)
        {
            var matrix = new Matrix(scale, 0, 0, scale, x + shape.Left * scale, y + shape.Top * scale);
            var data = new StringBuilder();
            // Bake the normalization at development time while preserving native
            // arcs/Bezier curves and compact path data. The app needs no WPF code.
            foreach (var figure in PathGeometry.CreateFromGeometry(shape.Geometry).Figures)
            {
                void Point(string command, Point point) { point = matrix.Transform(point); data.Append(command).Append(N(point.X)).Append(',').Append(N(point.Y)); }
                Point("M", figure.StartPoint);
                foreach (var segment in figure.Segments)
                {
                    if (segment is PolyLineSegment line) foreach (var point in line.Points) Point("L", point);
                    else if (segment is LineSegment straight) Point("L", straight.Point);
                    else if (segment is BezierSegment curve) { Point("C", curve.Point1); Point(" ", curve.Point2); Point(" ", curve.Point3); }
                    else if (segment is PolyBezierSegment curves)
                        for (var i = 0; i < curves.Points.Count; i += 3) { Point("C", curves.Points[i]); Point(" ", curves.Points[i + 1]); Point(" ", curves.Points[i + 2]); }
                    else if (segment is QuadraticBezierSegment quadratic) { Point("Q", quadratic.Point1); Point(" ", quadratic.Point2); }
                    else if (segment is PolyQuadraticBezierSegment quadratics)
                        for (var i = 0; i < quadratics.Points.Count; i += 2) { Point("Q", quadratics.Points[i]); Point(" ", quadratics.Points[i + 1]); }
                    else if (segment is ArcSegment arc)
                    {
                        data.Append('A').Append(N(arc.Size.Width * scale)).Append(',').Append(N(arc.Size.Height * scale)).Append(' ').Append(N(arc.RotationAngle)).Append(' ').Append(arc.IsLargeArc ? '1' : '0').Append(',').Append(arc.SweepDirection == SweepDirection.Clockwise ? '1' : '0').Append(' ');
                        Point("", arc.Point);
                    }
                    else throw new InvalidOperationException("Unsupported CAD icon segment.");
                }
                if (figure.IsClosed) data.Append('Z');
            }
            markup.Append("<Path Data=\"").Append(data).Append('"');
            if (shape.Stroked)
            {
                markup.Append($" Stroke=\"{Brush}\" StrokeThickness=\"{N(Stroke * Math.Clamp(shape.Weight / primaryWeight, .55, 1))}\" StrokeJoin=\"Round\" StrokeLineCap=\"Round\" Fill=\"Transparent\"");
                if (shape.Dash is { } dash) markup.Append($" StrokeDashArray=\"{SecurityElement.Escape(dash.Replace(' ', ','))}\"");
            }
            else markup.Append($" Fill=\"{Brush}\"");
            if (shape.Opacity is { } opacity) markup.Append($" Opacity=\"{SecurityElement.Escape(opacity)}\"");
            markup.Append(" />");
        }
        return Wrap(markup.ToString());
    }

    private static Shape Read(XElement element)
    {
        double A(string name, double fallback = 0) => double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
        Geometry geometry = element.Name.LocalName switch
        {
            "Path" => Geometry.Parse((string?)element.Attribute("Data") ?? ""),
            "Ellipse" => new EllipseGeometry(new Rect(0, 0, A("Width"), A("Height"))),
            "Rectangle" => new RectangleGeometry(new Rect(0, 0, A("Width"), A("Height"))),
            "Line" => new LineGeometry(new Point(A("X1"), A("Y1")), new Point(A("X2"), A("Y2"))),
            "Polyline" or "Polygon" => Geometry.Parse("M" + (string?)element.Attribute("Points") + (element.Name.LocalName == "Polygon" ? "Z" : "")),
            _ => throw new InvalidOperationException("Unsupported CAD icon shape.")
        };
        return new Shape(geometry, A("Canvas.Left"), A("Canvas.Top"), element.Attribute("Stroke") is not null || ((string?)element.Attribute("Style"))?.Contains("StrokePath") == true, A("StrokeThickness", 2), (string?)element.Attribute("StrokeDashArray"), (string?)element.Attribute("Opacity"));
    }

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Wrap(string shapes) => $"<Viewbox Width=\"26\" Height=\"26\"><Canvas Width=\"24\" Height=\"24\">{shapes}</Canvas></Viewbox>";
}
