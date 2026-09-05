using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class EntityPropertiesToolboxViewModel : CadToolboxViewModelBase, IDisposable
{

    private readonly IDisposable _interactionStateChangedSubscription;
    private readonly IDisposable _blockDefinitionSelectionChangedSubscription;
    private readonly ISystemFontCatalog _systemFontCatalog;
    private readonly ISnackbarService _snackbarService;
    private BlockId? _selectedBlockDefinitionId;
    private (Editor.CadEditor Editor, long DocumentVersion, long SelectionVersion,
        BlockId Owner, BlockId? Definition, CadCanvasToolMode Mode)? _lastSelectionRefresh;

    public EntityPropertiesToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionStateChangedSubscriber,
        ISubscriber<CadBlockDefinitionSelectionChangedMessage> blockDefinitionSelectionChangedSubscriber,
        ISystemFontCatalog systemFontCatalog,
        ISnackbarService snackbarService)
        : base(toolboxLayoutSettingsStore, "toolbox.entity-properties", DockZone.LeftBottom, isOpenByDefault: true)
    {
        _systemFontCatalog = systemFontCatalog ?? throw new ArgumentNullException(nameof(systemFontCatalog));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        Title = Strings.Property;
        _interactionStateChangedSubscription = interactionStateChangedSubscriber.Subscribe(OnInteractionStateChanged);
        _blockDefinitionSelectionChangedSubscription = blockDefinitionSelectionChangedSubscriber.Subscribe(
            OnBlockDefinitionSelectionChanged);
        Icon = toolboxIconProvider.Git;
        Shortcut = "Ctrl+Shift+G";
        CanClose = false;

    }
    [ObservableProperty]
    public partial ObservableObject? Entity { get; set; }

    private CadDocumentViewModel? _documentViewModel;
    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (ReferenceEquals(_documentViewModel, documentViewModel))
        {
            Refresh();
            return;
        }

        _selectedBlockDefinitionId = null;
        _documentViewModel = documentViewModel;
        Entity = null;
        _lastSelectionRefresh = null;

        Refresh();
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        if (message.ClearBlockDefinitionSelection)
            _selectedBlockDefinitionId = null;
        Refresh();
    }

    private void OnBlockDefinitionSelectionChanged(CadBlockDefinitionSelectionChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        _selectedBlockDefinitionId = message.BlockId;
        Refresh();
    }

    public void Dispose()
    {
        _interactionStateChangedSubscription.Dispose();
        _blockDefinitionSelectionChangedSubscription.Dispose();
    }

    private void Refresh()
    {
        if (_documentViewModel is null)
        {
            Entity = null;
            _lastSelectionRefresh = null;
            return;
        }

        if (_documentViewModel.IsPastePreviewActive ||
            _documentViewModel.CadCanvasToolMode != CadCanvasToolMode.Select)
            _lastSelectionRefresh = null;

        if (_documentViewModel.IsPastePreviewActive)
        {
            if (Entity is TransientPastePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientPastePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.InsertBlock &&
            _documentViewModel.BlockInsertionDefinitionId is not null)
        {
            if (Entity is TransientBlockInsertionPropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientBlockInsertionPropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (IsArcDrawingMode(_documentViewModel.CadCanvasToolMode))
        {
            if (Entity is TransientArcPropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientArcPropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Line)
        {
            if (Entity is TransientLinePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientLinePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (IsCircleDrawingMode(_documentViewModel.CadCanvasToolMode))
        {
            if (Entity is TransientCirclePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientCirclePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (IsEllipseDrawingMode(_documentViewModel.CadCanvasToolMode))
        {
            if (Entity is TransientEllipsePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientEllipsePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Rectangle)
        {
            if (Entity is TransientRectanglePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientRectanglePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Polyline)
        {
            if (Entity is TransientPolylinePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientPolylinePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Polygon)
        {
            if (Entity is TransientPolygonPropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientPolygonPropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Spline)
        {
            if (Entity is TransientSplinePropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientSplinePropertyViewModel(_documentViewModel);
            }

            return;
        }

        if (_documentViewModel.CadCanvasToolMode == CadCanvasToolMode.Text)
        {
            if (Entity is TransientTextPropertyViewModel transient &&
                ReferenceEquals(transient.DocumentViewModel, _documentViewModel))
            {
                transient.RefreshFromDocument();
            }
            else
            {
                Entity = new TransientTextPropertyViewModel(_documentViewModel, _systemFontCatalog);
            }

            return;
        }

        var editor = _documentViewModel.CadEditor;
        var refreshState = (editor, editor.DocumentChangeVersion, editor.Selection.Version,
            editor.ActiveOwnerBlockId, _selectedBlockDefinitionId, _documentViewModel.CadCanvasToolMode);
        if (_lastSelectionRefresh == refreshState)
            return;
        if (_lastSelectionRefresh is { } last && ReferenceEquals(last.Editor, editor) &&
            last.SelectionVersion == editor.Selection.Version && last.Owner == editor.ActiveOwnerBlockId &&
            last.Definition == _selectedBlockDefinitionId && last.Mode == _documentViewModel.CadCanvasToolMode &&
            unchecked(last.DocumentVersion + 1) == editor.DocumentChangeVersion &&
            _selectedBlockDefinitionId is null && !RequiresSelectionRefresh(editor))
        {
            _lastSelectionRefresh = refreshState;
            return;
        }
        if (_lastSelectionRefresh is { } previous && !ReferenceEquals(previous.Editor, editor))
            Entity = null;
        CadEntityChangeKind? propertyChanges = null;
        if (_lastSelectionRefresh is { } prior && ReferenceEquals(prior.Editor, editor) &&
            prior.SelectionVersion == editor.Selection.Version && prior.Owner == editor.ActiveOwnerBlockId &&
            unchecked(prior.DocumentVersion + 1) == editor.DocumentChangeVersion &&
            !editor.LastDocumentChanges.AffectsDocumentStructure && !editor.LastDocumentChanges.AffectsViewSettings &&
            !editor.LastDocumentChanges.AffectsLayouts && !editor.LastDocumentChanges.AffectsLayoutStructure)
        {
            propertyChanges = CadEntityChangeKind.None;
            foreach (var change in editor.LastDocumentChanges.EntityChanges)
                propertyChanges |= change.Kind;
        }
        _lastSelectionRefresh = refreshState;

        var selectedEntityIds = _documentViewModel.CadEditor.Selection.EntityIds;
        var validSelectedEntityIds = selectedEntityIds
            .Where(entityId =>
                _documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var selectedEntity) &&
                selectedEntity is { IsErased: false } &&
                selectedEntity.OwnerBlockId.Equals(_documentViewModel.CadEditor.ActiveOwnerBlockId))
            .ToArray();
        if (validSelectedEntityIds.Length > 1)
        {
            if (Entity is MultiEntityPropertyViewModel multiEntityViewModel &&
                multiEntityViewModel.Matches(validSelectedEntityIds))
            {
                multiEntityViewModel.RefreshFromEntities(propertyChanges);
            }
            else
            {
                Entity = new MultiEntityPropertyViewModel(_documentViewModel, validSelectedEntityIds);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out var entity) &&
            entity is CadArc arc &&
            !arc.IsErased)
        {
            if (Entity is ArcPropertyViewModel arcViewModel &&
                arcViewModel.EntityId.Equals(arc.Id))
            {
                arcViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new ArcPropertyViewModel(_documentViewModel, arc.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadLine line &&
            !line.IsErased)
        {
            if (Entity is LinePropertyViewModel lineViewModel &&
                lineViewModel.EntityId.Equals(line.Id))
            {
                lineViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new LinePropertyViewModel(_documentViewModel, line.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadCircle circle &&
            !circle.IsErased)
        {
            if (Entity is CirclePropertyViewModel circleViewModel &&
                circleViewModel.EntityId.Equals(circle.Id))
            {
                circleViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new CirclePropertyViewModel(_documentViewModel, circle.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadEllipse ellipse &&
            !ellipse.IsErased)
        {
            if (Entity is EllipsePropertyViewModel ellipseViewModel &&
                ellipseViewModel.EntityId.Equals(ellipse.Id))
            {
                ellipseViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new EllipsePropertyViewModel(_documentViewModel, ellipse.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadRectangle rectangle &&
            !rectangle.IsErased)
        {
            if (Entity is RectanglePropertyViewModel rectangleViewModel &&
                rectangleViewModel.EntityId.Equals(rectangle.Id))
            {
                rectangleViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new RectanglePropertyViewModel(_documentViewModel, rectangle.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadPolyline polyline &&
            !polyline.IsErased)
        {
            if (Entity is PolylinePropertyViewModel polylineViewModel &&
                polylineViewModel.EntityId.Equals(polyline.Id))
            {
                polylineViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new PolylinePropertyViewModel(_documentViewModel, polyline.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadSpline spline &&
            !spline.IsErased)
        {
            if (Entity is SplinePropertyViewModel splineViewModel &&
                splineViewModel.EntityId.Equals(spline.Id))
            {
                splineViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new SplinePropertyViewModel(_documentViewModel, spline.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadText text &&
            !text.IsErased)
        {
            if (Entity is TextPropertyViewModel textViewModel &&
                textViewModel.EntityId.Equals(text.Id))
            {
                textViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new TextPropertyViewModel(_documentViewModel, text.Id, _systemFontCatalog);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadImage image &&
            !image.IsErased)
        {
            if (Entity is ImagePropertyViewModel imageViewModel &&
                imageViewModel.EntityId.Equals(image.Id))
            {
                imageViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new ImagePropertyViewModel(_documentViewModel, image.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadOleObject oleObject &&
            !oleObject.IsErased)
        {
            if (Entity is OleObjectPropertyViewModel oleObjectViewModel &&
                oleObjectViewModel.EntityId.Equals(oleObject.Id))
            {
                oleObjectViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new OleObjectPropertyViewModel(_documentViewModel, oleObject.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is CadBlockReference blockReference &&
            !blockReference.IsErased)
        {
            if (Entity is BlockReferencePropertyViewModel blockReferenceViewModel &&
                blockReferenceViewModel.EntityId.Equals(blockReference.Id))
            {
                blockReferenceViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new BlockReferencePropertyViewModel(_documentViewModel, blockReference.Id);
            }

            return;
        }

        if (validSelectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(validSelectedEntityIds[0], out entity) &&
            entity is { IsErased: false })
        {
            if (Entity is CommonEntityPropertyViewModel commonEntityViewModel &&
                commonEntityViewModel.EntityId.Equals(entity.Id))
            {
                commonEntityViewModel.RefreshFromEntity();
            }
            else
            {
                Entity = new CommonEntityPropertyViewModel(_documentViewModel, entity.Id);
            }

            return;
        }

        if (_selectedBlockDefinitionId is { } blockId &&
            _documentViewModel.CadEditor.Document.TryGetBlock(blockId, out var block) &&
            block is { IsSystem: false })
        {
            if (Entity is BlockDefinitionPropertyViewModel blockDefinitionViewModel &&
                blockDefinitionViewModel.BlockId.Equals(blockId))
            {
                blockDefinitionViewModel.RefreshFromDefinition();
            }
            else
            {
                Entity = new BlockDefinitionPropertyViewModel(
                    _documentViewModel,
                    blockId,
                    _snackbarService);
            }

            return;
        }

        Entity = null;
    }

    private static bool IsCircleDrawingMode(CadCanvasToolMode toolMode)
    {
        return toolMode is
            CadCanvasToolMode.CircleCenterRadius or
            CadCanvasToolMode.CircleCenterDiameter or
            CadCanvasToolMode.CircleTwoPoint or
            CadCanvasToolMode.CircleThreePoint;
    }

    private bool RequiresSelectionRefresh(Editor.CadEditor editor)
    {
        var changes = editor.LastDocumentChanges;
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings || changes.AffectsLayouts ||
            changes.AffectsLayoutStructure || Entity is BlockReferencePropertyViewModel)
            return true;
        foreach (var change in changes.EntityChanges)
        {
            if (editor.Selection.Contains(change.EntityId))
                return true;
            // A definition edit can also change the bounds of selected references.
            if (editor.Document.TryGetEntity(change.EntityId, out var entity) && entity is not null &&
                editor.Document.TryGetBlock(entity.OwnerBlockId, out var owner) && owner is { IsSystem: false })
                return true;
        }
        return false;
    }

    private static bool IsEllipseDrawingMode(CadCanvasToolMode toolMode)
    {
        return toolMode is
            CadCanvasToolMode.EllipseCenter or
            CadCanvasToolMode.EllipseAxisEnd or
            CadCanvasToolMode.EllipseArc;
    }

    private static bool IsArcDrawingMode(CadCanvasToolMode toolMode)
    {
        return toolMode is
            CadCanvasToolMode.ArcThreePoint or
            CadCanvasToolMode.ArcStartCenterEnd or
            CadCanvasToolMode.ArcStartCenterAngle or
            CadCanvasToolMode.ArcStartCenterLength or
            CadCanvasToolMode.ArcStartEndAngle or
            CadCanvasToolMode.ArcStartEndDirection or
            CadCanvasToolMode.ArcStartEndRadius or
            CadCanvasToolMode.ArcCenterStartEnd or
            CadCanvasToolMode.ArcCenterStartAngle or
            CadCanvasToolMode.ArcCenterStartLength or
            CadCanvasToolMode.ArcContinue;
    }
}
