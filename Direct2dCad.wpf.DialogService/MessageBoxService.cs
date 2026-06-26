using System.Windows;
using Direct2dCad.IDialogService;

namespace Direct2dCad.wpf.DialogService;

internal class MessageBoxService : IMessageBoxService
{
    public void ShowMessage(string message, string caption)
    {
        MessageBox.Show(message, caption, MessageBoxButton.OK);
    }
}
