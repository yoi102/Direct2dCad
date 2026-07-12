using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class EntityPropertiesToolboxViewModel : ObservableToolboxBase, IDisposable
{

    private readonly IDisposable _interactionStateChangedSubscription;
    private readonly ISystemFontCatalog _systemFontCatalog;

    public EntityPropertiesToolboxViewModel(
        IToolboxIconProvider toolboxIconProvider,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionStateChangedSubscriber,
        ISystemFontCatalog systemFontCatalog)
    {
        _systemFontCatalog = systemFontCatalog ?? throw new ArgumentNullException(nameof(systemFontCatalog));
        Title = Strings.Property;
        _interactionStateChangedSubscription = interactionStateChangedSubscriber.Subscribe(OnInteractionStateChanged);
        Zone = DockZone.LeftBottom;
        Icon = toolboxIconProvider.Git;
        Shortcut = "Ctrl+Shift+G";
        IsOpenByDefault = true;
        ContentId = Id = Guid.NewGuid().ToString();
        CanClose = false;
        
    }
    [ObservableProperty]
    public partial string ContentId { get; private set; }
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

        _documentViewModel = documentViewModel;

        Refresh();
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        Refresh();
    }

    public void Dispose()
    {
        _interactionStateChangedSubscription.Dispose();
    }

    private void Refresh()
    {
        if (_documentViewModel is null)
        {
            Entity = null;
            return;
        }

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

        var selectedEntityIds = _documentViewModel.CadEditor.Selection.EntityIds.ToArray();
        var validSelectedEntityIds = selectedEntityIds
            .Where(entityId =>
                _documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var selectedEntity) &&
                selectedEntity is { IsErased: false })
            .ToArray();
        if (validSelectedEntityIds.Length > 1)
        {
            if (Entity is MultiEntityPropertyViewModel multiEntityViewModel &&
                multiEntityViewModel.Matches(validSelectedEntityIds))
            {
                multiEntityViewModel.RefreshFromEntities();
            }
            else
            {
                Entity = new MultiEntityPropertyViewModel(_documentViewModel, validSelectedEntityIds);
            }

            return;
        }

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out var entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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

        if (selectedEntityIds.Length == 1 &&
            _documentViewModel.CadEditor.Document.TryGetEntity(selectedEntityIds[0], out entity) &&
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
