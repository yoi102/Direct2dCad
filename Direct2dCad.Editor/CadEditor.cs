using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Editor.History;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor;

public sealed class CadEditor
{
    private readonly CadDocumentChangeDispatcher _documentChanges;

    public CadDocument Document { get; }
    public CadViewport Viewport { get; }
    public CadSelectionSet Selection { get; }
    public ICadSpatialIndex SpatialIndex { get; }
    public DirtySet DirtySet { get; }
    public CadDocumentCommandManager DocumentCommands { get; }
    public CadEditorCommandManager EditorCommands { get; }
    public CommandHistorySettings DocumentHistorySettings => DocumentCommands.Settings;
    public CommandHistorySettings EditorHistorySettings => EditorCommands.Settings;

    public event EventHandler<CadDocumentChangeSet>? DocumentChanged;
    public event EventHandler<CadEditorCommandResult>? EditorStateChanged;

    public CadEditor(CadDocument document)
        : this(document, new CadViewport(), new CadSelectionSet(), new CadSpatialIndex())
    {
    }

    public CadEditor(
        CadDocument document,
        CadViewport viewport,
        CadSelectionSet selection,
        ICadSpatialIndex spatialIndex)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        SpatialIndex = spatialIndex ?? throw new ArgumentNullException(nameof(spatialIndex));
        DirtySet = new DirtySet();

        _documentChanges = new CadDocumentChangeDispatcher(Document, DirtySet, SpatialIndex);
        DocumentCommands = new CadDocumentCommandManager(
            Document,
            _documentChanges,
            new CommandHistory<ICadCommand>());
        EditorCommands = new CadEditorCommandManager(
            Document,
            Viewport,
            Selection,
            SpatialIndex,
            _documentChanges,
            new CommandHistory<ICadEditorCommand>());

        _documentChanges.DocumentChanged += (_, result) => DocumentChanged?.Invoke(this, result);
        EditorCommands.Changed += (_, result) => EditorStateChanged?.Invoke(this, result);
        RebuildSpatialIndex();
    }

    public CadDocumentChangeSet Execute(ICadCommand command) => DocumentCommands.Execute(command);

    public CadEditorCommandResult Execute(ICadEditorCommand command) => EditorCommands.Execute(command);

    public CadEditorCommandResult ExecuteRange(
        IEnumerable<ICadEditorCommand> commands,
        string name = "Command Batch")
    {
        return EditorCommands.ExecuteRange(commands, name);
    }

    public CadDocumentChangeSet ExecuteRange(
        IEnumerable<ICadCommand> commands,
        string name = "Command Batch")
    {
        return DocumentCommands.ExecuteRange(commands, name);
    }

    public CadDocumentChangeSet Undo() => DocumentCommands.Undo();

    public CadDocumentChangeSet Redo() => DocumentCommands.Redo();

    public CadDocumentChangeSet UndoDocument() => DocumentCommands.Undo();

    public CadDocumentChangeSet RedoDocument() => DocumentCommands.Redo();

    public CadEditorCommandResult UndoEditor() => EditorCommands.Undo();

    public CadEditorCommandResult RedoEditor() => EditorCommands.Redo();

    public object CreateDocumentHistorySnapshot() => DocumentCommands.CreateUndoHistorySnapshot();

    public bool DocumentHistoryEquals(object? snapshot) => DocumentCommands.UndoHistoryEquals(snapshot);

    public CadDocumentChangeSet DrainDirtyChanges() => DirtySet.Drain();

    public CadDocumentChangeSet PublishDocumentChanges(CadDocumentChangeSet changes)
    {
        _documentChanges.Publish(changes);
        return changes;
    }

    public bool TryGetEntity(EntityId entityId, out CadEntity? entity)
    {
        return Document.TryGetEntity(entityId, out entity);
    }

    public CadEntity GetEntity(EntityId entityId)
    {
        return Document.GetEntity(entityId);
    }

    public TEntity GetEntity<TEntity>(EntityId entityId)
        where TEntity : CadEntity
    {
        return Document.GetEntity(entityId) is TEntity entity
            ? entity
            : throw new InvalidOperationException($"Entity {entityId} is not {typeof(TEntity).Name}.");
    }

    public EntityId AddLine(
        CadPointD start,
        CadPointD end,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddLineCommand(
            start,
            end,
            layerId,
            graphicStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddCircle(
        CadPointD center,
        double radius,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddCircleCommand(
            center,
            radius,
            layerId,
            graphicStyleId,
            fillStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddEllipse(
        CadPointD center,
        double radiusX,
        double radiusY,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddEllipseCommand(
            center,
            radiusX,
            radiusY,
            layerId,
            graphicStyleId,
            fillStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddArc(
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddArcCommand(
            center,
            radius,
            startAngleRadians,
            sweepAngleRadians,
            layerId,
            graphicStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddRectangle(
        CadRectD bounds,
        double cornerRadiusX = 0,
        double cornerRadiusY = 0,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddRectangleCommand(
            bounds,
            cornerRadiusX,
            cornerRadiusY,
            layerId,
            graphicStyleId,
            fillStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddPolygon(
        IEnumerable<CadPointD> points,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddPolygonCommand(
            points,
            layerId,
            graphicStyleId,
            fillStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddPolyline(
        IEnumerable<CadPointD> points,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddPolylineCommand(
            points,
            closed,
            layerId,
            graphicStyleId,
            fillStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddSpline(
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddSplineCommand(
            fitPoints,
            closed,
            layerId,
            graphicStyleId,
            name,
            lineWeight,
            zIndex,
            isVisible);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddText(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? textStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadText.DefaultInvertedMarginFactor,
        CadLineWeight? lineWeight = null,
        int zIndex = 0,
        bool isVisible = true)
    {
        var command = new AddTextCommand(
            text,
            position,
            height,
            rotationRadians,
            layerId,
            graphicStyleId,
            textStyleId,
            name,
            isInverted,
            invertedMarginFactor,
            lineWeight,
            zIndex,
            isVisible);

        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddShapeText(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        double widthFactor = CadStrokeFont.DefaultWidthFactor,
        double characterSpacingFactor = CadStrokeFont.DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = CadStrokeFont.DefaultObliqueAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
    {
        var command = new AddShapeTextCommand(
            text,
            position,
            height,
            rotationRadians,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians,
            layerId,
            graphicStyleId,
            name,
            isInverted,
            invertedMarginFactor,
            shapeFontId);

        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public CadDocumentChangeSet DeleteEntity(EntityId entityId)
    {
        return DeleteEntities([entityId]);
    }

    public CadDocumentChangeSet DeleteEntities(IEnumerable<EntityId> entityIds)
    {
        return DocumentCommands.Execute(new DeleteEntitiesCommand(entityIds));
    }

    public CadDocumentChangeSet MoveEntities(IEnumerable<EntityId> entityIds, CadVectorD delta)
    {
        return DocumentCommands.Execute(new MoveEntitiesCommand(entityIds, delta));
    }

    public IReadOnlyList<EntityId> DuplicateEntities(IEnumerable<EntityId> entityIds, CadVectorD delta)
    {
        var command = new DuplicateEntitiesCommand(entityIds, delta);
        DocumentCommands.Execute(command);
        return command.CreatedEntityIds.ToArray();
    }

    public CadDocumentChangeSet SetLineGeometry(EntityId entityId, CadPointD start, CadPointD end)
    {
        return DocumentCommands.Execute(new SetLineGeometryCommand(entityId, start, end));
    }

    public CadDocumentChangeSet SetCircleGeometry(EntityId entityId, CadPointD center, double radius)
    {
        return DocumentCommands.Execute(new SetCircleGeometryCommand(entityId, center, radius));
    }

    public CadDocumentChangeSet SetEllipseGeometry(EntityId entityId, CadPointD center, double radiusX, double radiusY)
    {
        return DocumentCommands.Execute(new SetEllipseGeometryCommand(entityId, center, radiusX, radiusY));
    }

    public CadDocumentChangeSet SetArcGeometry(
        EntityId entityId,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians)
    {
        return DocumentCommands.Execute(new SetArcGeometryCommand(
            entityId,
            center,
            radius,
            startAngleRadians,
            sweepAngleRadians));
    }

    public CadDocumentChangeSet SetRectangleGeometry(EntityId entityId, CadRectD bounds)
    {
        return DocumentCommands.Execute(new SetRectangleGeometryCommand(entityId, bounds));
    }

    public CadDocumentChangeSet SetRectangleCornerRadius(EntityId entityId, double radiusX, double radiusY)
    {
        return DocumentCommands.Execute(new SetRectangleCornerRadiusCommand(entityId, radiusX, radiusY));
    }

    public CadDocumentChangeSet SetPolylineGeometry(EntityId entityId, IEnumerable<CadPointD> points, bool closed)
    {
        return DocumentCommands.Execute(new SetPolylineGeometryCommand(entityId, points, closed));
    }

    public CadDocumentChangeSet SetSplineGeometry(EntityId entityId, IEnumerable<CadPointD> fitPoints, bool closed)
    {
        return DocumentCommands.Execute(new SetSplineGeometryCommand(entityId, fitPoints, closed));
    }

    public CadDocumentChangeSet SetTextContent(EntityId entityId, string text)
    {
        return DocumentCommands.Execute(new SetTextContentCommand(entityId, text));
    }

    public CadDocumentChangeSet SetShapeTextContent(EntityId entityId, string text)
    {
        return DocumentCommands.Execute(new SetShapeTextContentCommand(entityId, text));
    }

    public CadDocumentChangeSet SetTextGeometry(
        EntityId entityId,
        CadPointD position,
        double height,
        double rotationRadians = 0)
    {
        return DocumentCommands.Execute(new SetTextGeometryCommand(entityId, position, height, rotationRadians));
    }

    public CadDocumentChangeSet SetShapeTextGeometry(
        EntityId entityId,
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians)
    {
        return DocumentCommands.Execute(new SetShapeTextGeometryCommand(
            entityId,
            position,
            height,
            rotationRadians,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians));
    }

    public CadDocumentChangeSet SetShapeTextFont(EntityId entityId, CadShapeFontId shapeFontId)
    {
        return SetShapeTextFont([entityId], shapeFontId);
    }

    public CadDocumentChangeSet SetShapeTextFont(IEnumerable<EntityId> entityIds, CadShapeFontId shapeFontId)
    {
        return DocumentCommands.Execute(new SetShapeTextFontCommand(entityIds, shapeFontId));
    }

    public CadDocumentChangeSet SetTextStyle(EntityId entityId, StyleId? textStyleId)
    {
        return DocumentCommands.Execute(new SetTextStyleCommand(entityId, textStyleId));
    }

    public CadDocumentChangeSet SetTextInverted(EntityId entityId, bool isInverted)
    {
        return SetTextInverted([entityId], isInverted);
    }

    public CadDocumentChangeSet SetTextInverted(IEnumerable<EntityId> entityIds, bool isInverted)
    {
        return DocumentCommands.Execute(new SetTextInvertedCommand(entityIds, isInverted));
    }

    public CadDocumentChangeSet SetTextInvertedMarginFactor(EntityId entityId, double marginFactor)
    {
        return SetTextInvertedMarginFactor([entityId], marginFactor);
    }

    public CadDocumentChangeSet SetTextInvertedMarginFactor(IEnumerable<EntityId> entityIds, double marginFactor)
    {
        return DocumentCommands.Execute(new SetTextInvertedMarginFactorCommand(entityIds, marginFactor));
    }

    public CadDocumentChangeSet SetEntityColor(EntityId entityId, CadColor color)
    {
        return SetEntityColor([entityId], color);
    }

    public CadDocumentChangeSet SetEntityColor(IEnumerable<EntityId> entityIds, CadColor color)
    {
        return DocumentCommands.Execute(new SetEntityColorCommand(entityIds, color));
    }

    public CadDocumentChangeSet SetEntityGraphicStyle(EntityId entityId, StyleId? graphicStyleId)
    {
        return SetEntityGraphicStyle([entityId], graphicStyleId);
    }

    public CadDocumentChangeSet SetEntityGraphicStyle(IEnumerable<EntityId> entityIds, StyleId? graphicStyleId)
    {
        return DocumentCommands.Execute(new SetEntityGraphicStyleCommand(entityIds, graphicStyleId));
    }

    public CadDocumentChangeSet SetEntityFillStyle(EntityId entityId, StyleId? fillStyleId)
    {
        return SetEntityFillStyle([entityId], fillStyleId);
    }

    public CadDocumentChangeSet SetEntityFillStyle(IEnumerable<EntityId> entityIds, StyleId? fillStyleId)
    {
        return DocumentCommands.Execute(new SetEntityFillStyleCommand(entityIds, fillStyleId));
    }

    public CadDocumentChangeSet SetEntityLineWeight(EntityId entityId, CadLineWeight? lineWeight)
    {
        return SetEntityLineWeight([entityId], lineWeight);
    }

    public CadDocumentChangeSet SetEntityLineWeight(IEnumerable<EntityId> entityIds, CadLineWeight? lineWeight)
    {
        return DocumentCommands.Execute(new SetEntityLineWeightCommand(entityIds, lineWeight));
    }

    public CadDocumentChangeSet SetEntityZIndex(EntityId entityId, int zIndex)
    {
        return SetEntityZIndex([entityId], zIndex);
    }

    public CadDocumentChangeSet SetEntityZIndex(IEnumerable<EntityId> entityIds, int zIndex)
    {
        return DocumentCommands.Execute(new SetEntityZIndexCommand(entityIds, zIndex));
    }

    public CadDocumentChangeSet SetEntityVisibility(EntityId entityId, bool isVisible)
    {
        return SetEntityVisibility([entityId], isVisible);
    }

    public CadDocumentChangeSet SetEntityVisibility(IEnumerable<EntityId> entityIds, bool isVisible)
    {
        return DocumentCommands.Execute(new SetEntityVisibilityCommand(entityIds, isVisible));
    }

    public CadDocumentChangeSet ChangeEntityLayer(EntityId entityId, LayerId layerId)
    {
        return ChangeEntitiesLayer([entityId], layerId);
    }

    public CadDocumentChangeSet ChangeEntitiesLayer(IEnumerable<EntityId> entityIds, LayerId layerId)
    {
        return DocumentCommands.Execute(new ChangeLayerCommand(entityIds, layerId));
    }

    public CadDocumentChangeSet SetOriginSettings(
        CadOriginDisplayType displayType,
        CadOriginMarkerType markerType,
        CadOriginLinePattern linePattern,
        CadColor color,
        double size,
        double strokeWidth)
    {
        return DocumentCommands.Execute(new SetOriginSettingsCommand(
            new CadOriginSettingsSnapshot(
                Document.ViewSettings.Origin.Position,
                displayType,
                markerType,
                linePattern,
                color,
                size,
                strokeWidth)));
    }

    public CadDocumentChangeSet SetOriginPosition(CadPointD position)
    {
        return DocumentCommands.Execute(new SetOriginPositionCommand(position));
    }

    public void RegisterRenderer(ICadRenderer renderer, bool rebuildExistingResources = true)
    {
        _documentChanges.RegisterRenderer(renderer, rebuildExistingResources);
    }

    public bool UnregisterRenderer(ICadRenderer renderer)
    {
        return _documentChanges.UnregisterRenderer(renderer);
    }

    public void RegisterGeometryResourceManager(
        ICadGeometryResourceManager resourceManager,
        bool rebuildExistingResources = true)
    {
        _documentChanges.RegisterGeometryResourceManager(resourceManager, rebuildExistingResources);
    }

    public bool UnregisterGeometryResourceManager(ICadGeometryResourceManager resourceManager)
    {
        return _documentChanges.UnregisterGeometryResourceManager(resourceManager);
    }

    public void RebuildSpatialIndex()
    {
        SpatialIndex.Clear();

        foreach (var entity in Document.Entities.Values)
        {
            if (!entity.IsErased && entity.IsVisible)
                SpatialIndex.Update(entity.Id, entity.Bounds);
        }
    }

    private static EntityId GetCreatedEntityId(EntityId? entityId, string commandName)
    {
        return entityId ?? throw new InvalidOperationException($"{commandName} did not create an entity.");
    }
}
