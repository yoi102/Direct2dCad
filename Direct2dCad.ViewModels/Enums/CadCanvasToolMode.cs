using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;


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
    [LocalizedDescription("SetOrigin", typeof(Strings))]
    SetOrigin
}
