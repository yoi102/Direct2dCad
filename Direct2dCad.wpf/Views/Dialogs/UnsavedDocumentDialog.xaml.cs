using System.Globalization;
using System.Windows.Controls;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class UnsavedDocumentDialog : UserControl
{
    public UnsavedDocumentDialog(string documentName)
    {
        InitializeComponent();

        var format = Strings.ResourceManager.GetString(
            "UnsavedDocumentMessageFormat",
            CultureInfo.CurrentUICulture) ?? "Do you want to save changes to \"{0}\"?";
        MessageTextBlock.Text = string.Format(
            CultureInfo.CurrentUICulture,
            format,
            documentName);
    }
}
