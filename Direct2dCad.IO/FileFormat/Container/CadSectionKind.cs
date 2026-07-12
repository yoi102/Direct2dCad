namespace Direct2dCad.IO.FileFormat.Container;

public enum CadSectionKind : ushort
{
    Document = 1,
    Settings = 2,
    Layers = 10,
    Styles = 11,
    Layouts = 12,
    Lines = 100,
    Circles = 101,
    Arcs = 102,
    Rectangles = 103,
    Texts = 104,
    Polylines = 105,
    Splines = 106,
    Ellipses = 107,
    ShapeTexts = 108,
    Images = 109,
    OleObjects = 110
}
