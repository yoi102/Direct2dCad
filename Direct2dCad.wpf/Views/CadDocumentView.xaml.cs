using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Direct2dCad.wpf.Views;
/// <summary>
/// CadDocumentView.xaml 的交互逻辑
/// </summary>
public partial class CadDocumentView : IDisposable
{
    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(
            nameof(SaveCommand),
            typeof(ICommand),
            typeof(CadDocumentView),
            new PropertyMetadata(null));

    public CadDocumentView()
    {
        InitializeComponent();
    }

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public void Dispose()
    {
        cadCanvas.Dispose();
    }
}
