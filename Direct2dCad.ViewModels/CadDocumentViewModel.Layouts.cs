using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    public void ActivateModelSpace()
    {
        SetEditingBlock(null);
        _fitToWindowPending = false;
        if (ActiveLayoutId is { } previousLayoutId)
            _spaceViewportState.Capture(CadEditor.Viewport, previousLayoutId);

        _layoutViewportCreation.Clear();
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
            CadCanvasToolMode = CadCanvasToolMode.Select;
        ActiveLayoutViewportId = null;
        ActiveLayoutId = null;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        OnPropertyChanged(nameof(IsModelSpaceActive));
        RaiseActiveSpacePropertiesChanged();
        ClearInteractionState(clearClipboard: false, render: false);
        _spaceViewportState.TryRestore(CadEditor.Viewport, layoutId: null);
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
    }

    public void ActivateLayout(LayoutId layoutId)
    {
        SetEditingBlock(null);
        _fitToWindowPending = false;
        if (ActiveLayoutId is { } previousLayoutId)
            _spaceViewportState.Capture(CadEditor.Viewport, previousLayoutId);
        else
            _spaceViewportState.Capture(CadEditor.Viewport, layoutId: null);

        _layoutViewportCreation.Clear();
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
            CadCanvasToolMode = CadCanvasToolMode.Select;
        var layout = CadEditor.Document.GetLayout(layoutId);
        ActiveLayoutViewportId = null;
        ActiveLayoutId = layout.Id;
        CadEditor.ActiveOwnerBlockId = layout.PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        OnPropertyChanged(nameof(IsModelSpaceActive));
        RaiseActiveSpacePropertiesChanged();
        ClearInteractionState(clearClipboard: false, render: false);
        if (!_spaceViewportState.TryRestore(CadEditor.Viewport, layout.Id))
            FitToWindow();
        else
        {
            RefreshPointerWorldStatus();
            RequestRender();
        }
        RaiseInteractionStateChanged();
    }

    public void ActivateLayoutViewport(LayoutViewportId viewportId)
    {
        if (ActiveLayoutId is not { } layoutId)
            throw new InvalidOperationException("A paper layout must be active before activating its viewport.");

        var viewport = CadEditor.Document.GetLayout(layoutId).GetViewport(viewportId);
        if (!viewport.IsVisible)
            throw new InvalidOperationException("A hidden layout viewport cannot be activated.");

        _preferredLayoutViewports[layoutId] = viewport.Id;
        ActiveLayoutViewportId = viewport.Id;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseActiveSpacePropertiesChanged();
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
        PublishInteractionActivity($"Activate layout viewport {viewport.Id.Value}");
    }

    public void ExitLayoutViewport()
    {
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is null)
            return;

        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = CadEditor.Document.GetLayout(layoutId).PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseActiveSpacePropertiesChanged();
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
        PublishInteractionActivity("Return to paper space");
    }

    public void SetPreferredLayoutViewport(LayoutViewportId viewportId)
    {
        if (ActiveLayoutId is not { } layoutId)
            return;

        var layout = CadEditor.Document.GetLayout(layoutId);
        if (layout.Viewports.Any(item => item.Id == viewportId))
            _preferredLayoutViewports[layoutId] = viewportId;
    }

    public LayoutViewportId? GetPreferredLayoutViewportId(LayoutId layoutId)
    {
        var layout = CadEditor.Document.GetLayout(layoutId);
        return ResolvePreferredLayoutViewport(layout)?.Id;
    }

    private CadLayoutViewport? ResolvePreferredLayoutViewport(CadLayout layout)
    {
        if (_preferredLayoutViewports.TryGetValue(layout.Id, out var preferredId))
        {
            var preferred = layout.Viewports.FirstOrDefault(item => item.Id == preferredId && item.IsVisible);
            if (preferred is not null)
                return preferred;
        }

        return layout.Viewports.FirstOrDefault(item => item.IsVisible);
    }

    [RelayCommand]
    public void BeginLayoutViewportCreation()
    {
        if (ActiveLayoutId is null || !CanEditDocument)
            return;

        if (IsLayoutViewportActive)
            ExitLayoutViewport();
        SetToolMode(CadCanvasToolMode.LayoutViewport);
        _layoutViewportCreation.Begin();
        PublishInteractionActivity("MVIEW: specify the first corner");
        RequestRender();
    }

    private void HandleLayoutViewportCreationClick(CadPointD screen)
        => HandleLayoutViewportCreationPoint(SnapWorld(CadEditor.Viewport.ScreenToWorld(screen)), explicitInput: false);

    private bool HandleLayoutViewportCreationPoint(CadPointD raw, bool explicitInput)
    {
        if (!_layoutViewportCreation.IsActive ||
            ActiveLayoutId is not { } layoutId ||
            IsLayoutViewportActive)
        {
            StepInputError = "MVIEW requires an active paper layout and a pending corner step.";
            return false;
        }

        var layout = CadEditor.Document.GetLayout(layoutId);
        if (explicitInput && !layout.PaperBounds.Contains(raw))
        {
            StepInputError = "The viewport corner must lie within the paper bounds.";
            return false;
        }
        var point = new CadPointD(
            Math.Clamp(raw.X, layout.PaperBounds.Left, layout.PaperBounds.Right),
            Math.Clamp(raw.Y, layout.PaperBounds.Bottom, layout.PaperBounds.Top));
        if (_layoutViewportCreation.FirstCorner is null)
        {
            _layoutViewportCreation.SetFirstCorner(point);
            _lastCommandLineInputPoint = new(point.X, point.Y);
            PublishInteractionActivity("MVIEW: specify the opposite corner");
            RequestOverlayRender();
            return true;
        }

        var bounds = _layoutViewportCreation.CreateBounds(point);
        var minimumPaperSize = explicitInput ? 1e-9 : 8.0 / Math.Max(CadEditor.Viewport.Zoom, 1e-6);
        if (bounds.Width < minimumPaperSize || bounds.Height < minimumPaperSize)
        {
            StepInputError = explicitInput ? "The viewport must have nonzero width and height."
                : "The viewport must be at least eight screen pixels across.";
            return false;
        }

        ResolveModelFit(bounds, out var modelCenter, out var scale);
        var viewportId = CadEditor.AddLayoutViewport(
            layoutId,
            bounds,
            modelCenter,
            scale,
            rotationRadians: 0,
            batchId: _layoutViewportCreation.BatchId);
        _layoutViewportCreation.BeginAdjusting(viewportId);
        ActivateLayoutViewport(viewportId);
        _lastCommandLineInputPoint = new(point.X, point.Y);
        PublishInteractionActivity("MVIEW: use the wheel and right-drag to adjust; click to finish");
        return true;
    }

    private void CompleteLayoutViewportCreation()
    {
        if (!_layoutViewportCreation.IsActive)
            return;

        _layoutViewportCreation.Clear();
        if (IsLayoutViewportActive)
            ExitLayoutViewport();
        SetToolMode(CadCanvasToolMode.Select);
        PublishInteractionActivity("MVIEW complete");
    }

    public bool CanActivateDoubleClickObjectOrSpace => CadCanvasToolMode == CadCanvasToolMode.Select &&
        !IsGripEditing && !IsPastePreviewActive && !IsPanning && !_selectionDrag.IsDragging &&
        !_drawingState.HasPendingPoints;

    private void AddLayoutViewportCreationPreview(
        List<CadTransientItem> items,
        CadPointD mouseScreen)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.LayoutViewport ||
            !_layoutViewportCreation.IsDefiningBounds ||
            ActiveLayoutId is not { } layoutId)
        {
            return;
        }

        var layout = CadEditor.Document.GetLayout(layoutId);
        var raw = SnapWorld(CadEditor.Viewport.ScreenToWorld(mouseScreen));
        var point = new CadPointD(
            Math.Clamp(raw.X, layout.PaperBounds.Left, layout.PaperBounds.Right),
            Math.Clamp(raw.Y, layout.PaperBounds.Bottom, layout.PaperBounds.Top));
        var bounds = _layoutViewportCreation.CreateBounds(point);
        if (!bounds.IsEmpty)
            items.Add(new CadTransientRectangle(
                bounds,
                CreatePreviewStyleService().CreateSelectionWindowStyle()));
    }

    private bool TryGetActiveLayoutViewport(
        out CadLayout layout,
        out CadLayoutViewport viewport)
    {
        layout = default!;
        viewport = default!;
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is not { } viewportId ||
            !CadEditor.Document.TryGetLayout(layoutId, out var resolvedLayout) || resolvedLayout is null)
        {
            return false;
        }

        var resolvedViewport = resolvedLayout.Viewports.FirstOrDefault(item => item.Id == viewportId);
        if (resolvedViewport is null)
            return false;

        layout = resolvedLayout;
        viewport = resolvedViewport;
        return true;
    }

    private void RaiseActiveSpacePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsLayoutViewportActive));
        OnPropertyChanged(nameof(IsPaperSpaceActive));
        OnPropertyChanged(nameof(ActiveLayoutSpaceMode));
    }

    private void EnsureActiveLayoutViewportStillExists()
    {
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is not { } viewportId)
            return;

        if (!CadEditor.Document.TryGetLayout(layoutId, out var layout) || layout is null)
        {
            ActiveLayoutViewportId = null;
            ActiveLayoutId = null;
            CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
            CadEditor.Selection.Clear();
            OnPropertyChanged(nameof(IsModelSpaceActive));
            RaiseActiveSpacePropertiesChanged();
            return;
        }

        if (layout.Viewports.Any(item => item.Id == viewportId && item.IsVisible))
            return;

        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = layout.PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        RaiseActiveSpacePropertiesChanged();
    }
}
