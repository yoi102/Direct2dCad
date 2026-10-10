using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.VisualTree;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels;
using System.Windows.Input;

namespace Direct2dCad.Avalonia.Controls;

internal sealed class CadRadialMenu(CadCanvas owner, CadDocumentViewModel document, ICommand command)
{
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ZIndex = 1000 };
    private Panel? _overlayHost;
    private readonly Canvas _surface = new() { Width = 248, Height = 248, Background = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Views.Generated.RadialIconResources _icons = new();
    private IReadOnlyList<CadRadialMenuAction> _actions = [];
    private Point _origin;
    private int _selected = -1, _page;
    public bool IsVisible => _overlayHost is not null;
    internal bool UsesOwnerWindow => IsVisible && TopLevel.GetTopLevel(_surface) == TopLevel.GetTopLevel(owner);
    internal CadRadialMenuGesture Gesture { get; private set; }
    public void Show(Point point, KeyModifiers modifiers)
    {
        Close();
        _overlayHost = OverlayLayer.GetOverlayLayer(owner) ?? owner.GetVisualParent() as Panel;
        if(_overlayHost is null)return;
        _origin = point; _page = 0; _selected = -1;
        var anchor = owner.TranslatePoint(point,_overlayHost) ?? point;
        _overlay.Width=_overlayHost.Bounds.Width;_overlay.Height=_overlayHost.Bounds.Height;
        if(!_overlay.Children.Contains(_surface))_overlay.Children.Add(_surface);
        Canvas.SetLeft(_surface,anchor.X-124);Canvas.SetTop(_surface,anchor.Y-124);
        SetModifiers(modifiers);_overlayHost.Children.Add(_overlay);
    }
    public void SetModifiers(KeyModifiers modifiers)
    {
        var gesture = modifiers.HasFlag(KeyModifiers.Shift) ? CadRadialMenuGesture.ShiftMiddle : modifiers.HasFlag(KeyModifiers.Control) ? CadRadialMenuGesture.ControlMiddle : modifiers.HasFlag(KeyModifiers.Alt) ? CadRadialMenuGesture.AltMiddle : CadRadialMenuGesture.Middle;
        if (gesture != Gesture) { _page = 0; _selected = -1; }
        Gesture = gesture;
        _actions = document.UserSettings.Interaction.RadialMenu.GetActions(gesture); Draw();
    }
    public void Wheel(double delta) { var pages = Math.Max(1, (_actions.Count + 7) / 8); _page = (_page + (delta < 0 ? 1 : pages - 1)) % pages; Draw(); }
    public void Move(Point point) { var v = point - _origin; _selected = Math.Sqrt(v.X * v.X + v.Y * v.Y) < 36 ? -1 : ((int)Math.Round((Math.Atan2(v.Y, v.X) + Math.PI / 2) / (Math.PI / 4)) + 8) % 8; Draw(); }
    private void Draw()
    {
        _surface.Children.Clear();
        Point OnCircle(double radius,double angle) => new(124 + Math.Cos(angle)*radius,124 + Math.Sin(angle)*radius);
        for (var i=0;i<8;i++)
        {
            var angle=i*Math.PI/4-Math.PI/2; var start=angle-Math.PI/8; var end=angle+Math.PI/8;
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                context.BeginFigure(OnCircle(36,start),true); context.LineTo(OnCircle(116,start));
                context.ArcTo(OnCircle(116,end),new Size(116,116),0,false,SweepDirection.Clockwise);
                context.LineTo(OnCircle(36,end)); context.ArcTo(OnCircle(36,start),new Size(36,36),0,false,SweepDirection.CounterClockwise); context.EndFigure(true);
            }
            var selected=i==_selected;
            _surface.Children.Add(new global::Avalonia.Controls.Shapes.Path { Data=geometry, Fill=new SolidColorBrush(selected?Color.FromArgb(248,103,58,183):Color.FromArgb(246,37,37,42)), Stroke=new SolidColorBrush(selected?Color.FromRgb(198,168,245):Color.FromArgb(150,85,85,95)),StrokeThickness=selected?1.5:0.65,IsHitTestVisible=false });
            var index=_page*8+i;
            if(index>=_actions.Count || _actions[index]==CadRadialMenuAction.None) continue;
            if(_icons.Resources.TryGetValue(_actions[index].ToString(),out var resource) && resource is global::Avalonia.Controls.Templates.IDataTemplate template && template.Build(null) is Control icon)
            {
                var iconCenter=OnCircle(76,angle); Canvas.SetLeft(icon,iconCenter.X-14); Canvas.SetTop(icon,iconCenter.Y-14); icon.IsHitTestVisible=false; _surface.Children.Add(icon);
            }
        }
        var center=new Border {Width=72,Height=72,CornerRadius=new CornerRadius(36),Background=new SolidColorBrush(Color.FromArgb(250,43,43,49)),BorderBrush=new SolidColorBrush(Color.FromArgb(170,120,120,135)),BorderThickness=new Thickness(0.75),IsHitTestVisible=false};
        var selectedIndex=_page*8+_selected;
        var selectedText=_selected>=0&&selectedIndex<_actions.Count&&_actions[selectedIndex]!=CadRadialMenuAction.None?Direct2dCad.Lang.CadUiText.Get(CadEnumLabelData.Key(_actions[selectedIndex])):Direct2dCad.Lang.CadUiText.Get("Cancel");
        if(_actions.Count>8) selectedText += $"\n{_page+1}/{(_actions.Count+7)/8}";
        center.Child=new TextBlock {Text=selectedText,Foreground=Brushes.White,FontSize=11,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,VerticalAlignment=global::Avalonia.Layout.VerticalAlignment.Center,Margin=new Thickness(5)};
        Canvas.SetLeft(center,88);Canvas.SetTop(center,88);_surface.Children.Add(center);
    }
    public void Complete(Point point) { Move(point); var index = _page * 8 + _selected; var action = _selected >= 0 && index < _actions.Count ? _actions[index] : CadRadialMenuAction.None; Close(); if (action != CadRadialMenuAction.None && command.CanExecute(action)) command.Execute(action); }
    public void Close() { _overlayHost?.Children.Remove(_overlay); _overlayHost=null; }
}

