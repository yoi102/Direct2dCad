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

    public void RefreshFromDocument()
    {
        RefreshPasteLayerOptions(_documentViewModel);
    }
}
