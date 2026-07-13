using System.Globalization;
using System.Windows.Controls;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class UnsavedDocumentsDialog : UserControl
{
    public UnsavedDocumentsDialog(IReadOnlyList<UnsavedDocumentInfo> documents)
    {
        InitializeComponent();

        var unsavedLocation = Strings.ResourceManager.GetString(
            "UnsavedDocumentNoPath",
            CultureInfo.CurrentUICulture) ?? "Not saved yet";
        DocumentsList.ItemsSource = documents.Select(document => new DocumentListItem(
            document.Name,
            string.IsNullOrWhiteSpace(document.FilePath)
                ? unsavedLocation
                : document.FilePath));
    }

    private sealed record DocumentListItem(string Name, string Location);
}
