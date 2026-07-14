using System.Collections.ObjectModel;
using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class BlocksToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly IDisposable _interactionSubscription;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private CadDocumentViewModel? _documentViewModel;
    private bool _isRefreshing;

    public BlocksToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        IDialogService dialogService,
        ISnackbarService snackbarService,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionSubscriber)
        : base(toolboxLayoutSettingsStore, "toolbox.blocks", DockZone.BottomLeft, isOpenByDefault: true)
    {
        Title = Localize("Blocks", "Blocks");
        Icon = toolboxIconProvider.Blocks;
        IsOpenByDefault = false;
        CanClose = false;
        _dialogService = dialogService;
        _snackbarService = snackbarService;
        _interactionSubscription = interactionSubscriber.Subscribe(OnInteractionStateChanged);
    }

    public ObservableCollection<BlockItemViewModel> Blocks { get; } = [];
    public bool HasDocument => _documentViewModel is not null;
    public bool IsEditingBlock => _documentViewModel?.IsEditingBlock == true;
    public string CreateLabel => Localize("CreateBlock", "Create block");
    public string InsertLabel => Localize("InsertBlock", "Insert block");
    public string EditLabel => Localize("EditBlock", "Edit block");
    public string ExitEditLabel => Localize("ExitBlockEditor", "Exit block editor");
    public string DeleteLabel => Localize("DeleteBlock", "Delete block");
    public string BasePointLabel => Localize("BasePoint", "Base point");
    public string ReferencesLabel => Localize("References", "References");
    public string EntitiesLabel => Localize("Entities", "Entities");
    public string RotationLabel => Localize("Rotation", "Rotation");
    public string ScaleXLabel => Localize("ScaleX", "Scale X");
    public string ScaleYLabel => Localize("ScaleY", "Scale Y");

    [ObservableProperty]
    public partial BlockItemViewModel? SelectedBlock { get; set; }

    [ObservableProperty]
    public partial string NewBlockName { get; set; } = "Block 1";

    [ObservableProperty]
    public partial double CreateBasePointX { get; set; }

    [ObservableProperty]
    public partial double CreateBasePointY { get; set; }

    [ObservableProperty]
    public partial double SelectedBasePointX { get; set; }

    [ObservableProperty]
    public partial double SelectedBasePointY { get; set; }

    [ObservableProperty]
    public partial double InsertRotationDegrees { get; set; }

    [ObservableProperty]
    public partial double InsertScaleX { get; set; } = 1;

    [ObservableProperty]
    public partial double InsertScaleY { get; set; } = 1;

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        _documentViewModel = documentViewModel;
        OnPropertyChanged(nameof(HasDocument));
        RefreshBlocks();
    }

    partial void OnSelectedBlockChanged(BlockItemViewModel? value)
    {
        _isRefreshing = true;
        try
        {
            SelectedBasePointX = value?.BasePoint.X ?? 0;
            SelectedBasePointY = value?.BasePoint.Y ?? 0;
        }
        finally
        {
            _isRefreshing = false;
        }

        NotifyCommandStates();
    }

    internal void RenameBlock(BlockItemViewModel item, string name)
    {
        if (_documentViewModel is null || _isRefreshing || string.IsNullOrWhiteSpace(name))
            return;
        var current = _documentViewModel.CadEditor.Document.GetBlock(item.BlockId);
        if (string.Equals(current.Name, name.Trim(), StringComparison.Ordinal))
            return;
        try
        {
            _documentViewModel.CadEditor.RenameBlock(item.BlockId, name);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
        RefreshBlocks(item.BlockId);
    }

    [RelayCommand(CanExecute = nameof(CanCreateFromSelection))]
    private void CreateFromSelection()
    {
        if (_documentViewModel is null)
            return;
        var selectedIds = _documentViewModel.CadEditor.Selection.EntityIds
            .Where(id => _documentViewModel.CadEditor.Document.TryGetEntity(id, out var entity) &&
                         entity is { IsErased: false } &&
                         entity.OwnerBlockId.Equals(_documentViewModel.CadEditor.ActiveOwnerBlockId))
            .ToArray();
        if (selectedIds.Length == 0)
            return;

        try
        {
            var command = _documentViewModel.CadEditor.CreateBlock(
                selectedIds,
                NewBlockName,
                new CadPointD(CreateBasePointX, CreateBasePointY),
                _documentViewModel.DrawingLayerId);
            if (command.CreatedReferenceId is { } referenceId)
                _documentViewModel.SelectEntities([referenceId]);
            RefreshBlocks(command.CreatedBlockId);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
    }

    private bool CanCreateFromSelection() =>
        _documentViewModel is not null && _documentViewModel.CadEditor.Selection.EntityIds.Count > 0;

    [RelayCommand]
    private void UseSelectionCenter()
    {
        if (_documentViewModel is null)
            return;
        var bounds = _documentViewModel.CadEditor.Selection.EntityIds
            .Select(id => _documentViewModel.CadEditor.Document.TryGetEntity(id, out var entity) ? entity : null)
            .Where(entity => entity is { IsErased: false })
            .Aggregate(CadRectD.Empty, static (current, entity) => current.Union(entity!.Bounds));
        if (bounds.IsEmpty)
            return;
        CreateBasePointX = bounds.Center.X;
        CreateBasePointY = bounds.Center.Y;
    }

    [RelayCommand(CanExecute = nameof(CanInsert))]
    private void Insert()
    {
        if (_documentViewModel is null || SelectedBlock is null)
            return;
        try
        {
            _documentViewModel.BeginBlockInsertion(
                SelectedBlock.BlockId,
                InsertRotationDegrees * Math.PI / 180.0,
                InsertScaleX,
                InsertScaleY);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
    }

    private bool CanInsert() => SelectedBlock is { IsSystem: false };

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (_documentViewModel is null || SelectedBlock is null)
            return;
        try
        {
            _documentViewModel.EditBlockDefinition(SelectedBlock.BlockId);
            OnPropertyChanged(nameof(IsEditingBlock));
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
    }

    private bool CanEdit() => SelectedBlock is { IsReadOnly: false, IsSystem: false };

    [RelayCommand(CanExecute = nameof(CanExitEdit))]
    private void ExitEdit()
    {
        _documentViewModel?.ExitBlockEditing();
        OnPropertyChanged(nameof(IsEditingBlock));
    }

    private bool CanExitEdit() => IsEditingBlock;

    [RelayCommand(CanExecute = nameof(CanApplyBasePoint))]
    private void ApplyBasePoint()
    {
        if (_documentViewModel is null || SelectedBlock is null || _isRefreshing)
            return;
        try
        {
            _documentViewModel.CadEditor.SetBlockDefinitionBasePoint(
                SelectedBlock.BlockId,
                new CadPointD(SelectedBasePointX, SelectedBasePointY));
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
        RefreshBlocks(SelectedBlock.BlockId);
    }

    private bool CanApplyBasePoint() => CanEdit();

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (_documentViewModel is null || SelectedBlock is null)
            return;
        var selectedId = SelectedBlock.BlockId;
        var confirmed = await _dialogService.ShowOrReplaceMessageDialogWithCancelAsync(
            string.Format(Localize("DeleteBlockConfirmFormat", "Delete block '{0}'?"), SelectedBlock.Name),
            DeleteLabel,
            ViewServiceIdentifiers.RootDialogHost);
        if (!confirmed)
            return;
        try
        {
            _documentViewModel.CadEditor.DeleteBlock(selectedId);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }
        RefreshBlocks();
    }

    private bool CanDelete() =>
        SelectedBlock is
        {
            IsReadOnly: false,
            IsSystem: false,
            ReferenceCount: 0
        } selected &&
        _documentViewModel?.EditingBlockId != selected.BlockId;

    [RelayCommand]
    private void Refresh() => RefreshBlocks(SelectedBlock?.BlockId);

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;
        RefreshBlocks(SelectedBlock?.BlockId);
    }

    private void RefreshBlocks(BlockId? selectedBlockId = null)
    {
        _isRefreshing = true;
        try
        {
            Blocks.Clear();
            if (_documentViewModel is not null)
            {
                var document = _documentViewModel.CadEditor.Document;
                foreach (var block in document.Blocks.Values
                             .Where(block => !block.IsSystem)
                             .OrderBy(block => block.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    Blocks.Add(new BlockItemViewModel(
                        this,
                        block,
                        document.GetBlockReferenceIds(block.Id).Count));
                }
                NewBlockName = CreateUniqueBlockName(document);
            }

            SelectedBlock = selectedBlockId is { } id
                ? Blocks.FirstOrDefault(block => block.BlockId.Equals(id)) ?? Blocks.FirstOrDefault()
                : Blocks.FirstOrDefault();
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(IsEditingBlock));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        CreateFromSelectionCommand.NotifyCanExecuteChanged();
        InsertCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ExitEditCommand.NotifyCanExecuteChanged();
        ApplyBasePointCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private static string CreateUniqueBlockName(CadDocument document)
    {
        for (var index = 1; ; index++)
        {
            var name = $"Block {index}";
            if (document.Blocks.Values.All(block =>
                    !string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private static string Localize(string key, string fallback) =>
        Strings.ResourceManager.GetString(key) ?? fallback;

    public void Dispose() => _interactionSubscription.Dispose();
}

public sealed partial class BlockItemViewModel : ObservableObject
{
    private readonly BlocksToolboxViewModel _owner;
    private bool _refreshing;

    public BlockItemViewModel(
        BlocksToolboxViewModel owner,
        CadBlockDefinition block,
        int referenceCount)
    {
        _owner = owner;
        BlockId = block.Id;
        _refreshing = true;
        Name = block.Name;
        _refreshing = false;
        BasePoint = block.BasePoint;
        EntityCount = block.EntityIds.Count;
        ReferenceCount = referenceCount;
        IsReadOnly = block.IsReadOnly;
        IsSystem = block.IsSystem;
    }

    public BlockId BlockId { get; }
    public CadPointD BasePoint { get; }
    public int EntityCount { get; }
    public int ReferenceCount { get; }
    public bool IsReadOnly { get; }
    public bool IsSystem { get; }
    public string IdText => BlockId.Value.ToString();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (!_refreshing)
            _owner.RenameBlock(this, value);
    }
}
