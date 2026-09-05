using System.Collections.ObjectModel;
using System.ComponentModel;
using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class BlocksToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly IDisposable _interactionSubscription;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IPublisher<CadBlockDefinitionSelectionChangedMessage> _selectionChangedPublisher;
    private CadDocumentViewModel? _documentViewModel;
    private int _attachmentVersion;
    private bool _isRefreshing;
    private long _catalogVersion = -1;

    public BlocksToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        IDialogService dialogService,
        ISnackbarService snackbarService,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionSubscriber,
        IPublisher<CadBlockDefinitionSelectionChangedMessage> selectionChangedPublisher)
        : base(toolboxLayoutSettingsStore, "toolbox.blocks", DockZone.BottomLeft, isOpenByDefault: true)
    {
        Title = Localize("Blocks", "Blocks");
        Icon = toolboxIconProvider.Blocks;
        CanClose = false;
        _dialogService = dialogService;
        _snackbarService = snackbarService;
        _selectionChangedPublisher = selectionChangedPublisher;
        _interactionSubscription = interactionSubscriber.Subscribe(OnInteractionStateChanged);
        PropertyChanged += OnToolboxPropertyChanged;
    }

    public ObservableCollection<BlockItemViewModel> Blocks { get; } = [];
    public bool HasDocument => _documentViewModel is not null;
    public bool IsEditingBlock => _documentViewModel?.IsEditingBlock == true;
    public string InsertLabel => Localize("InsertBlock", "Insert block");
    public string EditLabel => Localize("EditBlock", "Edit block");
    public string ExitEditLabel => Localize("ExitBlockEditor", "Exit block editor");
    public string DeleteLabel => Localize("DeleteBlock", "Delete block");
    public string ReferencesLabel => Localize("References", "References");
    public string EntitiesLabel => Localize("Entities", "Entities");

    [ObservableProperty]
    public partial BlockItemViewModel? SelectedBlock { get; set; }

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (!ReferenceEquals(_documentViewModel, documentViewModel))
        {
            _attachmentVersion++;
            Blocks.Clear();
            _catalogVersion = -1;
        }
        if (_documentViewModel is not null &&
            !ReferenceEquals(_documentViewModel, documentViewModel))
        {
            _selectionChangedPublisher.Publish(
                new CadBlockDefinitionSelectionChangedMessage(_documentViewModel, null));
        }

        _documentViewModel = documentViewModel;
        OnPropertyChanged(nameof(HasDocument));
        RefreshBlocks();
    }

    partial void OnSelectedBlockChanged(BlockItemViewModel? value)
    {
        NotifyCommandStates();
        if (_documentViewModel is not null)
        {
            _selectionChangedPublisher.Publish(
                new CadBlockDefinitionSelectionChangedMessage(
                    _documentViewModel,
                    value?.BlockId));
        }
    }

    internal void RenameBlock(BlockItemViewModel item, string name)
    {
        if (_documentViewModel is null || _isRefreshing || !Blocks.Contains(item) || string.IsNullOrWhiteSpace(name))
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
            _snackbarService.Enqueue(ex.Message, level: CadMessageLevel.Error);
        }
        RefreshBlocks(item.BlockId);
    }

    [RelayCommand(CanExecute = nameof(CanInsert))]
    private void Insert()
    {
        if (_documentViewModel is null || SelectedBlock is null)
            return;
        try
        {
            _documentViewModel.BeginBlockInsertion(SelectedBlock.BlockId);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message, level: CadMessageLevel.Error);
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
            _snackbarService.Enqueue(ex.Message, level: CadMessageLevel.Error);
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

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (_documentViewModel is null || SelectedBlock is null)
            return;
        var selectedId = SelectedBlock.BlockId;
        var documentViewModel = _documentViewModel;
        var attachmentVersion = _attachmentVersion;
        var confirmed = await _dialogService.ShowOrReplaceMessageDialogWithCancelAsync(
            string.Format(Localize("DeleteBlockConfirmFormat", "Delete block '{0}'?"), SelectedBlock.Name),
            DeleteLabel,
            ViewServiceIdentifiers.RootDialogHost);
        if (!confirmed || attachmentVersion != _attachmentVersion)
            return;
        try
        {
            documentViewModel.CadEditor.DeleteBlock(selectedId);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message, level: CadMessageLevel.Error);
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

        if (message.ClearBlockDefinitionSelection)
            SelectedBlock = null;
        if (IsOpen && _catalogVersion != _documentViewModel!.CadEditor.BlockCatalogVersion)
            RefreshBlocks(SelectedBlock?.BlockId);
        OnPropertyChanged(nameof(IsEditingBlock));
        NotifyCommandStates();
    }

    private void OnToolboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IsOpen))
            return;
        if (!IsOpen)
            SelectedBlock = null;
        else
            RefreshBlocks(SelectedBlock?.BlockId);
    }

    private void RefreshBlocks(BlockId? selectedBlockId = null)
    {
        _isRefreshing = true;
        try
        {
            if (_documentViewModel is not null)
            {
                var document = _documentViewModel.CadEditor.Document;
                var definitions = document.Blocks.Values
                    .Where(block => !block.IsSystem)
                    .OrderBy(block => block.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var existing = Blocks.ToDictionary(item => item.BlockId);
                var retained = definitions.Select(block => block.Id).ToHashSet();
                for (var index = Blocks.Count - 1; index >= 0; index--)
                    if (!retained.Contains(Blocks[index].BlockId))
                        Blocks.RemoveAt(index);
                for (var index = 0; index < definitions.Length; index++)
                {
                    var block = definitions[index];
                    if (existing.TryGetValue(block.Id, out var item))
                    {
                        item.Refresh(block, document.GetBlockReferenceCount(block.Id));
                        if (!ReferenceEquals(Blocks[index], item))
                            Blocks.Move(Blocks.IndexOf(item), index);
                    }
                    else
                    {
                        Blocks.Insert(index, new BlockItemViewModel(this, block,
                            document.GetBlockReferenceCount(block.Id)));
                    }
                }
                _catalogVersion = _documentViewModel.CadEditor.BlockCatalogVersion;
            }
            else
                Blocks.Clear();

            SelectedBlock = selectedBlockId is { } id
                ? Blocks.FirstOrDefault(block => block.BlockId.Equals(id))
                : null;
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
        InsertCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ExitEditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private static string Localize(string key, string fallback) =>
        Strings.ResourceManager.GetString(key) ?? fallback;

    public void Dispose()
    {
        Attach(null);
        PropertyChanged -= OnToolboxPropertyChanged;
        _interactionSubscription.Dispose();
    }
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
        Refresh(block, referenceCount);
    }

    internal void Refresh(CadBlockDefinition block, int referenceCount)
    {
        _refreshing = true;
        try
        {
            Name = block.Name;
            EntityCount = block.EntityIds.Count;
            ReferenceCount = referenceCount;
            IsReadOnly = block.IsReadOnly;
            IsSystem = block.IsSystem;
        }
        finally { _refreshing = false; }
    }

    public BlockId BlockId { get; }
    [ObservableProperty]
    public partial int EntityCount { get; private set; }
    [ObservableProperty]
    public partial int ReferenceCount { get; private set; }
    [ObservableProperty]
    public partial bool IsReadOnly { get; private set; }
    [ObservableProperty]
    public partial bool IsSystem { get; private set; }
    public string IdText => BlockId.Value.ToString();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (!_refreshing)
            _owner.RenameBlock(this, value);
    }
}
