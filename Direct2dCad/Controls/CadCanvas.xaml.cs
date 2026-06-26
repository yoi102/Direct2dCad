using System.Windows;
using Direct2dCad.Rendering.Direct2D;

namespace Direct2dCad.wpf.Controls;

/// <summary>
/// CadCanvas.xaml 的交互逻辑
/// </summary>
public partial class CadCanvas
{
    public CadCanvas()
    {
        InitializeComponent();

        Loaded += CadCanvas_Loaded;
        SizeChanged += CadCanvas_SizeChanged;
        Unloaded += CadCanvas_Unloaded;
        MouseDown += CadCanvas_MouseDown;
    }

    private void CadCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => throw new NotImplementedException();

    private void CadCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        d3d11ImageSource.SetSize((int)ActualWidth, (int)ActualHeight);
        Direct2DCadRender?.SetSize((int)ActualWidth, (int)ActualHeight);
    }

    private void CadCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        InvalidateArrange();
        d3d11ImageSource.SetSize((int)ActualWidth, (int)ActualHeight);
        Direct2DCadRender?.SetSize((int)ActualWidth, (int)ActualHeight);
    }

    private void CadCanvas_Unloaded(object sender, RoutedEventArgs e)
    {
        d3d11ImageSource.Dispose();
    }

    public Direct2DCadRender Direct2DCadRender
    {
        get { return (Direct2DCadRender)GetValue(Direct2DCadRenderProperty); }
        set { SetValue(Direct2DCadRenderProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Direct2DCadRender.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty Direct2DCadRenderProperty =
        DependencyProperty.Register(nameof(Direct2DCadRender), typeof(Direct2DCadRender), typeof(CadCanvas), new PropertyMetadata(null, OnDirect2DCadRenderChanged));

    private static void OnDirect2DCadRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CadCanvas cadCanvas)
            return;

        if (e.NewValue is Direct2DCadRender newRender)
        {
            newRender.AttachImageSource(cadCanvas.d3d11ImageSource);
            newRender.SetSize((int)cadCanvas.ActualWidth, (int)cadCanvas.ActualHeight);
        }
    }
}
