using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.Lang;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class CadPrintPreviewDialog
{
    private const double PreviewMaximumSide = 600.0;

    private readonly IReadOnlyList<CadPrinterChoice> _printers;
    private readonly CadPrintRequest _request;
    private int _previewVersion;
    private bool _closed;
    private readonly SemaphoreSlim _previewGate=new(1,1);

    internal CadPrintPreviewDialog(
        ImageSource preview,
        string documentName,
        IReadOnlyList<CadPrinterChoice> printers,
        PageOrientation initialOrientation,
        CadPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(printers);
        _request=request;
        InitializeComponent();
        Closed+=(_,_)=>{_closed=true;_previewVersion++;};

        _printers = printers;
        DocumentNameText.Text = documentName;
        PreviewImage.Source = preview;

        OrientationCombo.SelectedValue = initialOrientation;
        DpiInput.Value = CadPrintService.DefaultRenderDpi;
        ScalingCombo.SelectedIndex=0;
        RangeCombo.Visibility=request.IsModelSpace?Visibility.Visible:Visibility.Collapsed;
        RangeLabel.Visibility=RangeCombo.Visibility;
        RangeCombo.SelectedIndex=0;

        PrinterCombo.ItemsSource = _printers;
        PrinterCombo.SelectedItem = _printers.FirstOrDefault(printer => printer.IsDefault) ?? _printers[0];
    }

    internal CadPrintPreviewSelection? Selection { get; private set; }

    private void PrinterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PrinterCombo.SelectedItem is not CadPrinterChoice printer)
            return;

        PaperSizeCombo.ItemsSource = printer.PaperSizes;
        PaperSizeCombo.SelectedItem = printer.PaperSizes.FirstOrDefault(paper => paper.IsDefault) ??
                                      printer.PaperSizes.FirstOrDefault();
        UpdatePreviewPageSize();
    }

    private void PaperSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdatePreviewPageSize();

    private void OrientationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdatePreviewPageSize();

    private async void UpdatePreviewPageSize()
    {
        if (PaperSizeCombo.SelectedItem is not CadPaperSizeChoice paper ||
            OrientationCombo.SelectedValue is not PageOrientation orientation)
        {
            return;
        }

        if(PrinterCombo.SelectedItem is not CadPrinterChoice printer || ScalingCombo is null || PercentInput is null || PrintButton is null)return;
        var version=++_previewVersion;Selection=null;PrintButton.IsEnabled=false;
        PercentInput.Visibility=ScalingCombo.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;
        try
        {
            await Task.Delay(100);
            if(_closed || version!=_previewVersion)return;
            var candidate=new CadPrintPreviewSelection(printer.QueueName,paper.MediaSize,orientation,
                Math.Clamp((int)Math.Round(CopiesInput.Value??1),1,999),Math.Clamp((int)Math.Round(DpiInput.Value??300),72,1200))
            {Scaling=(CadPaperScaling)Math.Max(0,ScalingCombo.SelectedIndex),Percent=PercentInput.Value??100,UseCurrentView=RangeCombo.SelectedIndex==1};
            await _previewGate.WaitAsync();
            CadValidatedPrintPreview result;
            try
            {
                if(_closed || version!=_previewVersion)return;
                result=await CadPrintService.ValidatePreviewAsync(_request,candidate);
            }
            finally{_previewGate.Release();}
            if(_closed || version!=_previewVersion)return;
            Selection=result.Selection;PreviewImage.Source=result.Image;PrintButton.IsEnabled=true;
            ClippingText.Text=CadUiText.Get(result.Selection.Placement!.IsClipped?"PrintClipped":"PrintInsideBounds");
            SetPageAspect(result.Selection.PageWidth,result.Selection.PageHeight);
        }
        catch(Exception ex){if(!_closed && version==_previewVersion)ClippingText.Text=ex.Message;}
    }

    private void OptionsChanged(object sender,RoutedEventArgs e)=>UpdatePreviewPageSize();
    private void SetPageAspect(double width,double height)
    {

        var aspect = width / Math.Max(height, double.Epsilon);
        if (aspect >= 1.0)
        {
            PreviewPageBorder.Width = PreviewMaximumSide;
            PreviewPageBorder.Height = PreviewMaximumSide / aspect;
        }
        else
        {
            PreviewPageBorder.Width = PreviewMaximumSide * aspect;
            PreviewPageBorder.Height = PreviewMaximumSide;
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Selection?.ValidatedTicket is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

}

internal sealed record CadPrinterChoice(
    string QueueName,
    string DisplayName,
    bool IsDefault,
    IReadOnlyList<CadPaperSizeChoice> PaperSizes)
{
    public override string ToString() => DisplayName;
}

internal sealed record CadPaperSizeChoice(
    PageMediaSize MediaSize,
    string DisplayName,
    double Width,
    double Height,
    bool IsDefault)
{
    public override string ToString() => DisplayName;
}

internal sealed record CadPrintPreviewSelection(
    string QueueName,
    PageMediaSize MediaSize,
    PageOrientation Orientation,
    int Copies,
    int RenderDpi)
{
    public CadPaperScaling Scaling {get;init;}=CadPaperScaling.ActualSize;
    public double Percent {get;init;}=100;
    public bool UseCurrentView {get;init;}
    public byte[]? ValidatedTicket {get;init;}
    public CadPrintPlacement? Placement {get;init;}
    public double PageWidth {get;init;}
    public double PageHeight {get;init;}
}
internal sealed record CadValidatedPrintPreview(CadPrintPreviewSelection Selection,ImageSource Image);
