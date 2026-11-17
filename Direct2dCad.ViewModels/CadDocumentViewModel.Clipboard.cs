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
    public CadClipboardSnapshot? CopySelection()
    {
        var snapshot = _paste.Copy(CreateClipboardInteractionService());
        if (snapshot is null)
            return null;

        var blockReferenceCount = snapshot.Items.Count(item => item.Entity is CadBlockReferenceClipboardSnapshot);
        var blockDetails = blockReferenceCount > 0
            ? $", {blockReferenceCount} block references, {snapshot.BlockDefinitions.Count} block definitions"
            : string.Empty;
        PublishInteractionActivity(
            $"Copy selection ({snapshot.Items.Count} entities{blockDetails})");
        return snapshot;
    }

    public CadCanvasInteractionResult BeginPastePreview()
    {
        if (!_paste.BeginPreview(CreateClipboardInteractionService()))
            return CadCanvasInteractionResult.NotHandled;

        return BeginPastePreviewCore();
    }

    public CadCanvasInteractionResult BeginClipboardPastePreview()
    {
        if (_paste.HasUserCopySnapshot)
            return BeginPastePreview();

        CadOleImportData? oleObject;
        try
        {
            oleObject = _oleSessions.LoadFromClipboard();
        }
        catch
        {
            oleObject = null;
        }

        if (oleObject is not null)
        {
            _paste.SetSnapshot(CreateOleObjectClipboardSnapshot(oleObject));
            return BeginPastePreviewCore();
        }

        CadImageImportData? image;
        try
        {
            image = _imageImportService.LoadFromClipboard();
        }
        catch
        {
            image = null;
        }

        if (image is not null)
        {
            _paste.SetSnapshot(CreateImageClipboardSnapshot(image));
            return BeginPastePreviewCore();
        }

        string? text;
        try
        {
            text = _clipboardTextService.LoadFromClipboard();
        }
        catch
        {
            text = null;
        }

        if (text is not null)
        {
            DrawingDefaults.Text = text;
            return SetToolMode(CadCanvasToolMode.Text);
        }

        return BeginPastePreview();
    }

    private void CommitPaste(CadPointD screen)
        => CommitPasteWorldPoint(ScreenToWorld(screen, snapToGrid: true));

    private bool CommitPasteWorldPoint(CadPointD target)
    {
        if (!EnsureLayerAcceptsEntities(PasteTargetLayerId))
            return false;

        var createdIds = _paste.Commit(
            CreateClipboardInteractionService(),
            target,
            PasteTargetLayerId);
        if (createdIds.Count > 0)
        {
            CadEditor.Selection.Replace(createdIds.Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                CanSelectEntity(entity)));
            RaiseInteractionStateChanged();
        }

        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));
        RequestOverlayRender(updateHandleScene: true);
        return createdIds.Count > 0;
    }

    private CadCanvasInteractionResult BeginPastePreviewCore()
    {
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(DrawingLayerId);
        _drawingState.Clear();
        _selectionDrag.Clear();
        OnPropertyChanged(nameof(PasteTargetLayerId));
        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));
        RaiseInteractionStateChanged();
        PublishInteractionActivity("Begin paste preview");
        RequestOverlayRender();
        return new CadCanvasInteractionResult(true, Cursor: CanvasCursor);
    }

    private void AddPastePreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (!_paste.IsPreviewActive)
            return;

        _paste.AddPreview(CreateClipboardInteractionService(), items, mouseWorld, PasteTargetLayerId);
    }

    private CadClipboardInteractionService CreateClipboardInteractionService()
    {
        return new CadClipboardInteractionService(CadEditor);
    }

    private CadClipboardSnapshot CreateImageClipboardSnapshot(CadImageImportData image)
    {
        var layer = ResolveDrawingLayer();
        var bounds = CreateClipboardImageBounds(image);
        var state = new CadEntityStateClipboardSnapshot(
            image.SourceName,
            null,
            ColorSource: CadColorSource.ByLayer,
            UseLayerLineWeight: true,
            IsVisible: true,
            IsLocked: false,
            CadStrokeStyle.Default,
            ZIndex: 0);
        var item = new CadClipboardEntityItem(
            new CadImageClipboardSnapshot(
                state,
                bounds,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                image.Pixels,
                image.ContentType,
                image.SourceName),
            new CadLayerClipboardSnapshot(
                layer.Name,
                layer.Color,
                layer.LineWeight,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen),
            GraphicStyle: null,
            FillStyle: null,
            TextStyle: null);

        return new CadClipboardSnapshot([item], bounds.Center, bounds);
    }

    private CadClipboardSnapshot CreateOleObjectClipboardSnapshot(CadOleImportData oleObject)
    {
        var layer = ResolveDrawingLayer();
        var bounds = CreateClipboardOleObjectBounds(oleObject);
        var state = new CadEntityStateClipboardSnapshot(
            oleObject.SourceName,
            null,
            ColorSource: CadColorSource.ByLayer,
            UseLayerLineWeight: true,
            IsVisible: true,
            IsLocked: false,
            CadStrokeStyle.Default,
            ZIndex: 0);
        var item = new CadClipboardEntityItem(
            new CadOleObjectClipboardSnapshot(
                state,
                bounds,
                oleObject.OleBytes,
                oleObject.ContentType,
                oleObject.SourceName,
                Guid.NewGuid()),
            new CadLayerClipboardSnapshot(
                layer.Name,
                layer.Color,
                layer.LineWeight,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen),
            GraphicStyle: null,
            FillStyle: null,
            TextStyle: null);

        return new CadClipboardSnapshot([item], bounds.Center, bounds);
    }

    private CadRectD CreateClipboardImageBounds(CadImageImportData image)
    {
        var maxPixelSide = Math.Max(Math.Max(image.PixelWidth, image.PixelHeight), 1);
        var visible = InteractionViewport.VisibleWorldBounds;
        var maxWorldSide = visible.IsEmpty
            ? Math.Max(maxPixelSide, 1)
            : Math.Max(Math.Min(visible.Width, visible.Height) * 0.35, 1.0);
        var scale = maxWorldSide / maxPixelSide;
        var width = Math.Max(image.PixelWidth * scale, 1.0);
        var height = Math.Max(image.PixelHeight * scale, 1.0);

        return CadRectD.FromCenter(CadPointD.Origin, width, height);
    }

    private CadRectD CreateClipboardOleObjectBounds(CadOleImportData oleObject)
    {
        var aspectRatio = oleObject.NaturalAspectRatio > 0 &&
                          !double.IsNaN(oleObject.NaturalAspectRatio) &&
                          !double.IsInfinity(oleObject.NaturalAspectRatio)
            ? oleObject.NaturalAspectRatio
            : 4.0 / 3.0;
        var visible = InteractionViewport.VisibleWorldBounds;
        var maxWorldSide = visible.IsEmpty
            ? 100.0
            : Math.Max(Math.Min(visible.Width, visible.Height) * 0.35, 1.0);
        var width = aspectRatio >= 1.0 ? maxWorldSide : maxWorldSide * aspectRatio;
        var height = aspectRatio >= 1.0 ? maxWorldSide / aspectRatio : maxWorldSide;

        return CadRectD.FromCenter(CadPointD.Origin, width, height);
    }
}
