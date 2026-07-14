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
    [LocalizedDescription("CircleCenterRadius", typeof(Strings))]
    CircleCenterRadius,
    [LocalizedDescription("CircleCenterDiameter", typeof(Strings))]
    CircleCenterDiameter,
    [LocalizedDescription("CircleTwoPoint", typeof(Strings))]
    CircleTwoPoint,
    [LocalizedDescription("CircleThreePoint", typeof(Strings))]
    CircleThreePoint,
    [LocalizedDescription("EllipseCenter", typeof(Strings))]
    EllipseCenter,
    [LocalizedDescription("EllipseAxisEnd", typeof(Strings))]
    EllipseAxisEnd,
    [LocalizedDescription("EllipseArc", typeof(Strings))]
    EllipseArc,
    [LocalizedDescription("ArcThreePoint", typeof(Strings))]
    ArcThreePoint,
    [LocalizedDescription("ArcStartCenterEnd", typeof(Strings))]
    ArcStartCenterEnd,
    [LocalizedDescription("ArcStartCenterAngle", typeof(Strings))]
    ArcStartCenterAngle,
    [LocalizedDescription("ArcStartCenterLength", typeof(Strings))]
    ArcStartCenterLength,
    [LocalizedDescription("ArcStartEndAngle", typeof(Strings))]
    ArcStartEndAngle,
    [LocalizedDescription("ArcStartEndDirection", typeof(Strings))]
    ArcStartEndDirection,
    [LocalizedDescription("ArcStartEndRadius", typeof(Strings))]
    ArcStartEndRadius,
    [LocalizedDescription("ArcCenterStartEnd", typeof(Strings))]
    ArcCenterStartEnd,
    [LocalizedDescription("ArcCenterStartAngle", typeof(Strings))]
    ArcCenterStartAngle,
    [LocalizedDescription("ArcCenterStartLength", typeof(Strings))]
    ArcCenterStartLength,
    [LocalizedDescription("ArcContinue", typeof(Strings))]
    ArcContinue,
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
    SetOrigin,
    [LocalizedDescription("InsertBlock", typeof(Strings))]
    InsertBlock,
    [LocalizedDescription("LayoutViewportMode", typeof(Strings))]
    LayoutViewport
}
