using System;
using System.Windows;
using System.Windows.Controls;

namespace Direct2dCad.wpf.Views;

public partial class CadEditParametersView : UserControl
{
    public event EventHandler? RequestCanvasFocus;

    public CadEditParametersView() => InitializeComponent();

    private void Action_OnClick(object sender, RoutedEventArgs e) => RequestCanvasFocus?.Invoke(this, EventArgs.Empty);
}
