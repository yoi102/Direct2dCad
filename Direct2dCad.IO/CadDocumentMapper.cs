using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Common;
using Direct2dCad.IO.FileFormat.Entities;
using Direct2dCad.IO.FileFormat.Layers;
using Direct2dCad.IO.FileFormat.Sections;
using Direct2dCad.IO.FileFormat.Styles;

namespace Direct2dCad.IO;

internal static class CadDocumentMapper
{
    internal static CadDocumentSection ToDocumentSection(CadDocument document)
    {
        return new CadDocumentSection
        {
            Id = document.Id.Value,
            Name = document.Name
        };
    }

    internal static CadSettingsSection ToSettingsSection(CadDocument document)
    {
        return new CadSettingsSection
        {
            Unit = document.DocumentSettings.Unit,
            LengthPrecision = document.DocumentSettings.LengthPrecision,
            AnglePrecision = document.DocumentSettings.AnglePrecision,
            BackgroundColor = ToData(document.ViewSettings.BackgroundColor),
            GridType = document.ViewSettings.Grid.Type,
            GridSpacingX = document.ViewSettings.Grid.SpacingX,
            GridSpacingY = document.ViewSettings.Grid.SpacingY,
            GridSubdivision = document.ViewSettings.Grid.Subdivision,
            GridSnapSpacingX = document.ViewSettings.Grid.SnapSpacingX,
            GridSnapSpacingY = document.ViewSettings.Grid.SnapSpacingY,
            GridMinimumScreenSpacing = document.ViewSettings.Grid.MinimumScreenSpacing,
            GridMinimumWorldSpacing = document.ViewSettings.Grid.MinimumWorldSpacing,
            GridMinorLineColor = ToData(document.ViewSettings.Grid.MinorLineColor),
            GridMajorLineColor = ToData(document.ViewSettings.Grid.MajorLineColor),
            GridMinorLineWidth = document.ViewSettings.Grid.MinorLineWidth,
            GridMajorLineWidth = document.ViewSettings.Grid.MajorLineWidth,
            GridSnapMarkerColor = ToData(document.ViewSettings.Grid.SnapMarkerColor),
            GridSnapMarkerLength = document.ViewSettings.Grid.SnapMarkerLength,
            GridSnapMarkerStrokeWidth = document.ViewSettings.Grid.SnapMarkerStrokeWidth,
            GridSnapMarkerType = document.ViewSettings.Grid.SnapMarkerType,
            OriginPosition = ToData(document.ViewSettings.Origin.Position),
            OriginColor = ToData(document.ViewSettings.Origin.Color),
            OriginStrokeWidth = document.ViewSettings.Origin.StrokeWidth,
            OriginDisplayType = document.ViewSettings.Origin.DisplayType,
            OriginMarkerType = document.ViewSettings.Origin.MarkerType,
            OriginLinePattern = document.ViewSettings.Origin.LinePattern,
            OriginSize = document.ViewSettings.Origin.Size,
            DefaultLayerDrawingPriority = document.DocumentSettings.LayerDrawingPriority.DefaultPriority,
            LayerDrawingPriorities = document.DocumentSettings.LayerDrawingPriority.Priorities
                .Select(x => new CadLayerDrawingPriorityData
                {
                    LayerId = x.Key.Value,
                    Priority = x.Value
                })
                .ToList()
        };
    }

    internal static CadLayerSection ToLayerSection(CadDocument document)
    {
        return new CadLayerSection
        {
            Layers = document.Layers.Values
                .Select(x => new CadLayerData
                {
                    Id = x.Id.Value,
                    Name = x.Name,
                    IsVisible = x.IsVisible,
                    IsLocked = x.IsLocked,
                    IsFrozen = x.IsFrozen,
                    Color = ToData(x.Color),
                    LineWeight = x.LineWeight.Value,
                    DefaultGraphicStyleId = x.DefaultGraphicStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadStylesSection ToStylesSection(CadDocument document)
    {
        return new CadStylesSection
        {
            Styles = document.Styles.Values.Select(ToData).ToList(),
            HatchPatterns = document.HatchPatterns.Values.Select(ToData).ToList()
        };
    }

    internal static CadLinesSection ToLinesSection(CadDocument document)
    {
        return new CadLinesSection
        {
            Lines = document.Entities.Values
                .OfType<CadLine>()
                .Select(x => new CadLineData
                {
                    Entity = ToEntityData(x),
                    Start = ToData(x.Start),
                    End = ToData(x.End),
                    GraphicStyleId = x.GraphicStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadCirclesSection ToCirclesSection(CadDocument document)
    {
        return new CadCirclesSection
        {
            Circles = document.Entities.Values
                .OfType<CadCircle>()
                .Select(x => new CadCircleData
                {
                    Entity = ToEntityData(x),
                    Center = ToData(x.Center),
                    Radius = x.Radius,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    FillStyleId = x.FillStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadEllipsesSection ToEllipsesSection(CadDocument document)
    {
        return new CadEllipsesSection
        {
            Ellipses = document.Entities.Values
                .OfType<CadEllipse>()
                .Select(x => new CadEllipseData
                {
                    Entity = ToEntityData(x),
                    Center = ToData(x.Center),
                    RadiusX = x.RadiusX,
                    RadiusY = x.RadiusY,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    FillStyleId = x.FillStyleId?.Value
                })
                .ToList(),
            EllipseArcs = document.Entities.Values
                .OfType<CadEllipseArc>()
                .Select(x => new CadEllipseArcData
                {
                    Entity = ToEntityData(x),
                    Center = ToData(x.Center),
                    RadiusX = x.RadiusX,
                    RadiusY = x.RadiusY,
                    StartAngleRadians = x.StartAngleRadians,
                    SweepAngleRadians = x.SweepAngleRadians,
                    GraphicStyleId = x.GraphicStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadArcsSection ToArcsSection(CadDocument document)
    {
        return new CadArcsSection
        {
            Arcs = document.Entities.Values
                .OfType<CadArc>()
                .Select(x => new CadArcData
                {
                    Entity = ToEntityData(x),
                    Center = ToData(x.Center),
                    Radius = x.Radius,
                    StartAngleRadians = x.StartAngleRadians,
                    SweepAngleRadians = x.SweepAngleRadians,
                    GraphicStyleId = x.GraphicStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadRectanglesSection ToRectanglesSection(CadDocument document)
    {
        return new CadRectanglesSection
        {
            Rectangles = document.Entities.Values
                .OfType<CadRectangle>()
                .Select(x => new CadRectangleData
                {
                    Entity = ToEntityData(x),
                    Min = ToData(new CadPointD(x.Bounds.MinX, x.Bounds.MinY)),
                    Max = ToData(new CadPointD(x.Bounds.MaxX, x.Bounds.MaxY)),
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    FillStyleId = x.FillStyleId?.Value,
                    CornerRadiusX = x.CornerRadiusX,
                    CornerRadiusY = x.CornerRadiusY
                })
                .ToList()
        };
    }

    internal static CadPolylinesSection ToPolylinesSection(CadDocument document)
    {
        return new CadPolylinesSection
        {
            Polylines = document.Entities.Values
                .OfType<CadPolyline>()
                .Select(x => new CadPolylineData
                {
                    Entity = ToEntityData(x),
                    Points = x.Points.Select(ToData).ToList(),
                    Closed = x.Closed,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    FillStyleId = x.FillStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadSplinesSection ToSplinesSection(CadDocument document)
    {
        return new CadSplinesSection
        {
            Splines = document.Entities.Values
                .OfType<CadSpline>()
                .Select(x => new CadSplineData
                {
                    Entity = ToEntityData(x),
                    FitPoints = x.FitPoints.Select(ToData).ToList(),
                    Closed = x.Closed,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    FillStyleId = x.FillStyleId?.Value
                })
                .ToList()
        };
    }

    internal static CadTextsSection ToTextsSection(CadDocument document)
    {
        return new CadTextsSection
        {
            Texts = document.Entities.Values
                .OfType<CadText>()
                .Select(x => new CadTextData
                {
                    Entity = ToEntityData(x),
                    Text = x.Text,
                    Position = ToData(x.Position),
                    Height = x.Height,
                    RotationRadians = x.RotationRadians,
                    TextStyleId = x.TextStyleId?.Value,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    IsInverted = x.IsInverted,
                    InvertedMarginFactor = x.InvertedMarginFactor
                })
                .ToList()
        };
    }

    internal static CadShapeTextsSection ToShapeTextsSection(CadDocument document)
    {
        return new CadShapeTextsSection
        {
            ShapeTexts = document.Entities.Values
                .OfType<CadShapeText>()
                .Select(x => new CadShapeTextData
                {
                    Entity = ToEntityData(x),
                    Text = x.Text,
                    Position = ToData(x.Position),
                    Height = x.Height,
                    RotationRadians = x.RotationRadians,
                    WidthFactor = x.WidthFactor,
                    CharacterSpacingFactor = x.CharacterSpacingFactor,
                    ObliqueAngleRadians = x.ObliqueAngleRadians,
                    GraphicStyleId = x.GraphicStyleId?.Value,
                    IsInverted = x.IsInverted,
                    InvertedMarginFactor = x.InvertedMarginFactor,
                    ShapeFontId = x.ShapeFontId.Value
                })
                .ToList()
        };
    }

    internal static CadDocument FromSections(
        CadDocumentSection documentInfo,
        CadSettingsSection settings,
        CadLayerSection layers,
        CadStylesSection styles,
        CadLinesSection lines,
        CadCirclesSection circles,
        CadEllipsesSection ellipses,
        CadArcsSection arcs,
        CadRectanglesSection rectangles,
        CadPolylinesSection polylines,
        CadSplinesSection splines,
        CadTextsSection texts,
        CadShapeTextsSection shapeTexts)
    {
        var document = new CadDocument(
            new DocumentId(documentInfo.Id),
            documentInfo.Name,
            new CadIdGenerator());

        ApplySettings(document, settings);
        ApplyStyles(document, styles);
        ApplyLayers(document, layers);
        ApplyEntities(document, lines, circles, ellipses, arcs, rectangles, polylines, splines, texts, shapeTexts);

        return document;
    }

    private static void ApplySettings(CadDocument document, CadSettingsSection settings)
    {
        document.DocumentSettings.SetUnit(settings.Unit);
        document.DocumentSettings.SetLengthPrecision(settings.LengthPrecision);
        document.DocumentSettings.SetAnglePrecision(settings.AnglePrecision);
        document.ViewSettings.BackgroundColor = FromData(settings.BackgroundColor);
        document.ViewSettings.Grid.Type = settings.GridType;
        document.ViewSettings.Grid.SpacingX = settings.GridSpacingX;
        document.ViewSettings.Grid.SpacingY = settings.GridSpacingY;
        document.ViewSettings.Grid.Subdivision = settings.GridSubdivision;
        document.ViewSettings.Grid.SnapSpacingX = settings.GridSnapSpacingX;
        document.ViewSettings.Grid.SnapSpacingY = settings.GridSnapSpacingY;
        document.ViewSettings.Grid.MinimumScreenSpacing = settings.GridMinimumScreenSpacing > 0
            ? settings.GridMinimumScreenSpacing
            : document.ViewSettings.Grid.MinimumScreenSpacing;
        document.ViewSettings.Grid.MinimumWorldSpacing = settings.GridMinimumWorldSpacing is > 0
            ? settings.GridMinimumWorldSpacing.Value
            : document.ViewSettings.Grid.MinimumWorldSpacing;
        document.ViewSettings.Grid.MinorLineColor = settings.GridMinorLineColor.HasValue
            ? FromData(settings.GridMinorLineColor.Value)
            : document.ViewSettings.Grid.MinorLineColor;
        document.ViewSettings.Grid.MajorLineColor = settings.GridMajorLineColor.HasValue
            ? FromData(settings.GridMajorLineColor.Value)
            : document.ViewSettings.Grid.MajorLineColor;
        document.ViewSettings.Grid.MinorLineWidth = settings.GridMinorLineWidth is > 0
            ? settings.GridMinorLineWidth.Value
            : document.ViewSettings.Grid.MinorLineWidth;
        document.ViewSettings.Grid.MajorLineWidth = settings.GridMajorLineWidth is > 0
            ? settings.GridMajorLineWidth.Value
            : document.ViewSettings.Grid.MajorLineWidth;
        document.ViewSettings.Grid.SnapMarkerColor = settings.GridSnapMarkerColor.HasValue
            ? FromData(settings.GridSnapMarkerColor.Value)
            : document.ViewSettings.Grid.SnapMarkerColor;
        document.ViewSettings.Grid.SnapMarkerLength = settings.GridSnapMarkerLength is > 0
            ? settings.GridSnapMarkerLength.Value
            : document.ViewSettings.Grid.SnapMarkerLength;
        document.ViewSettings.Grid.SnapMarkerStrokeWidth = settings.GridSnapMarkerStrokeWidth is > 0
            ? settings.GridSnapMarkerStrokeWidth.Value
            : document.ViewSettings.Grid.SnapMarkerStrokeWidth;
        document.ViewSettings.Grid.SnapMarkerType = settings.GridSnapMarkerType
            ?? document.ViewSettings.Grid.SnapMarkerType;
        document.ViewSettings.Origin.Position = settings.OriginPosition.HasValue
            ? FromData(settings.OriginPosition.Value)
            : document.ViewSettings.Origin.Position;
        document.ViewSettings.Origin.Color = settings.OriginColor.HasValue
            ? FromData(settings.OriginColor.Value)
            : document.ViewSettings.Origin.Color;
        document.ViewSettings.Origin.StrokeWidth = settings.OriginStrokeWidth is > 0
            ? settings.OriginStrokeWidth.Value
            : document.ViewSettings.Origin.StrokeWidth;
        document.ViewSettings.Origin.DisplayType = settings.OriginDisplayType
            ?? document.ViewSettings.Origin.DisplayType;
        document.ViewSettings.Origin.MarkerType = settings.OriginMarkerType
            ?? document.ViewSettings.Origin.MarkerType;
        document.ViewSettings.Origin.LinePattern = settings.OriginLinePattern
            ?? document.ViewSettings.Origin.LinePattern;
        document.ViewSettings.Origin.Size = settings.OriginSize is > 0
            ? settings.OriginSize.Value
            : document.ViewSettings.Origin.Size;
        document.DocumentSettings.LayerDrawingPriority.SetDefaultPriority(settings.DefaultLayerDrawingPriority);
        document.DocumentSettings.LayerDrawingPriority.Clear();

        foreach (var priority in settings.LayerDrawingPriorities)
            document.DocumentSettings.LayerDrawingPriority.SetPriority(
                new LayerId(priority.LayerId),
                priority.Priority);
    }

    private static void ApplyLayers(CadDocument document, CadLayerSection section)
    {
        foreach (var layerData in section.Layers)
        {
            var layerId = new LayerId(layerData.Id);
            var layer = layerId.Equals(LayerId.Default)
                ? document.GetLayer(LayerId.Default)
                : new CadLayer(
                    layerId,
                    layerData.Name,
                    FromData(layerData.Color),
                    new CadLineWeight(layerData.LineWeight),
                    ToStyleId(layerData.DefaultGraphicStyleId));

            if (layerId.Equals(LayerId.Default))
            {
                document.RenameLayer(layerId, layerData.Name);
                layer.SetColor(FromData(layerData.Color));
                layer.SetLineWeight(new CadLineWeight(layerData.LineWeight));
                document.SetLayerDefaultGraphicStyle(layerId, ToStyleId(layerData.DefaultGraphicStyleId));
            }
            else
            {
                document.AddLayerCore(layer);
            }

            layer.SetVisible(layerData.IsVisible);
            layer.SetLocked(layerData.IsLocked);
            layer.SetFrozen(layerData.IsFrozen);
        }
    }

    private static void ApplyStyles(CadDocument document, CadStylesSection section)
    {
        foreach (var patternData in section.HatchPatterns ?? [])
        {
            var pattern = FromData(patternData);
            if (!document.HatchPatterns.ContainsKey(pattern.Id))
                document.AddHatchPatternCore(pattern);
        }

        foreach (var styleData in section.Styles)
        {
            var style = FromData(styleData);
            if (style.Id.Equals(StyleId.DefaultGraphic) &&
                document.TryGetStyle(StyleId.DefaultGraphic, out var existing) &&
                existing is CadGraphicStyle existingGraphic &&
                style is CadGraphicStyle graphic)
            {
                existingGraphic.Rename(style.Name);
                existingGraphic.SetStrokeColor(graphic.StrokeColor);
                existingGraphic.SetLineWeight(graphic.LineWeight);
                existingGraphic.SetLineType(graphic.LineTypeId);
                continue;
            }

            if (!document.Styles.ContainsKey(style.Id))
                document.AddStyleCore(style);
        }
    }

    private static void ApplyEntities(
        CadDocument document,
        CadLinesSection lines,
        CadCirclesSection circles,
        CadEllipsesSection ellipses,
        CadArcsSection arcs,
        CadRectanglesSection rectangles,
        CadPolylinesSection polylines,
        CadSplinesSection splines,
        CadTextsSection texts,
        CadShapeTextsSection shapeTexts)
    {
        foreach (var lineData in lines.Lines)
        {
            var line = new CadLine(
                new EntityId(lineData.Entity.Id),
                new LayerId(lineData.Entity.LayerId),
                new BlockId(lineData.Entity.OwnerBlockId),
                FromData(lineData.Start),
                FromData(lineData.End),
                lineData.Entity.Name);
            line.SetGraphicStyleInternal(ToStyleId(lineData.GraphicStyleId));
            ApplyEntityState(document, line, lineData.Entity);
            document.AddEntityCore(line);
        }

        foreach (var circleData in circles.Circles)
        {
            var circle = new CadCircle(
                new EntityId(circleData.Entity.Id),
                new LayerId(circleData.Entity.LayerId),
                new BlockId(circleData.Entity.OwnerBlockId),
                FromData(circleData.Center),
                circleData.Radius,
                circleData.Entity.Name);
            circle.SetGraphicStyleInternal(ToStyleId(circleData.GraphicStyleId));
            circle.SetFillStyleInternal(ToStyleId(circleData.FillStyleId));
            ApplyEntityState(document, circle, circleData.Entity);
            document.AddEntityCore(circle);
        }

        foreach (var ellipseData in ellipses.Ellipses)
        {
            var ellipse = new CadEllipse(
                new EntityId(ellipseData.Entity.Id),
                new LayerId(ellipseData.Entity.LayerId),
                new BlockId(ellipseData.Entity.OwnerBlockId),
                FromData(ellipseData.Center),
                ellipseData.RadiusX,
                ellipseData.RadiusY,
                ellipseData.Entity.Name);
            ellipse.SetGraphicStyleInternal(ToStyleId(ellipseData.GraphicStyleId));
            ellipse.SetFillStyleInternal(ToStyleId(ellipseData.FillStyleId));
            ApplyEntityState(document, ellipse, ellipseData.Entity);
            document.AddEntityCore(ellipse);
        }

        foreach (var ellipseArcData in ellipses.EllipseArcs ?? [])
        {
            var ellipseArc = new CadEllipseArc(
                new EntityId(ellipseArcData.Entity.Id),
                new LayerId(ellipseArcData.Entity.LayerId),
                new BlockId(ellipseArcData.Entity.OwnerBlockId),
                FromData(ellipseArcData.Center),
                ellipseArcData.RadiusX,
                ellipseArcData.RadiusY,
                ellipseArcData.StartAngleRadians,
                ellipseArcData.SweepAngleRadians,
                ellipseArcData.Entity.Name);
            ellipseArc.SetGraphicStyleInternal(ToStyleId(ellipseArcData.GraphicStyleId));
            ApplyEntityState(document, ellipseArc, ellipseArcData.Entity);
            document.AddEntityCore(ellipseArc);
        }

        foreach (var arcData in arcs.Arcs)
        {
            var arc = new CadArc(
                new EntityId(arcData.Entity.Id),
                new LayerId(arcData.Entity.LayerId),
                new BlockId(arcData.Entity.OwnerBlockId),
                FromData(arcData.Center),
                arcData.Radius,
                arcData.StartAngleRadians,
                arcData.SweepAngleRadians,
                arcData.Entity.Name);
            arc.SetGraphicStyleInternal(ToStyleId(arcData.GraphicStyleId));
            ApplyEntityState(document, arc, arcData.Entity);
            document.AddEntityCore(arc);
        }

        foreach (var rectangleData in rectangles.Rectangles)
        {
            var rectangle = new CadRectangle(
                new EntityId(rectangleData.Entity.Id),
                new LayerId(rectangleData.Entity.LayerId),
                new BlockId(rectangleData.Entity.OwnerBlockId),
                CadRectD.FromLTRB(
                    rectangleData.Min.X,
                    rectangleData.Min.Y,
                    rectangleData.Max.X,
                    rectangleData.Max.Y),
                rectangleData.CornerRadiusX,
                rectangleData.CornerRadiusY,
                rectangleData.Entity.Name);
            rectangle.SetGraphicStyleInternal(ToStyleId(rectangleData.GraphicStyleId));
            rectangle.SetFillStyleInternal(ToStyleId(rectangleData.FillStyleId));
            ApplyEntityState(document, rectangle, rectangleData.Entity);
            document.AddEntityCore(rectangle);
        }

        foreach (var polylineData in polylines.Polylines)
        {
            var polyline = new CadPolyline(
                new EntityId(polylineData.Entity.Id),
                new LayerId(polylineData.Entity.LayerId),
                new BlockId(polylineData.Entity.OwnerBlockId),
                polylineData.Points.Select(FromData),
                polylineData.Closed,
                polylineData.Entity.Name);
            polyline.SetGraphicStyleInternal(ToStyleId(polylineData.GraphicStyleId));
            polyline.SetFillStyleInternal(ToStyleId(polylineData.FillStyleId));
            ApplyEntityState(document, polyline, polylineData.Entity);
            document.AddEntityCore(polyline);
        }

        foreach (var splineData in splines.Splines)
        {
            var spline = new CadSpline(
                new EntityId(splineData.Entity.Id),
                new LayerId(splineData.Entity.LayerId),
                new BlockId(splineData.Entity.OwnerBlockId),
                splineData.FitPoints.Select(FromData),
                splineData.Closed,
                splineData.Entity.Name);
            spline.SetGraphicStyleInternal(ToStyleId(splineData.GraphicStyleId));
            spline.SetFillStyleInternal(ToStyleId(splineData.FillStyleId));
            ApplyEntityState(document, spline, splineData.Entity);
            document.AddEntityCore(spline);
        }

        foreach (var textData in texts.Texts)
        {
            var text = new CadText(
                new EntityId(textData.Entity.Id),
                new LayerId(textData.Entity.LayerId),
                new BlockId(textData.Entity.OwnerBlockId),
                textData.Text,
                FromData(textData.Position),
                textData.Height,
                textData.RotationRadians,
                ToStyleId(textData.TextStyleId),
                textData.Entity.Name,
                textData.IsInverted,
                textData.InvertedMarginFactor ?? CadText.DefaultInvertedMarginFactor);
            text.SetGraphicStyleInternal(ToStyleId(textData.GraphicStyleId));
            ApplyEntityState(document, text, textData.Entity);
            document.AddEntityCore(text);
        }

        foreach (var textData in shapeTexts.ShapeTexts)
        {
            var text = new CadShapeText(
                new EntityId(textData.Entity.Id),
                new LayerId(textData.Entity.LayerId),
                new BlockId(textData.Entity.OwnerBlockId),
                textData.Text,
                FromData(textData.Position),
                textData.Height,
                textData.RotationRadians,
                textData.WidthFactor,
                textData.CharacterSpacingFactor,
                textData.ObliqueAngleRadians,
                textData.Entity.Name,
                textData.IsInverted,
                textData.InvertedMarginFactor ?? CadShapeText.DefaultInvertedMarginFactor,
                CadShapeFontRegistry.FromStoredValue(textData.ShapeFontId));
            text.SetGraphicStyleInternal(ToStyleId(textData.GraphicStyleId));
            ApplyEntityState(document, text, textData.Entity);
            document.AddEntityCore(text);
        }
    }

    private static CadStyleData ToData(CadStyle style)
    {
        return style switch
        {
            CadGraphicStyle graphic => new CadStyleData
            {
                Id = graphic.Id.Value,
                Name = graphic.Name,
                Kind = graphic.Kind,
                Graphic = new CadGraphicStyleData
                {
                    StrokeColor = ToData(graphic.StrokeColor),
                    LineWeight = graphic.LineWeight.Value,
                    LineTypeId = graphic.LineTypeId.Value
                }
            },
            CadTextStyle text => new CadStyleData
            {
                Id = text.Id.Value,
                Name = text.Name,
                Kind = text.Kind,
                Text = new CadTextStyleData
                {
                    FontFamily = text.FontFamily,
                    TextHeight = text.TextHeight,
                    WidthFactor = text.WidthFactor,
                    ObliqueAngle = text.ObliqueAngle,
                    IsBold = text.IsBold,
                    IsItalic = text.IsItalic
                }
            },
            CadGradientFillStyle gradient => new CadStyleData
            {
                Id = gradient.Id.Value,
                Name = gradient.Name,
                Kind = gradient.Kind,
                GradientFill = new CadGradientFillStyleData
                {
                    GradientKind = gradient.GradientKind,
                    Stops = gradient.Stops.Select(ToData).ToList(),
                    GradientAngle = gradient.GradientAngle,
                    GradientScale = gradient.GradientScale,
                    GradientOrigin = ToData(gradient.GradientOrigin),
                    IsCentered = gradient.IsCentered
                }
            },
            CadHatchFillStyle hatch => new CadStyleData
            {
                Id = hatch.Id.Value,
                Name = hatch.Name,
                Kind = hatch.Kind,
                HatchFill = new CadHatchFillStyleData
                {
                    PatternId = hatch.PatternId.Value,
                    ForegroundColor = ToData(hatch.ForegroundColor),
                    ReservedBackgroundColor = null,
                    HatchScale = hatch.HatchScale,
                    HatchAngle = hatch.HatchAngle,
                    HatchOrigin = ToData(hatch.HatchOrigin),
                    IsAnnotative = hatch.IsAnnotative
                }
            },
            _ => throw new NotSupportedException($"Unsupported style type: {style.GetType().Name}")
        };
    }

    private static CadStyle FromData(CadStyleData data)
    {
        return data.Kind switch
        {
            CadStyleKind.Graphic when data.Graphic is not null => new CadGraphicStyle(
                new StyleId(data.Id),
                data.Name,
                FromData(data.Graphic.StrokeColor),
                new CadLineWeight(data.Graphic.LineWeight),
                new LineTypeId(data.Graphic.LineTypeId)),

            CadStyleKind.Text when data.Text is not null => new CadTextStyle(
                new StyleId(data.Id),
                data.Name,
                data.Text.FontFamily,
                data.Text.TextHeight,
                data.Text.WidthFactor,
                data.Text.ObliqueAngle,
                data.Text.IsBold,
                data.Text.IsItalic),

            CadStyleKind.Fill when data.GradientFill is not null => new CadGradientFillStyle(
                new StyleId(data.Id),
                data.Name,
                data.GradientFill.GradientKind,
                data.GradientFill.Stops.Select(FromData),
                data.GradientFill.GradientAngle,
                data.GradientFill.GradientScale,
                FromData(data.GradientFill.GradientOrigin),
                data.GradientFill.IsCentered),

            CadStyleKind.Fill when data.HatchFill is not null => new CadHatchFillStyle(
                new StyleId(data.Id),
                data.Name,
                new HatchPatternId(data.HatchFill.PatternId),
                FromData(data.HatchFill.ForegroundColor),
                data.HatchFill.HatchScale,
                data.HatchFill.HatchAngle,
                FromData(data.HatchFill.HatchOrigin),
                data.HatchFill.IsAnnotative),

            _ => throw new InvalidDataException($"Invalid style data: {data.Id}")
        };
    }

    private static CadHatchPatternData ToData(CadHatchPatternDefinition pattern)
    {
        return new CadHatchPatternData
        {
            Id = pattern.Id.Value,
            Name = pattern.Name,
            Description = pattern.Description,
            Lines = pattern.Lines.Select(ToData).ToList()
        };
    }

    private static CadHatchPatternDefinition FromData(CadHatchPatternData data)
    {
        return new CadHatchPatternDefinition(
            new HatchPatternId(data.Id),
            data.Name,
            data.Lines.Select(FromData),
            data.Description);
    }

    private static CadHatchLineData ToData(CadHatchLineDefinition line)
    {
        return new CadHatchLineData
        {
            Angle = line.Angle,
            Origin = ToData(line.Origin),
            Offset = ToData(line.Offset),
            DashPattern = line.DashPattern.ToList()
        };
    }

    private static CadHatchLineDefinition FromData(CadHatchLineData data)
    {
        return new CadHatchLineDefinition(
            data.Angle,
            FromData(data.Origin),
            ToVector(data.Offset),
            data.DashPattern);
    }

    private static CadEntityData ToEntityData(CadEntity entity)
    {
        return new CadEntityData
        {
            Id = entity.Id.Value,
            Name = entity.Name,
            LayerId = entity.LayerId.Value,
            OwnerBlockId = entity.OwnerBlockId.Value,
            IsLocked = entity.IsLocked,
            IsErased = entity.IsErased,
            IsVisible = entity.IsVisible,
            LineWeight = entity.LineWeight?.Value,
            ZIndex = entity.ZIndex,
            UseLayerColor = entity.UseLayerColor,
            UseLayerLineWeight = entity.UseLayerLineWeight,
            StrokeStartCap = (int)entity.StrokeStyle.StartCap,
            StrokeEndCap = (int)entity.StrokeStyle.EndCap,
            StrokeDashCap = (int)entity.StrokeStyle.DashCap,
            StrokeDashStyle = (int)entity.StrokeStyle.DashStyle,
            StrokeLineJoin = (int)entity.StrokeStyle.LineJoin
        };
    }

    private static void ApplyEntityState(CadDocument document, CadEntity entity, CadEntityData data)
    {
        entity.SetLocked(data.IsLocked);
        entity.SetVisible(data.IsVisible);
        var storedLineWeight = ToStoredLineWeight(data.LineWeight);
        var hasStoredByLayerLineWeight = data.LineWeight is { } rawLineWeight && rawLineWeight < 0;
        var hasGraphicLineWeight = HasExplicitGraphicLineWeight(document, GetGraphicStyleId(entity));
        var useLayerLineWeight = data.UseLayerLineWeight ??
                                 (hasStoredByLayerLineWeight ||
                                  storedLineWeight is null && !hasGraphicLineWeight);

        entity.SetLineWeightState(storedLineWeight, useLayerLineWeight);
        entity.SetUseLayerColor(data.UseLayerColor ?? (GetGraphicStyleId(entity) is null));
        entity.SetStrokeStyle(ToStrokeStyle(data));
        entity.SetZIndex(data.ZIndex);

        if (data.IsErased)
            entity.Erase();
        else
            entity.Restore();
    }

    private static CadStrokeStyle ToStrokeStyle(CadEntityData data)
    {
        var defaults = CadStrokeStyle.Default;
        return new CadStrokeStyle(
            ToEnum(data.StrokeStartCap, defaults.StartCap),
            ToEnum(data.StrokeEndCap, defaults.EndCap),
            ToEnum(data.StrokeDashCap, defaults.DashCap),
            ToEnum(data.StrokeDashStyle, defaults.DashStyle),
            ToEnum(data.StrokeLineJoin, defaults.LineJoin));
    }

    private static TEnum ToEnum<TEnum>(int? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return value is { } raw && Enum.IsDefined(typeof(TEnum), raw)
            ? (TEnum)Enum.ToObject(typeof(TEnum), raw)
            : fallback;
    }

    private static CadLineWeight? ToStoredLineWeight(double? value)
    {
        if (value is null)
            return null;

        var lineWeight = new CadLineWeight(value.Value);
        return lineWeight.IsByLayer ? null : lineWeight;
    }

    private static StyleId? GetGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static bool HasExplicitGraphicLineWeight(CadDocument document, StyleId? styleId)
    {
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic &&
               graphic.LineWeight is { IsByLayer: false };
    }

    private static StyleId? ToStyleId(long? value) => value is null ? null : new StyleId(value.Value);

    private static CadPointData ToData(CadPointD point) => new(point.X, point.Y);

    private static CadPointD FromData(CadPointData point) => new(point.X, point.Y);

    private static CadPointData ToData(CadVectorD vector) => new(vector.X, vector.Y);

    private static CadVectorD ToVector(CadPointData point) => new(point.X, point.Y);

    private static CadColorData ToData(CadColor color) => new(color.A, color.R, color.G, color.B);

    private static CadColor FromData(CadColorData color) => new(color.A, color.R, color.G, color.B);

    private static CadGradientStopData ToData(CadGradientStop stop)
    {
        return new CadGradientStopData(stop.Offset, ToData(stop.Color));
    }

    private static CadGradientStop FromData(CadGradientStopData stop)
    {
        return new CadGradientStop(stop.Offset, FromData(stop.Color));
    }
}
