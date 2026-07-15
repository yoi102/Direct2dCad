using Direct2dCad.Commands.Clipboard;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public sealed class TransientPastePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;

    public TransientPastePropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;

    public int EntityCount => _documentViewModel.ActivePasteSnapshot?.Items.Count ?? 0;

    public int BlockReferenceCount => _documentViewModel.ActivePasteSnapshot?.Items.Count(
        item => item.Entity is CadBlockReferenceClipboardSnapshot) ?? 0;

    public int BlockDefinitionCount => _documentViewModel.ActivePasteSnapshot?.BlockDefinitions.Count ?? 0;

    public bool HasBlockReferences => BlockReferenceCount > 0;

    public bool HasBlockDefinitions => BlockDefinitionCount > 0;

    public void RefreshFromDocument()
    {
        RefreshPasteLayerOptions(_documentViewModel);
        OnPropertyChanged(nameof(EntityCount));
        OnPropertyChanged(nameof(BlockReferenceCount));
        OnPropertyChanged(nameof(BlockDefinitionCount));
        OnPropertyChanged(nameof(HasBlockReferences));
        OnPropertyChanged(nameof(HasBlockDefinitions));
    }
}
