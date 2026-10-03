using System.Windows;
using System.Windows.Controls;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

public partial class StrokeStylePropertySection : UserControl
{
    private static readonly Dictionary<string,bool> ExpandedByContext=[];
    private bool _applyingContext;
    private string ContextKey=>ViewModel?.GetType().FullName ?? "";
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(IStrokeStylePropertySectionViewModel),
        typeof(StrokeStylePropertySection),new PropertyMetadata(null,OnViewModelChanged));

    public IStrokeStylePropertySectionViewModel? ViewModel
    {
        get => (IStrokeStylePropertySectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public StrokeStylePropertySection()
    {
        InitializeComponent();
    }
    private static void OnViewModelChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        var view=(StrokeStylePropertySection)sender;
        if(view.AdvancedStroke is null) return;
        view._applyingContext=true;
        view.AdvancedStroke.IsExpanded=ExpandedByContext.GetValueOrDefault(view.ContextKey);
        view._applyingContext=false;
    }
    private void OnAdvancedStrokeChanged(object sender,RoutedEventArgs args)
    {if(!_applyingContext && ViewModel is not null) ExpandedByContext[ContextKey]=AdvancedStroke.IsExpanded;}
}
