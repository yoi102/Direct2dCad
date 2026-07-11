using System.Windows;
using Direct2dCad.ViewModels.Services.ViewServices;

namespace Direct2dCad.wpf.Views.Settings.DocumentSettings;

public partial class DocumentSettingsDialog
{
    public DocumentSettingsDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IDocumentSettingsDialogViewModel viewModel && viewModel.TryApply())
            DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IDocumentSettingsDialogViewModel viewModel)
            viewModel.TryApply();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
