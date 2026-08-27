using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Direct2dCad.wpf.Services.Printing;

namespace Direct2dCad.wpf.Views.Dialogs;

public partial class CadPrintPreviewDialog
{
    private const double PreviewMaximumSide = 600.0;

    private readonly IReadOnlyList<CadPrinterChoice> _printers;

    internal CadPrintPreviewDialog(
        BitmapSource preview,
        string documentName,
        IReadOnlyList<CadPrinterChoice> printers,
        PageOrientation initialOrientation)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(printers);
        InitializeComponent();

        _printers = printers;
        DocumentNameText.Text = documentName;
        PreviewImage.Source = preview;

        OrientationCombo.SelectedValue = initialOrientation;
        DpiInput.Value = CadPrintService.DefaultRenderDpi;

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

    private void UpdatePreviewPageSize()
    {
        if (PaperSizeCombo.SelectedItem is not CadPaperSizeChoice paper ||
            OrientationCombo.SelectedValue is not PageOrientation orientation)
        {
            return;
        }

        var width = paper.Width;
        var height = paper.Height;
        if (orientation == PageOrientation.Landscape && height > width ||
            orientation == PageOrientation.Portrait && width > height)
        {
            (width, height) = (height, width);
        }

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
        if (PrinterCombo.SelectedItem is not CadPrinterChoice printer ||
            PaperSizeCombo.SelectedItem is not CadPaperSizeChoice paper ||
            OrientationCombo.SelectedValue is not PageOrientation orientation)
        {
            return;
        }

        Selection = new CadPrintPreviewSelection(
            printer.QueueName,
            paper.MediaSize,
            orientation,
            Math.Clamp((int)Math.Round(CopiesInput.Value ?? 1.0), 1, 999),
            Math.Clamp(
                (int)Math.Round(DpiInput.Value ?? CadPrintService.DefaultRenderDpi),
                CadPrintService.MinimumRenderDpi,
                CadPrintService.MaximumRenderDpi));
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
    int RenderDpi);
