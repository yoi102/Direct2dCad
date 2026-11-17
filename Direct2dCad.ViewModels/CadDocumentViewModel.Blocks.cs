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
    public CadCanvasInteractionResult BeginBlockInsertion(
        BlockId definitionBlockId,
        double rotationRadians = 0,
        double scaleX = 1,
        double scaleY = 1)
    {
        var definition = CadEditor.Document.GetBlock(definitionBlockId);
        if (definition.IsSystem)
            throw new InvalidOperationException("System space blocks cannot be inserted.");
        if (!IsValidBlockScale(scaleX) || !IsValidBlockScale(scaleY))
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Block scale must be finite and non-zero.");

        SetToolMode(CadCanvasToolMode.InsertBlock);
        _insertBlockDefinitionId = definitionBlockId;
        _insertBlockRotationRadians = rotationRadians;
        _insertBlockScaleX = scaleX;
        _insertBlockScaleY = scaleY;
        RaiseInteractionStateChanged();
        RequestOverlayRender();
        return CadCanvasInteractionResult.HandledOnly;
    }

    public bool UpdateBlockInsertionTransform(
        double rotationDegrees,
        double scaleX,
        double scaleY)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.InsertBlock ||
            _insertBlockDefinitionId is null)
        {
            return false;
        }

        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        if (!IsValidBlockScale(scaleX) || !IsValidBlockScale(scaleY))
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Block scale must be finite and non-zero.");

        var rotationRadians = rotationDegrees * Math.PI / 180.0;
        if (_insertBlockRotationRadians.Equals(rotationRadians) &&
            _insertBlockScaleX.Equals(scaleX) &&
            _insertBlockScaleY.Equals(scaleY))
        {
            return true;
        }

        _insertBlockRotationRadians = rotationRadians;
        _insertBlockScaleX = scaleX;
        _insertBlockScaleY = scaleY;
        RaiseInteractionStateChanged();
        RequestOverlayRender();
        return true;
    }

    private static bool IsValidBlockScale(double value) =>
        double.IsFinite(value) && Math.Abs(value) > 1e-9;

    public void EditBlockDefinition(BlockId blockId)
    {
        var block = CadEditor.Document.GetBlock(blockId);
        if (block.IsSystem || block.IsReadOnly)
            throw new InvalidOperationException($"Block is read-only: {block.Name}");

        ActivateModelSpace();
        CadEditor.ActiveOwnerBlockId = blockId;
        SetEditingBlock(blockId);
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        FitToWindow();
        RaiseInteractionStateChanged();
        PublishInteractionActivity($"Edit block: {block.Name}");
    }

    [RelayCommand]
    public void ExitBlockEditing()
    {
        if (!IsEditingBlock)
            return;
        ActivateModelSpace();
        PublishInteractionActivity("Exit block editor");
    }

    private void SetEditingBlock(BlockId? blockId)
    {
        if (EditingBlockId == blockId)
            return;
        EditingBlockId = blockId;
        OnPropertyChanged(nameof(EditingBlockId));
        OnPropertyChanged(nameof(IsEditingBlock));
        OnPropertyChanged(nameof(EditingBlockName));
    }

    private void CommitBlockInsertion(CadPointD screen)
    {
        if (_insertBlockDefinitionId is not { } definitionBlockId ||
            !EnsureLayerAcceptsEntities(DrawingLayerId))
        {
            return;
        }

        var position = ScreenToWorld(screen, snapToGrid: true);
        var definition = CadEditor.Document.GetBlock(definitionBlockId);
        var entityId = CadEditor.InsertBlockReference(
            definitionBlockId,
            position,
            DrawingLayerId,
            _insertBlockRotationRadians,
            _insertBlockScaleX,
            _insertBlockScaleY,
            definition.Name);
        CadEditor.Selection.Replace([entityId]);
        SetToolMode(CadCanvasToolMode.Select);
        RaiseInteractionStateChanged();
    }

    private void AddBlockInsertionPreview(List<CadTransientItem> items, CadPointD position)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.InsertBlock ||
            _insertBlockDefinitionId is not { } definitionBlockId)
        {
            return;
        }

        items.Add(new CadTransientBlockReference(
            definitionBlockId,
            position,
            _insertBlockRotationRadians,
            _insertBlockScaleX,
            _insertBlockScaleY,
            DrawingLayerId,
            CadColorSource.ByLayer,
            GraphicStyleId: null,
            CreatePreviewStyleService().CreateSelectionWindowStyle()));
    }
}
