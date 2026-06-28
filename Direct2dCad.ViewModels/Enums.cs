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
    [LocalizedDescription("Text", typeof(Strings))]
    Text,
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
    None,
    Axes,
    Marker,
    AxesAndMarker
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginMarkerType
{
    Cross,
    X,
    Circle,
    Square
}

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginLinePattern
{
    Solid,
    Dash,
    Dot,
    DashDot
}
