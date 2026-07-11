using System.Windows;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Views.Settings.UserSettings;

public partial class UserSettingsDialog
{
    public UserSettingsDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IUserSettingsDialogViewModel viewModel && viewModel.TryApply())
            DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IUserSettingsDialogViewModel viewModel)
            viewModel.TryApply();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IUserSettingsDialogViewModel viewModel)
            viewModel.ResetToDefaults();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
