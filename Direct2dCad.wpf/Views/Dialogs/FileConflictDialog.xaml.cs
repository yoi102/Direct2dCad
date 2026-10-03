using System.Windows.Controls;
using Direct2dCad.Lang;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class FileConflictDialog : UserControl
{
    public FileConflictDialog(string path)
    {
        InitializeComponent();TitleText.Text=CadUiText.Get(LangKeys.SaveConflict);
        DescriptionText.Text=CadUiText.Get("FileConflictDescription");PathText.Text=path;
        SaveAsButton.Content=CadUiText.Get(LangKeys.SaveAs);OverwriteButton.Content=CadUiText.Get("OverwriteExternalChanges");CancelButton.Content=CadUiText.Get(LangKeys.Cancel);
    }
}
