using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class PasteEntitiesCommand : ICadCommand
{
    private readonly CadClipboardSnapshot _snapshot;
    private readonly CadVectorD _delta;
    private readonly List<EntityId> _createdEntityIds = [];

    public string Name => "Paste Entities";
    public IReadOnlyList<EntityId> CreatedEntityIds => _createdEntityIds;

    public PasteEntitiesCommand(CadClipboardSnapshot snapshot, CadVectorD delta)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _delta = delta;

        if (_snapshot.IsEmpty)
            throw new ArgumentException("Clipboard snapshot must contain at least one entity.", nameof(snapshot));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityIds.Count > 0)
        {
            var restoredIds = new List<EntityId>();
            foreach (var entityId in _createdEntityIds)
            {
                if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                    continue;

                entity.Restore();
                restoredIds.Add(entity.Id);
            }

            return restoredIds.Count == 0
                ? CadDocumentChangeSet.Empty
                : CadDocumentChangeSet.ForEntities(
                    restoredIds,
                    CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance | CadEntityChangeKind.Visibility);
        }

        var context = new PasteEntityContext(document);
        foreach (var item in _snapshot.Items)
        {
            if (TryCreateEntity(context, item, _delta, out var created) && created is not null)
                _createdEntityIds.Add(created.Id);
        }

        return _createdEntityIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(
                _createdEntityIds,
                CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance | CadEntityChangeKind.Fill | CadEntityChangeKind.Layer | CadEntityChangeKind.DrawOrder);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var erasedIds = new List<EntityId>();
        foreach (var entityId in _createdEntityIds)
        {
            if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                continue;

            entity.Erase();
            erasedIds.Add(entity.Id);
        }

        return erasedIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(erasedIds, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }

    private static bool TryCreateEntity(
        PasteEntityContext context,
        CadClipboardEntityItem item,
        CadVectorD delta,
        out CadEntity? created)
    {
        var document = context.Document;
        var layerId = context.ResolveLayer(item.Layer);
        var graphicStyleId = context.ResolveStyle(item.GraphicStyle);
        var fillStyleId = context.ResolveStyle(item.FillStyle);
        var textStyleId = context.ResolveStyle(item.TextStyle);

        created = item.Entity switch
        {
            CadLineClipboardSnapshot line => document.AddLine(
                line.Start + delta,
                line.End + delta,
                layerId,
                graphicStyleId,
                line.State.Name),
            CadCircleClipboardSnapshot circle => document.AddCircle(
                circle.Center + delta,
                circle.Radius,
                layerId,
                graphicStyleId,
                fillStyleId,
                circle.State.Name),
            CadEllipseClipboardSnapshot ellipse => document.AddEllipse(
                ellipse.Center + delta,
                ellipse.RadiusX,
                ellipse.RadiusY,
                layerId,
                graphicStyleId,
                fillStyleId,
                ellipse.State.Name),
            CadEllipseArcClipboardSnapshot ellipseArc => document.AddEllipseArc(
                ellipseArc.Center + delta,
                ellipseArc.RadiusX,
                ellipseArc.RadiusY,
                ellipseArc.StartAngleRadians,
                ellipseArc.SweepAngleRadians,
                layerId,
                graphicStyleId,
                ellipseArc.State.Name),
            CadArcClipboardSnapshot arc => document.AddArc(
                arc.Center + delta,
                arc.Radius,
                arc.StartAngleRadians,
                arc.SweepAngleRadians,
                layerId,
                graphicStyleId,
                arc.State.Name),
            CadRectangleClipboardSnapshot rectangle => document.AddRectangle(
                rectangle.Bounds.Translate(delta),
                rectangle.CornerRadiusX,
                rectangle.CornerRadiusY,
                layerId,
                graphicStyleId,
                fillStyleId,
                rectangle.State.Name),
            CadPolylineClipboardSnapshot polyline => document.AddPolyline(
                polyline.Points.Select(x => x + delta),
                polyline.Closed,
                layerId,
                graphicStyleId,
                fillStyleId,
                polyline.State.Name),
            CadSplineClipboardSnapshot spline => document.AddSpline(
                spline.FitPoints.Select(x => x + delta),
                spline.Closed,
                layerId,
                graphicStyleId,
                spline.State.Name),
            CadTextClipboardSnapshot text => CreateText(document, text, delta, layerId, graphicStyleId, textStyleId),
            CadShapeTextClipboardSnapshot shapeText => document.AddShapeText(
                shapeText.Text,
                shapeText.Position + delta,
                shapeText.Height,
                shapeText.RotationRadians,
                shapeText.WidthFactor,
                shapeText.CharacterSpacingFactor,
                shapeText.ObliqueAngleRadians,
                layerId,
                graphicStyleId,
                shapeText.State.Name,
                shapeText.IsInverted,
                shapeText.InvertedMarginFactor,
                shapeText.ShapeFontId),
            _ => null
        };

        if (created is null)
            return false;

        ApplyState(created, item.Entity.State);
        return true;
    }

    private static CadText CreateText(
        CadDocument document,
        CadTextClipboardSnapshot snapshot,
        CadVectorD delta,
        LayerId layerId,
        StyleId? graphicStyleId,
        StyleId? textStyleId)
    {
        var text = document.AddText(
            snapshot.Text,
            snapshot.Position + delta,
            snapshot.Height,
            snapshot.RotationRadians,
            layerId,
            graphicStyleId,
            textStyleId,
            snapshot.State.Name,
            snapshot.IsInverted,
            snapshot.InvertedMarginFactor);

        if (!snapshot.RequiresBoundsMeasurement)
            text.SetLocalBounds(snapshot.LocalBounds);

        return text;
    }

    private static void ApplyState(CadEntity entity, CadEntityStateClipboardSnapshot state)
    {
        entity.SetLineWeightState(state.LineWeight, state.UseLayerLineWeight);
        entity.SetUseLayerColor(state.UseLayerColor);
        entity.SetVisible(state.IsVisible);
        entity.SetLocked(state.IsLocked);
        entity.SetZIndex(state.ZIndex);
    }

    private sealed class PasteEntityContext(CadDocument document)
    {
        private readonly Dictionary<CadLayerClipboardSnapshot, LayerId> _layers = [];
        private readonly Dictionary<CadStyleClipboardSnapshot, StyleId?> _styles = [];
        private readonly Dictionary<CadHatchPatternClipboardSnapshot, HatchPatternId> _hatchPatterns = [];

        public CadDocument Document { get; } = document;

        public LayerId ResolveLayer(CadLayerClipboardSnapshot snapshot)
        {
            if (_layers.TryGetValue(snapshot, out var cached))
                return cached;

            var existing = Document.Layers.Values.FirstOrDefault(x =>
                string.Equals(x.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _layers[snapshot] = existing.Id;
                return existing.Id;
            }

            var layerId = Document.CreateLayer(
                CreateUniqueLayerName(Document, snapshot.Name),
                snapshot.Color,
                snapshot.LineWeight);
            var layer = Document.GetLayer(layerId);
            layer.SetVisible(snapshot.IsVisible);
            layer.SetLocked(snapshot.IsLocked);
            layer.SetFrozen(snapshot.IsFrozen);
            _layers[snapshot] = layerId;
            return layerId;
        }

        public StyleId? ResolveStyle(CadStyleClipboardSnapshot? snapshot)
        {
            if (snapshot is null)
                return null;

            if (_styles.TryGetValue(snapshot, out var cached))
                return cached;

            var styleId = snapshot switch
            {
                CadGraphicStyleClipboardSnapshot graphic => ResolveGraphicStyle(graphic),
                CadTextStyleClipboardSnapshot text => ResolveTextStyle(text),
                CadGradientFillStyleClipboardSnapshot gradient => ResolveGradientFillStyle(gradient),
                CadHatchFillStyleClipboardSnapshot hatch => ResolveHatchFillStyle(hatch),
                _ => null
            };

            _styles[snapshot] = styleId;
            return styleId;
        }

        private StyleId? ResolveGraphicStyle(CadGraphicStyleClipboardSnapshot snapshot)
        {
            var existing = Document.Styles.Values
                .OfType<CadGraphicStyle>()
                .FirstOrDefault(x =>
                    x.StrokeColor == snapshot.StrokeColor &&
                    x.LineWeight == snapshot.LineWeight &&
                    x.LineTypeId == snapshot.LineTypeId);

            return existing?.Id ??
                   Document.CreateGraphicStyle(
                       CreateUniqueStyleName(Document, snapshot.Name),
                       snapshot.StrokeColor,
                       snapshot.LineWeight,
                       snapshot.LineTypeId);
        }

        private StyleId? ResolveTextStyle(CadTextStyleClipboardSnapshot snapshot)
        {
            var existing = Document.Styles.Values
                .OfType<CadTextStyle>()
                .FirstOrDefault(x =>
                    string.Equals(x.FontFamily, snapshot.FontFamily, StringComparison.OrdinalIgnoreCase) &&
                    x.TextHeight.Equals(snapshot.TextHeight) &&
                    x.WidthFactor.Equals(snapshot.WidthFactor) &&
                    x.ObliqueAngle.Equals(snapshot.ObliqueAngle) &&
                    x.IsBold == snapshot.IsBold &&
                    x.IsItalic == snapshot.IsItalic);

            return existing?.Id ??
                   Document.CreateTextStyle(
                       CreateUniqueStyleName(Document, snapshot.Name),
                       snapshot.FontFamily,
                       snapshot.TextHeight,
                       snapshot.WidthFactor,
                       snapshot.ObliqueAngle,
                       snapshot.IsBold,
                       snapshot.IsItalic);
        }

        private StyleId? ResolveGradientFillStyle(CadGradientFillStyleClipboardSnapshot snapshot)
        {
            var existing = Document.Styles.Values
                .OfType<CadGradientFillStyle>()
                .FirstOrDefault(x =>
                    x.GradientKind == snapshot.GradientKind &&
                    x.Stops.SequenceEqual(snapshot.Stops) &&
                    x.GradientAngle.Equals(snapshot.GradientAngle) &&
                    x.GradientScale.Equals(snapshot.GradientScale) &&
                    x.GradientOrigin == snapshot.GradientOrigin &&
                    x.IsCentered == snapshot.IsCentered);

            return existing?.Id ??
                   Document.CreateGradientFillStyle(
                       CreateUniqueStyleName(Document, snapshot.Name),
                       snapshot.GradientKind,
                       snapshot.Stops,
                       snapshot.GradientAngle,
                       snapshot.GradientScale,
                       snapshot.GradientOrigin,
                       snapshot.IsCentered);
        }

        private StyleId? ResolveHatchFillStyle(CadHatchFillStyleClipboardSnapshot snapshot)
        {
            var patternId = ResolveHatchPattern(snapshot.Pattern);
            var existing = Document.Styles.Values
                .OfType<CadHatchFillStyle>()
                .FirstOrDefault(x =>
                    x.PatternId == patternId &&
                    x.ForegroundColor == snapshot.ForegroundColor &&
                    x.HatchScale.Equals(snapshot.HatchScale) &&
                    x.HatchAngle.Equals(snapshot.HatchAngle) &&
                    x.HatchOrigin == snapshot.HatchOrigin &&
                    x.IsAnnotative == snapshot.IsAnnotative);

            return existing?.Id ??
                   Document.CreateHatchFillStyle(
                       CreateUniqueStyleName(Document, snapshot.Name),
                       patternId,
                       snapshot.ForegroundColor,
                       snapshot.HatchScale,
                       snapshot.HatchAngle,
                       snapshot.HatchOrigin,
                       snapshot.IsAnnotative);
        }

        private HatchPatternId ResolveHatchPattern(CadHatchPatternClipboardSnapshot snapshot)
        {
            if (_hatchPatterns.TryGetValue(snapshot, out var cached))
                return cached;

            var existing = Document.HatchPatterns.Values.FirstOrDefault(x =>
                string.Equals(x.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase) &&
                x.Lines.SequenceEqual(snapshot.Lines));
            if (existing is not null)
            {
                _hatchPatterns[snapshot] = existing.Id;
                return existing.Id;
            }

            var patternId = Document.CreateHatchPattern(
                CreateUniqueHatchPatternName(Document, snapshot.Name),
                snapshot.Lines,
                snapshot.Description);
            _hatchPatterns[snapshot] = patternId;
            return patternId;
        }
    }

    private static string CreateUniqueLayerName(CadDocument document, string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Layer" : baseName.Trim();
        return CreateUniqueName(baseName, name => document.Layers.Values.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreateUniqueStyleName(CadDocument document, string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Pasted Style" : baseName.Trim();
        return CreateUniqueName(baseName, name => document.Styles.Values.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreateUniqueHatchPatternName(CadDocument document, string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Pasted Hatch" : baseName.Trim();
        return CreateUniqueName(baseName, name => document.HatchPatterns.Values.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreateUniqueName(string baseName, Func<string, bool> exists)
    {
        if (!exists(baseName))
            return baseName;

        for (var index = 2; ; index++)
        {
            var name = $"{baseName} {index}";
            if (!exists(name))
                return name;
        }
    }
}
