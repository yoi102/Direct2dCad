using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels;
using System.ComponentModel;

namespace Direct2dCad.Avalonia.Views;

internal sealed class PropertyInspector : ContentControl, IDisposable
{
    private readonly EntityPropertiesToolboxViewModel _model;
    private CadDocumentViewModel? _document;
    private object? _entity;
    private readonly StackPanel _forms = new();
    private readonly ContentControl _parameters = new(), _properties = new();
    private readonly TextBlock _empty = new() { Margin = new Thickness(12,8), TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly StackPanel _association = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12,6,12,8) };
    private readonly Button _reassociate = new() { Content = new CadIcon { Kind = "LinkVariant" } }, _detach = new() { Content = new CadIcon { Kind = "LinkVariantOff" } };
    private readonly Grid _surface = new() { RowDefinitions = RowDefinitions.Parse("*,Auto") };
    private Control? _annotationView, _editView;
    public PropertyInspector(EntityPropertiesToolboxViewModel model)
    {
        DataContext = _model = model;
        Classes.Add("cadProperties");
        _forms.Children.Add(_parameters);_forms.Children.Add(_properties);
        _surface.Children.Add(new ScrollViewer { Content = _forms, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        _surface.Children.Add(_empty);
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(_empty,"EntityPropertiesEmptyHint");
        Loc.Track(_empty,TextBlock.TextProperty,"DrawingAssistantEmpty");
        Loc.Track(_reassociate,ToolTip.TipProperty,"Reassociate");Loc.Track(_detach,ToolTip.TipProperty,"DetachAssociation");
        _reassociate.Classes.Add("toolIcon");_detach.Classes.Add("toolIcon");
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(_reassociate,"ReassociateDimensionButton");
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(_detach,"DetachDimensionButton");
        _association.Children.Add(_reassociate);_association.Children.Add(_detach);Grid.SetRow(_association,1);_surface.Children.Add(_association);
        Content = _surface;
        model.PropertyChanged += ModelChanged;
        AttachDocument();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(EntityPropertiesToolboxViewModel.DocumentViewModel))AttachDocument();
        else if(e.PropertyName==nameof(EntityPropertiesToolboxViewModel.Entity))Refresh();
    }
    private void AttachDocument()
    {
        if(_document is not null)_document.PropertyChanged-=DocumentChanged;
        _document=_model.DocumentViewModel;
        if(_document is not null)_document.PropertyChanged+=DocumentChanged;
        _annotationView=_document is null?null:new Generated.CadAnnotationParametersView {DataContext=_document};
        _editView=_document is null?null:new Generated.CadEditParametersView {DataContext=_document};
        _reassociate.Command=_document?.ReassociateDimensionCommand;_detach.Command=_document?.DetachDimensionCommand;
        Refresh();
    }
    private void DocumentChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(CadDocumentViewModel.IsDimensionContext) or nameof(CadDocumentViewModel.CadCanvasToolMode)
            or nameof(CadDocumentViewModel.HasSelectedDimension) or nameof(CadDocumentViewModel.CanEditDocument)
            or nameof(CadDocumentViewModel.CanEditDimensionParameters))Refresh();
    }
    private void Refresh()
    {
        _empty.IsVisible=_document is null;
        _forms.IsVisible=_document is not null;
        _parameters.Content=_document is {IsDimensionContext:true}?_annotationView:_document is {IsCurveEditTool:true}?_editView:null;
        if(!ReferenceEquals(_entity,_model.Entity)) { _entity=_model.Entity;_properties.Content=KnownViews.Create(_entity); }
        _properties.IsEnabled=_document?.CanEditDocument==true;
        _association.IsVisible=_document?.HasSelectedDimension==true;_association.IsEnabled=_document?.CanEditDimensionParameters==true;
    }
    public void Dispose()
    {
        _model.PropertyChanged-=ModelChanged;
        if(_document is not null)_document.PropertyChanged-=DocumentChanged;
    }
}


