using System.ComponentModel;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels;

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum CadCanvasToolMode
{
    [LocalizedDescription("Select", typeof(Strings))]
    Select,
    [LocalizedDescription("Line", typeof(Strings))]
    Line,
    [LocalizedDescription("Circle", typeof(Strings))]
    Circle,
    [LocalizedDescription("Ellipse", typeof(Strings))]
    Ellipse,
    [LocalizedDescription("Arc", typeof(Strings))]
    Arc,
    [LocalizedDescription("Rectangle", typeof(Strings))]
    Rectangle,
    [LocalizedDescription("Polyline", typeof(Strings))]
    Polyline,
    [LocalizedDescription("Polygon", typeof(Strings))]
    Polygon,
    [LocalizedDescription("Spline", typeof(Strings))]
    Spline,
    [LocalizedDescription("Text", typeof(Strings))]
    Text,
    [LocalizedDescription("ShapeText", typeof(Strings))]
    ShapeText,
    [LocalizedDescription("SetOrigin", typeof(Strings))]
    SetOrigin
}



[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadGridType
{
    [LocalizedDescription("None", typeof(Strings))]
    None,
    [LocalizedDescription("Dots", typeof(Strings))]
    Dots,
    [LocalizedDescription("Lines", typeof(Strings))]
    Lines,
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadSnapMarkerType
{
    [LocalizedDescription("None", typeof(Strings))]
    None,
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross,
    [LocalizedDescription("X", typeof(Strings))]
    X,
    [LocalizedDescription("Square", typeof(Strings))]
    Square
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginDisplayType
{
    [LocalizedDescription("None", typeof(Strings))]
    None,
    [LocalizedDescription("Axes", typeof(Strings))]
    Axes,
    [LocalizedDescription("Marker", typeof(Strings))]
    Marker,
    [LocalizedDescription("AxesAndMarker", typeof(Strings))]
    AxesAndMarker
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginMarkerType
{
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross,
    [LocalizedDescription("X", typeof(Strings))]
    X,
    [LocalizedDescription("Circle", typeof(Strings))]
    Circle,
    [LocalizedDescription("Square", typeof(Strings))]
    Square
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginLinePattern
{
    [LocalizedDescription("Solid", typeof(Strings))]
    Solid,
    [LocalizedDescription("Dash", typeof(Strings))]
    Dash,
    [LocalizedDescription("Dot", typeof(Strings))]
    Dot,
    [LocalizedDescription("DashDot", typeof(Strings))]
    DashDot
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadShapeFont
{
    [LocalizedDescription("Unicode", typeof(Strings))]
    Unicode,
    [LocalizedDescription("Simplex", typeof(Strings))]
    Simplex,
    [LocalizedDescription("MonoLine", typeof(Strings))]
    MonoLine,
    [LocalizedDescription("BoxFallback", typeof(Strings))]
    BoxFallback
}
