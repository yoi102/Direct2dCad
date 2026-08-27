using System.Globalization;
using System.Windows.Controls;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class UnsavedDocumentDialog : UserControl
{
    public UnsavedDocumentDialog(string documentName)
    {
        InitializeComponent();

        MessageTextBlock.Text = string.Format(
            CultureInfo.CurrentUICulture,
            Strings.UnsavedDocumentMessageFormat,
            documentName);
    }
}
