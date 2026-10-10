using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.ViewModels.Services.Platform.Printing;

namespace Direct2dCad.Avalonia.Views;

internal sealed class WindowsPrintPreview : IDisposable
{
    public Window Window { get; }
    public WindowsPrinterSettings Printer { get; private set; } = null!;
    public CadRectD Bounds { get; private set; }
    public CadRectD Printable { get; private set; }
    public double PageWidth { get; private set; }
    public double PageHeight { get; private set; }
    public CadPaperScaling Scaling => (CadPaperScaling)Math.Max(0, _scale.SelectedIndex);
    public double Percent => (double)(_percent.Value ?? 100);
    public int Dpi => (int)(_dpi.Value ?? 300);
    private readonly CadPrintRequest _request;
    private readonly ComboBox _printers = new(), _paper = new(), _orientation = new(), _scale = new(), _range = new();
    private readonly CadNumericUpDown _copies = new() { Minimum = 1, Maximum = 999, Value = 1 }, _dpi = new() { Minimum = 72, Maximum = 1200, Increment = 50, Value = 300 }, _percent = new() { Minimum = .01m, Maximum = 10000, Value = 100, FormatString = "0.##'%'" };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Button _print = new() { MinWidth = 112, IsDefault = true, IsEnabled = false };
    private Bitmap? _bitmap;
    private bool _syncing, _disposed;
    private static string T(string key) => Direct2dCad.Lang.CadUiText.Get(key);
    internal WindowsPrintPreview(CadPrintRequest request)
    {
        _request = request;
        var names = WindowsPrinterSettings.Printers(); if (names.Count == 0) throw new IOException(T("NoPrintersAvailable"));
        Window = new Window { Title = T("PrintPreview"), Width = 1100, Height = 780, MinWidth = 820, MinHeight = 580, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { Margin = new Thickness(18), RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        var heading = new StackPanel { Spacing = 5, Margin = new Thickness(2,0,2,14) };
        heading.Children.Add(new TextBlock { Text = request.DocumentName, FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        heading.Children.Add(new TextBlock { Text = T("PrintPreviewDescription"), Opacity = .72, TextWrapping = TextWrapping.Wrap }); root.Children.Add(heading);
        var workspace = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("280,16,*") }; Grid.SetRow(workspace,1); root.Children.Add(workspace);
        var options = new StackPanel { Margin = new Thickness(0,0,40,0) }; options.Children.Add(new TextBlock { Text = T("PrinterSettings"), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0,0,0,18) });
        void Option(string key, Control control, string id) { options.Children.Add(new TextBlock { Text = T(key), Margin = new Thickness(0,0,0,5) }); control.Margin = new Thickness(0,0,0,16); control.MinHeight = 32; global::Avalonia.Automation.AutomationProperties.SetAutomationId(control,id); options.Children.Add(control); }
        _printers.ItemsSource = names; _orientation.ItemsSource = new[] { T("Portrait"), T("Landscape") };
        _scale.ItemsSource = new[] { T("PrintActualSize"), T("PrintFit"), T("PrintCustom") }; _scale.SelectedIndex = 0;
        _range.ItemsSource = new[] { T("PrintExtents"), T("PrintCurrentView") }; _range.SelectedIndex = 0;
        Option("Printer",_printers,"PrintPreviewPrinterCombo"); Option("PaperSize",_paper,"PrintPreviewPaperSizeCombo"); Option("Orientation",_orientation,"PrintPreviewOrientationCombo"); Option("Copies",_copies,"PrintPreviewCopiesInput"); Option("PaperScaling",_scale,"PrintScalingCombo"); options.Children.Add(_percent);
        if (request.IsModelSpace) Option("PrintRange",_range,"PrintRangeCombo");
        _message.Margin = new Thickness(0,10); options.Children.Add(_message);
        var details = new StackPanel(); details.Children.Add(new TextBlock { Text = T("PrintResolution") }); details.Children.Add(_dpi);
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(_dpi,"PrintPreviewDpiInput"); options.Children.Add(new Expander { Header = T("ViewDetails"), Content = details, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var driver = new Button { Content = T("PrinterSettings"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0,10,0,0) }; options.Children.Add(driver);
        options.Children.Add(new TextBlock { Text = T("PrintPreviewPrinterNote"), TextWrapping = TextWrapping.Wrap, Opacity = .68, Margin = new Thickness(0,20,0,0) });
        var optionsScroll = new ScrollViewer { Content = options, AllowAutoHide=false };
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(optionsScroll,"PrintOptionsScrollViewer");
        var optionsBorder = new Border { Padding = new Thickness(18), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = optionsScroll }; optionsBorder.Bind(Border.BorderBrushProperty,optionsBorder.GetResourceObservable("MaterialBodyBrush")); workspace.Children.Add(optionsBorder);
        var imageBorder = new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _image };
        var viewbox = new Viewbox { Child = imageBorder, Stretch = Stretch.Uniform };
        var canvas = new Border { Background = new SolidColorBrush(Color.Parse("#25272B")), Padding = new Thickness(24), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = viewbox }; Grid.SetColumn(canvas,2); workspace.Children.Add(canvas);
        var cancel = new Button { Content = T("Cancel"), MinWidth = 88, IsCancel = true }; _print.Content = T("Print");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0,16,0,0) }; actions.Children.Add(cancel); actions.Children.Add(_print); Grid.SetRow(actions,2); root.Children.Add(actions);
        Window.Content = CadWindowChrome.Wrap(Window,root);
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(Window,"PrintPreviewDialog"); global::Avalonia.Automation.AutomationProperties.SetAutomationId(_image,"PrintPreviewImage"); global::Avalonia.Automation.AutomationProperties.SetAutomationId(_print,"PrintFromPreviewButton");
        cancel.Click += (_,_) => Window.Close(false); _print.Click += (_,_) => { if (_print.IsEnabled) Window.Close(true); };
        _printers.SelectionChanged += (_,_) =>
        {
            if (_syncing || _printers.SelectedItem is not string name) return;
            try { var next = new WindowsPrinterSettings(name); Printer?.Dispose(); Printer = next; SynchronizeDriver(); Update(); }
            catch(Exception ex) { _print.IsEnabled = false; _message.Text = ex.Message; }
        };
        foreach (var combo in new[] { _paper, _orientation, _scale, _range }) combo.SelectionChanged += (_,_) => Update();
        foreach (var input in new[] { _copies, _dpi, _percent }) input.ValueChanged += (_,_) => Update();
        driver.Click += (_,_) => { try { if (Printer.ShowDriverSettings(Window.TryGetPlatformHandle()?.Handle ?? App.OwnerHandle)) { SynchronizeDriver(); Update(); } } catch(Exception ex) { _print.IsEnabled = false; _message.Text = ex.Message; } };
        _printers.SelectedItem = names.FirstOrDefault(name => string.Equals(name,WindowsPrinterSettings.DefaultPrinter(),StringComparison.OrdinalIgnoreCase)) ?? names[0];
        if (Printer is null) throw new IOException(_message.Text);
    }
    private void SynchronizeDriver()
    {
        _syncing = true;
        try { _paper.ItemsSource = Printer.Papers; _paper.SelectedItem = Printer.Papers.FirstOrDefault(p => p.Id == Printer.PaperId) ?? Printer.Papers.FirstOrDefault(); _orientation.SelectedIndex = Printer.Landscape ? 1 : 0; _copies.Value = Printer.Copies; }
        finally { _syncing = false; }
    }
    private void Update()
    {
        if (_syncing || _disposed || Printer is null || _paper.SelectedItem is not WindowsPaper paper) return;
        _print.IsEnabled = false; _percent.IsVisible = Scaling == CadPaperScaling.Custom;
        nint dc = 0;
        try
        {
            Printer.Apply(paper.Id, _orientation.SelectedIndex == 1, (int)(_copies.Value ?? 1), Dpi); dc = Printer.CreateContext();
            var dx = GetDeviceCaps(dc,88); var dy = GetDeviceCaps(dc,90); if(dx<=0 || dy<=0) throw new IOException("Invalid printer resolution.");
            PageWidth = GetDeviceCaps(dc,110)*96d/dx; PageHeight = GetDeviceCaps(dc,111)*96d/dy;
            Printable = CadRectD.FromXYWH(GetDeviceCaps(dc,112)*96d/dx,GetDeviceCaps(dc,113)*96d/dy,GetDeviceCaps(dc,8)*96d/dx,GetDeviceCaps(dc,10)*96d/dy);
            if (PageWidth <= 0 || PageHeight <= 0 || Printable.IsEmpty) throw new IOException("Invalid printable page.");
            Bounds = _request.IsModelSpace && _range.SelectedIndex == 1 ? _request.CurrentViewBounds ?? _request.PaperBounds : _request.PaperBounds;
            var placement = CadPrintPlacement.Calculate(Bounds,Printable,Scaling,Percent);
            var ratio = 900/Math.Max(PageWidth,PageHeight); var width = Math.Max(1,(int)Math.Round(PageWidth*ratio)); var height = Math.Max(1,(int)Math.Round(PageHeight*ratio));
            var viewport = new CadViewport(); viewport.SetSize(width,height); viewport.SetView(placement.Scale*ratio,new CadPointD((placement.Output.MinX-Bounds.MinX*placement.Scale)*ratio,(placement.Output.MinY+Bounds.MaxY*placement.Scale)*ratio));
            var options = new CadRenderOptions { ActiveLayoutId = _request.IsModelSpace ? null : _request.ActiveLayoutId, DrawGrid = false, DrawOrigin = false, DrawGripHandles = false, DrawLayoutGuides = false, KeepStrokeWidthScreenConstant = false, IsLevelOfDetailEnabled = false };
            var frame = WindowsPrintService.RenderPage(_request,viewport,options,width,height);
            var left=Math.Clamp((int)Math.Ceiling(Printable.MinX*ratio),0,width-1); var right=Math.Clamp((int)Math.Floor(Printable.MaxX*ratio),left,width-1);
            var top=Math.Clamp((int)Math.Ceiling(Printable.MinY*ratio),0,height-1); var bottom=Math.Clamp((int)Math.Floor(Printable.MaxY*ratio),top,height-1);
            for(var y=0;y<height;y++) for(var x=0;x<width;x++) { var offset=y*frame.Stride+x*4; if(x<left||x>right||y<top||y>bottom) frame.Pixels.AsSpan(offset,4).Fill(255); else if(x==left||x==right||y==top||y==bottom) {frame.Pixels[offset]=60;frame.Pixels[offset+1]=60;frame.Pixels[offset+2]=220;frame.Pixels[offset+3]=255;} }
            var next = new Bitmap(new MemoryStream(ImageImportService.Png(frame.Pixels,width,height,frame.Stride))); _image.Source = next; _bitmap?.Dispose(); _bitmap = next;
            _image.Width = width; _image.Height = height;
            var clipped = placement.Output.MinX<Printable.MinX-.01 || placement.Output.MaxX>Printable.MaxX+.01 || placement.Output.MinY<Printable.MinY-.01 || placement.Output.MaxY>Printable.MaxY+.01;
            _message.Text = T(clipped ? "PrintClipped" : "PrintInsideBounds"); _print.IsEnabled = true;
        }
        catch(Exception ex) { _message.Text = ex.Message; }
        finally { if(dc!=0) DeleteDC(dc); }
    }
    public void Dispose() { if(_disposed)return; _disposed=true; _image.Source=null; _bitmap?.Dispose(); Printer?.Dispose(); }
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc,int index);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}


