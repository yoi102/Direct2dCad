namespace Direct2dCad.IO.FileFormat.Container;

public enum CadSectionKind : ushort
{
    Document = 1,
    Settings = 2,
    Layers = 10,
    Styles = 11,
    Lines = 100,
    Circles = 101,
    Arcs = 102,
    Rectangles = 103,
    Texts = 104,
    Polylines = 105,
    Splines = 106
}
