using System.Windows;
using Direct2dCad.ViewServices.Abstractions;

namespace Direct2dCad.wpf.Services;

internal class MessageBoxService : IMessageBoxService
{
    public void ShowMessage(string message, string caption)
    {
        MessageBox.Show(message, caption, MessageBoxButton.OK);
    }
}
