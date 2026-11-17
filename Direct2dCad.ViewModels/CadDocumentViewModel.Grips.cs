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
    private bool TryBeginGripDrag(CadPointD screen)
    {
        _overlayScenes.UpdateHandleScene(
            CadEditor,
            activeHandleItems: null,
            CreateHandleSceneBuildOptions(),
            InteractionZoom);

        if (!_gripDrag.TryBegin(
                CadEditor,
                _overlayScenes.HandleScene,
                _worldToScreen,
                _screenToSnappedWorld,
                screen))
            return false;

        _selectionDrag.Clear();
        _paste.Clear(clearClipboard: false);
        _dynamicInputStep = null;
        _dynamicInputPointer = _gripDrag.ActiveDrag!.DraggedGripPosition;
        NotifyDrawingUx();
        OnPropertyChanged(nameof(IsGripEditing));
        RequestOverlayRender(updateHandleScene: true);
        return true;
    }

    private void AddGripDragPreview(List<CadTransientItem> items)
    {
        if (!_gripDrag.IsActive)
            return;

        CreateGripDragPreviewBuilder().AddPreview(items, _gripDrag.ActiveDrag);
    }

    private CadGripDragPreviewBuilder CreateGripDragPreviewBuilder()
    {
        return new CadGripDragPreviewBuilder(CadEditor, CreatePreviewStyleService(), CreateTextMeasurementService());
    }

    private void CommitGripDrag(CadPointD screen, bool preserveNumericPoint = false)
    {
        if (!preserveNumericPoint)
        {
            UpdateGripDynamicInputPointer(_screenToSnappedWorld(screen));
            if (!TryGetDynamicInputPoint(out _)) { DynamicInputError=CadUiText.Get("InvalidDynamicInput"); return; }
        }
        var numericPoint = _gripDrag.ActiveDrag!.CurrentPointerWorld;
        if (!_gripDrag.Commit(
                CadEditor,
                CreateGripDragCommitter(),
                _ => numericPoint,
                screen))
        {
            RequestOverlayRender(updateHandleScene: true);
        }
        _dynamicInputStep = null;
        NotifyDrawingUx();
        OnPropertyChanged(nameof(IsGripEditing));
    }

    private CadCanvasInteractionResult CommitActiveGripDrag(CadPointD screen)
    {
        CommitGripDrag(screen);
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: !_gripDrag.IsActive,
            Cursor: CanvasCursor);
    }

    private CadCanvasInteractionResult KeepActiveGripDragAfterRelease(CadPointD screen)
    {
        UpdateGripDynamicInputPointer(_screenToSnappedWorld(screen));
        RequestOverlayRender(updateHandleScene: true);
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Hand);
    }

    private CadGripDragCommitter CreateGripDragCommitter()
    {
        return new CadGripDragCommitter(CadEditor, CreateTextMeasurementService());
    }
}
