using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class BlockDefinitionPropertyViewModel : ObservableObject
{
    private readonly CadDocumentViewModel _documentViewModel;
    private readonly ISnackbarService _snackbarService;
    private bool _isRefreshing;

    public BlockDefinitionPropertyViewModel(
        CadDocumentViewModel documentViewModel,
        BlockId blockId,
        ISnackbarService snackbarService)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        BlockId = blockId;
        RefreshFromDefinition();
    }

    public BlockId BlockId { get; }
    public string BlockIdText => BlockId.Value.ToString();
    public string Title => Localize("BlockDefinition", "Block definition");
    public string NameLabel => Localize("Name", "Name");
    public string EntitiesLabel => Localize("Entities", "Entities");
    public string ReferencesLabel => Localize("References", "References");
    public string BasePointLabel => Localize("BasePoint", "Base point");

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int EntityCount { get; private set; }

    [ObservableProperty]
    public partial int ReferenceCount { get; private set; }

    [ObservableProperty]
    public partial double BasePointX { get; set; }

    [ObservableProperty]
    public partial double BasePointY { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; private set; }

    public void RefreshFromDefinition()
    {
        if (!_documentViewModel.CadEditor.Document.TryGetBlock(BlockId, out var block) ||
            block is null)
        {
            IsEditable = false;
            ApplyBasePointCommand.NotifyCanExecuteChanged();
            return;
        }

        _isRefreshing = true;
        try
        {
            Name = block.Name;
            EntityCount = block.EntityIds.Count;
            ReferenceCount = _documentViewModel.CadEditor.Document.GetBlockReferenceCount(BlockId);
            BasePointX = block.BasePoint.X;
            BasePointY = block.BasePoint.Y;
            IsEditable = !block.IsReadOnly && !block.IsSystem;
        }
        finally
        {
            _isRefreshing = false;
        }

        ApplyBasePointCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApplyBasePoint))]
    private void ApplyBasePoint()
    {
        if (_isRefreshing ||
            !double.IsFinite(BasePointX) ||
            !double.IsFinite(BasePointY) ||
            !_documentViewModel.CadEditor.Document.TryGetBlock(BlockId, out var block) ||
            block is null)
        {
            return;
        }

        var basePoint = new CadPointD(BasePointX, BasePointY);
        if (block.BasePoint.NearEquals(basePoint, 1e-9))
            return;

        try
        {
            _documentViewModel.CadEditor.SetBlockDefinitionBasePoint(BlockId, basePoint);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message);
        }

        RefreshFromDefinition();
    }

    private bool CanApplyBasePoint() => IsEditable;

    private static string Localize(string key, string fallback) =>
        Strings.ResourceManager.GetString(key) ?? fallback;
}
