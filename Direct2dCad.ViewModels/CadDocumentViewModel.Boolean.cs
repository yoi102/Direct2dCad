using System.Globalization;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    private EntityId[] _booleanTargets = [];
    private EntityId? _booleanSubject;
    private IReadOnlyList<CadRegionContour>? _booleanPreview;
    private bool _committingBoolean;
    private CancellationTokenSource? _booleanCancellation;
    private BooleanRegionsCommand? _booleanCommand;
    public Task BooleanPreviewCompletion { get; private set; } = Task.CompletedTask;
    public bool IsBooleanCalculating { get; private set; }
    public bool IsBooleanTool => CadCanvasToolMode is CadCanvasToolMode.BooleanUnion or CadCanvasToolMode.BooleanIntersection or CadCanvasToolMode.BooleanDifference;
    public bool CanBooleanSelection => BooleanSelectionDisabledReason.Length == 0;
    public string BooleanSelectionDisabledReason => GetBooleanSelectionDisabledReason();

    // The command predicate and its explanation share the same validation path.
    private string GetBooleanSelectionDisabledReason()
    {
        if (IsBooleanTool)
            return CadUiText.Get("BooleanSelectionPending");
        var document = CadEditor.Document;
        if (document.IsReadOnly)
            return CadUiText.Get("BooleanSelectionReadOnly");
        var ids = CadEditor.Selection.EntityIds;
        if (ids.Count < 2)
            return CadUiText.Get("BooleanSelectionMinimum");
        if (ids.Count > CadRegionBoolean.MaximumInputEdges)
            return FormatBooleanReason("BooleanSelectionMaximum", CadRegionBoolean.MaximumInputEdges);

        foreach (var id in ids)
        {
            if (!document.TryGetEntity(id, out var entity) || entity is null || entity.IsErased)
                return CadUiText.Get("BooleanSelectionUnavailable");
            if (entity.OwnerBlockId != CadEditor.ActiveOwnerBlockId)
                return CadUiText.Get("BooleanSelectionWrongSpace");
            if (!CadEntityAccessPolicy.IsEditable(document, entity) ||
                !CadEntityAccessPolicy.CanAddToLayer(document, entity.LayerId))
            {
                if (entity.IsLocked)
                    return FormatBooleanReason("BooleanSelectionEntityLocked", BooleanEntityTypeName(entity));
                if (document.TryGetLayer(entity.LayerId, out var layer) && layer is not null)
                {
                    if (layer.IsFrozen)
                        return FormatBooleanReason("BooleanSelectionLayerFrozen", layer.Name);
                    if (layer.IsLocked)
                        return FormatBooleanReason("BooleanSelectionLayerLocked", layer.Name);
                }
                return CadUiText.Get("BooleanSelectionUnavailable");
            }
            if (!CadRegionBoolean.Supports(entity))
            {
                if (entity is CadRectangle { HasRoundedCorners: true })
                    return CadUiText.Get("BooleanSelectionRoundedRectangle");
                if (entity is CadArc { IsFullCircle: false } or CadPolyline { Closed: false } or CadCompositePath { Closed: false })
                    return FormatBooleanReason("BooleanSelectionOpenBoundary", BooleanEntityTypeName(entity));
                return FormatBooleanReason("BooleanSelectionUnsupportedType", BooleanEntityTypeName(entity));
            }
        }
        return string.Empty;
    }

    private static string BooleanEntityTypeName(CadEntity entity)
    {
        var descriptor = CadSelectionEntityTypeCatalog.All.FirstOrDefault(item => item.EntityType == entity.GetType());
        return descriptor is null ? entity.GetType().Name : CadUiText.Get(descriptor.ResourceKey);
    }

    private static string FormatBooleanReason(string key, object value) =>
        string.Format(CultureInfo.CurrentUICulture, CadUiText.Get(key), value);
    private CadBooleanOperation BooleanOperation => CadCanvasToolMode switch
    {
        CadCanvasToolMode.BooleanIntersection => CadBooleanOperation.Intersection,
        CadCanvasToolMode.BooleanDifference => CadBooleanOperation.Difference,
        _ => CadBooleanOperation.Union
    };
    private string BooleanPrompt => CadUiText.Get(CadCanvasToolMode.ToString()) + ": " + (!string.IsNullOrEmpty(StepInputError) ? StepInputError :
        CadUiText.Get(IsBooleanCalculating ? "BooleanCalculating" : BooleanOperation == CadBooleanOperation.Difference && _booleanSubject is null ? "BooleanChooseSubject" : "BooleanConfirm"));

    public async Task BeginBoolean(CadBooleanOperation operation)
    {
        if (!CanBooleanSelection || !Enum.IsDefined(operation)) return;
        var targets = CadEditor.Selection.EntityIds.OrderBy(id => id.Value).ToArray();
        SetToolMode(operation switch
        {
            CadBooleanOperation.Intersection => CadCanvasToolMode.BooleanIntersection,
            CadBooleanOperation.Difference => CadCanvasToolMode.BooleanDifference,
            _ => CadCanvasToolMode.BooleanUnion
        });
        _booleanTargets = targets;
        CadEditor.Selection.Replace(targets);
        NotifyDrawingUx(); RaiseInteractionStateChanged(); RequestOverlayRender(updateHandleScene: true);
        // Finish synchronous state publication before starting work that can complete asynchronously.
        // The continuation remains on the calling UI synchronization context.
        if (operation != CadBooleanOperation.Difference) BooleanPreviewCompletion = UpdateBooleanPreviewAsync();
        await BooleanPreviewCompletion;
    }
    private void ClearBooleanInteraction()
    {
        _booleanCancellation?.Cancel(); _booleanCancellation?.Dispose(); _booleanCancellation = null;
        _booleanTargets = []; _booleanSubject = null; _booleanPreview = null; _booleanCommand = null;
        IsBooleanCalculating = false; BooleanPreviewCompletion = Task.CompletedTask;
    }

    private bool HandleBooleanClick(CadPointD point)
    {
        if (BooleanOperation != CadBooleanOperation.Difference) return true;
        var document = CadEditor.Document; var tolerance = 8 / Math.Max(InteractionZoom, 1e-9);
        // Picking the edge disambiguates nested, overlapping subjects; unfilled interiors are also usable.
        var entities = _booleanTargets.Select(document.GetEntity).Where(e => CadEntityAccessPolicy.IsEditable(document, e)).ToArray();
        var edge = entities.OrderByDescending(e => document.DocumentSettings.LayerDrawingPriority.GetPriority(e.LayerId))
            .ThenByDescending(e => e.ZIndex).ThenByDescending(e => document.GetEntityInsertionIndex(e.Id))
            .FirstOrDefault(e => Direct2dCad.HitTesting.CadEntityHitTester.HitTestEdge(document, e, point, tolerance, CreateHitTestOptions(), out _));
        var interior = entities.Where(e => CadRegionGeometry.Contains(CadRegionBoolean.GetContours(e), point)).ToArray();
        var subject = edge ?? (interior.Length == 1 ? interior[0] : null);
        if (subject is null)
        {
            StepInputError = CadUiText.Get("BooleanChooseSubject"); NotifyDrawingUx(); return false;
        }
        _booleanSubject = subject.Id;
        NotifyDrawingUx(); RequestOverlayRender();
        BooleanPreviewCompletion = UpdateBooleanPreviewAsync(); return true;
    }
    private async Task UpdateBooleanPreviewAsync()
    {
        _booleanCancellation?.Cancel(); _booleanCancellation?.Dispose();
        using var cancellation = new CancellationTokenSource(); _booleanCancellation = cancellation;
        _booleanPreview = null; StepInputError = ""; EditErrorDetail = "";
        _booleanCommand = null; IsBooleanCalculating = true;
        NotifyDrawingUx(); RequestOverlayRender();
        try
        {
            var command = new BooleanRegionsCommand(_booleanTargets, BooleanOperation, _booleanSubject);
            var result = await command.PrepareAsync(CadEditor.Document, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_booleanCancellation, cancellation)) return;
            if (result.Count == 0) { StepInputError = CadUiText.Get("BooleanEmpty"); _snackbarService.Enqueue(StepInputError); return; }
            _booleanPreview = result; _booleanCommand = command; StepInputError = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        { if (ReferenceEquals(_booleanCancellation, cancellation)) { ReportEditError(ex); _snackbarService.Enqueue(StepInputError); } }
        finally
        {
            if (ReferenceEquals(_booleanCancellation, cancellation))
            { _booleanCancellation = null; IsBooleanCalculating = false; NotifyDrawingUx(); RequestOverlayRender(); }
        }
    }
    private bool CompleteBooleanInteraction()
    {
        if (!IsBooleanTool) return false;
        if (IsBooleanCalculating)
        {
            StepInputError = CadUiText.Get("BooleanCalculating");
            NotifyDrawingUx(); return true;
        }
        if (_booleanPreview is null || _booleanCommand is null)
        {
            if (_booleanSubject is null && BooleanOperation == CadBooleanOperation.Difference)
                StepInputError = CadUiText.Get("BooleanChooseSubject");
            else if (string.IsNullOrEmpty(StepInputError))
                StepInputError = CadUiText.Get("BooleanEmpty");
            // Consume Enter in the active tool, but report failure to DONE through DrawingInputError.
            // Preserve computation errors (including an empty intersection) for the user.
            NotifyDrawingUx(); return true;
        }
        try
        {
            StepInputError = "";
            _committingBoolean = true;
            var command = _booleanCommand;
            CadEditor.DocumentCommands.Execute(command);
            SetToolMode(CadCanvasToolMode.Select);
            CadEditor.Selection.Replace([command.ResultEntityId!.Value]);
            RaiseInteractionStateChanged(); RequestOverlayRender(updateHandleScene: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { ReportEditError(ex); }
        finally { _committingBoolean = false; NotifyDrawingUx(); }
        return true;
    }
    private void AddBooleanPreview(List<CadTransientItem> items)
    {
        if (!IsBooleanTool) return;
        if (_booleanPreview is not null)
            foreach (var edge in _booleanPreview.SelectMany(c => c.Edges))
                AddEditPrimitive(items, edge, new CadTransientStyle(new CadColor(255, 64, 220, 180), 2));
        if (_booleanSubject is { } id)
            items.Add(new CadTransientEntityReference(id, default, new CadTransientStyle(new CadColor(255, 255, 190, 70), 2)));
    }
}
