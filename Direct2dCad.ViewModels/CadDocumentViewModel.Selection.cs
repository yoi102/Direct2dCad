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
    public void SelectEntities(IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var resolvedEntityIds = entityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is { IsErased: false } &&
                CanSelectEntity(entity))
            .Distinct()
            .ToArray();

        CadEditor.Selection.Replace(resolvedEntityIds);
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestOverlayRender(updateHandleScene: true);
    }

    public bool PanToEntity(EntityId entityId)
    {
        if (!CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased ||
            entity.Bounds.IsEmpty ||
            !TryActivateEntityOwnerSpace(entity.OwnerBlockId))
        {
            return false;
        }

        var viewport = CadEditor.Viewport;
        if (viewport.ViewWidth <= 1 || viewport.ViewHeight <= 1)
            return false;

        var entityScreenCenter = viewport.WorldToScreen(entity.Bounds.Center);
        var viewportCenter = new CadPointD(viewport.ViewWidth * 0.5, viewport.ViewHeight * 0.5);
        var screenDelta = viewportCenter - entityScreenCenter;
        if (Math.Abs(screenDelta.X) > 0.5 || Math.Abs(screenDelta.Y) > 0.5)
            CadEditor.Execute(new PanViewportCommand(screenDelta));

        RefreshPointerWorldStatus();
        RequestRender();
        return true;
    }

    private bool TryActivateEntityOwnerSpace(BlockId ownerBlockId)
    {
        if (ownerBlockId.Equals(BlockId.ModelSpace))
        {
            if (ActiveLayoutId is not null)
                ActivateModelSpace();
            return true;
        }

        var layout = CadEditor.Document.Layouts.Values
            .FirstOrDefault(item => item.PaperSpaceBlockId.Equals(ownerBlockId));
        if (layout is null)
            return ownerBlockId.Equals(CadEditor.ActiveOwnerBlockId);

        if (!ActiveLayoutId.Equals(layout.Id) || ActiveLayoutViewportId is not null)
            ActivateLayout(layout.Id);
        return true;
    }

    public CadCanvasInteractionResult SelectAllEntities()
    {
        var entityIds = GetSelectableEntityIds();
        if (!CadEditor.Selection.EntityIds.ToHashSet().SetEquals(entityIds))
            CadEditor.Execute(new SetSelectionCommand(entityIds, "Select All"));

        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestOverlayRender(updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    private EntityId[] GetSelectableEntityIds()
    {
        return CadEditor.Document.Entities.Values
            .Where(entity => !entity.IsErased && CanSelectEntity(entity))
            .Select(entity => entity.Id)
            .ToArray();
    }

    public bool IsEntityTypeSelectionEnabled(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return !_disabledSelectionEntityTypes.Contains(entityType);
    }

    public void SetEntityTypeSelectionEnabled(Type entityType, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (!typeof(CadEntity).IsAssignableFrom(entityType))
            throw new ArgumentException("Type must derive from CadEntity.", nameof(entityType));

        var changed = isEnabled
            ? _disabledSelectionEntityTypes.Remove(entityType)
            : _disabledSelectionEntityTypes.Add(entityType);
        if (!changed)
            return;

        if (PruneSelectionToFilter())
        {
            RaiseInteractionStateChanged();
            RequestOverlayRender(updateHandleScene: true);
        }

        _selectionFilterChangedPublisher.Publish(new CadSelectionFilterChangedMessage(this));
    }

    public IReadOnlyCollection<string> GetDisabledSelectionEntityTypeKeys()
    {
        return _disabledSelectionEntityTypes
            .Select(CadSelectionEntityTypeCatalog.GetKey)
            .Where(key => key is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public void ApplyDisabledSelectionEntityTypeKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var disabledTypes = keys
            .Select(CadSelectionEntityTypeCatalog.GetEntityType)
            .Where(entityType => entityType is not null)
            .Cast<Type>()
            .ToHashSet();
        if (_disabledSelectionEntityTypes.SetEquals(disabledTypes))
            return;

        _disabledSelectionEntityTypes.Clear();
        _disabledSelectionEntityTypes.UnionWith(disabledTypes);

        if (PruneSelectionToFilter())
        {
            RaiseInteractionStateChanged();
            RequestOverlayRender(updateHandleScene: true);
        }

        _selectionFilterChangedPublisher.Publish(new CadSelectionFilterChangedMessage(this));
    }

    private bool CanSelectEntity(CadEntity entity)
    {
        return entity.OwnerBlockId.Equals(CadEditor.ActiveOwnerBlockId) &&
               CadEntityAccessPolicy.IsSelectable(CadEditor.Document, entity) &&
               !_disabledSelectionEntityTypes.Contains(entity.GetType());
    }

    private bool PruneSelectionToFilter(CadDocumentChangeSet? changes = null)
    {
        bool IsInvalid(EntityId id) => !CadEditor.Document.TryGetEntity(id, out var entity) ||
            entity is null || !CanSelectEntity(entity);
        if (changes is null || changes.AffectsDocumentStructure || changes.AffectsLayoutStructure)
            return CadEditor.Selection.RemoveWhere(IsInvalid);

        const CadEntityChangeKind accessChanges = CadEntityChangeKind.Created | CadEntityChangeKind.Deleted |
            CadEntityChangeKind.Layer | CadEntityChangeKind.Visibility | CadEntityChangeKind.Metadata;
        var changed = false;
        foreach (var change in changes.EntityChanges)
            if ((change.Kind & accessChanges) != 0 && CadEditor.Selection.Contains(change.EntityId) && IsInvalid(change.EntityId))
                changed |= CadEditor.Selection.Remove(change.EntityId);
        return changed;
    }
}
