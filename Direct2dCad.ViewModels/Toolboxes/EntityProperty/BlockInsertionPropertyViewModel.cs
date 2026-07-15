using CommunityToolkit.Mvvm.ComponentModel;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class TransientBlockInsertionPropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientBlockInsertionPropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;

    [ObservableProperty]
    public partial string DefinitionName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    [ObservableProperty]
    public partial double ScaleX { get; set; } = 1.0;

    [ObservableProperty]
    public partial double ScaleY { get; set; } = 1.0;

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            DefinitionName = _documentViewModel.BlockInsertionDefinitionId is { } blockId &&
                             _documentViewModel.CadEditor.Document.TryGetBlock(blockId, out var block) &&
                             block is not null
                ? block.Name
                : string.Empty;
            RotationDegrees = _documentViewModel.BlockInsertionRotationDegrees;
            ScaleX = _documentViewModel.BlockInsertionScaleX;
            ScaleY = _documentViewModel.BlockInsertionScaleY;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnRotationDegreesChanged(double value) => CommitTransform();
    partial void OnScaleXChanged(double value) => CommitTransform();
    partial void OnScaleYChanged(double value) => CommitTransform();

    private void CommitTransform()
    {
        if (_isRefreshing ||
            !double.IsFinite(RotationDegrees) ||
            ScaleX <= 0 || ScaleY <= 0 ||
            !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY))
        {
            return;
        }

        _documentViewModel.UpdateBlockInsertionTransform(RotationDegrees, ScaleX, ScaleY);
    }
}
