using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
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

    public CadDocumentChangeSet DrainDirtyChanges() => DirtySet.Drain();

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
        string name = "")
    {
        var command = new AddLineCommand(start, end, layerId, graphicStyleId, name);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddCircle(
        CadPointD center,
        double radius,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var command = new AddCircleCommand(center, radius, layerId, graphicStyleId, fillStyleId, name);
        DocumentCommands.Execute(command);
        return GetCreatedEntityId(command.CreatedEntityId, command.Name);
    }

    public EntityId AddRectangle(
        CadRectD bounds,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var command = new AddRectangleCommand(bounds, layerId, graphicStyleId, fillStyleId, name);
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
        string name = "")
    {
        var command = new AddTextCommand(
            text,
            position,
            height,
            rotationRadians,
            layerId,
            graphicStyleId,
            textStyleId,
            name);

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

    public CadDocumentChangeSet SetRectangleGeometry(EntityId entityId, CadRectD bounds)
    {
        return DocumentCommands.Execute(new SetRectangleGeometryCommand(entityId, bounds));
    }

    public CadDocumentChangeSet SetTextContent(EntityId entityId, string text)
    {
        return DocumentCommands.Execute(new SetTextContentCommand(entityId, text));
    }

    public CadDocumentChangeSet SetTextGeometry(
        EntityId entityId,
        CadPointD position,
        double height,
        double rotationRadians = 0)
    {
        return DocumentCommands.Execute(new SetTextGeometryCommand(entityId, position, height, rotationRadians));
    }

    public CadDocumentChangeSet SetTextStyle(EntityId entityId, StyleId? textStyleId)
    {
        return DocumentCommands.Execute(new SetTextStyleCommand(entityId, textStyleId));
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
