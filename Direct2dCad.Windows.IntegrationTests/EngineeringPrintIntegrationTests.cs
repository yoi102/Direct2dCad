using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.wpf.Services.Printing;
using Direct2dCad.wpf.Views.Dialogs;

namespace Direct2dCad.Windows.IntegrationTests;
public sealed class EngineeringPrintIntegrationTests
{
    [Fact][Trait("Category","WindowsIntegration")]
    public async Task PdfDriverValidatesActualFitCustomAndModelRangeWithoutSubmittingJob()
    {
        var document=CadEngineeringTemplates.Create("A3-100");document.AddLine(new(-5000,0),new(5000,0));
        var layout=document.Layouts.Values.Single();var request=new CadPrintRequest("calibration",document,layout.PaperBounds,layout.Id);
        var dimension=document.AddDimension(new(Direct2dCad.Db.Data.Entities.CadDimensionKind.Aligned,
            [new(new(-5000,0)),new(new(5000,0))],new(0,-800),new(),100));
        Assert.Equal(2.5,dimension.Definition.Style.TextHeight*dimension.Definition.AnnotationScale*layout.Viewports.Single().Scale,10);
        var border=document.AddPolyline([new(5,5),new(415,5),new(415,292),new(5,292)],true);document.MoveEntityToBlock(border.Id,layout.PaperSpaceBlockId);
        var calibration=document.AddLine(new(20,20),new(120,20));document.MoveEntityToBlock(calibration.Id,layout.PaperSpaceBlockId);
        var text=document.AddText("100 mm / A3 / 1:100",new(20,24),2.5);document.MoveEntityToBlock(text.Id,layout.PaperSpaceBlockId);
        using var server=new LocalPrintServer();using var queue=server.GetPrintQueue("Microsoft Print to PDF");
        var selection=new CadPrintPreviewSelection(queue.Name,new PageMediaSize(PageMediaSizeName.ISOA4),PageOrientation.Landscape,1,300);
        var actual=await CadPrintService.ValidatePreviewAsync(request,selection);var fit=await CadPrintService.ValidatePreviewAsync(request,selection with {Scaling=CadPaperScaling.Fit});
        var custom=await CadPrintService.ValidatePreviewAsync(request,selection with{Scaling=CadPaperScaling.Custom,Percent=50});
        Assert.Equal(CadPrintPlacement.DipsPerMillimeter,actual.Selection.Placement!.Scale,10);Assert.True(actual.Selection.Placement.IsClipped);
        Assert.False(fit.Selection.Placement!.IsClipped);Assert.Equal(actual.Selection.Placement.Scale/2,custom.Selection.Placement!.Scale,10);
        Assert.True(actual.Image.IsFrozen);Assert.NotEmpty(actual.Selection.ValidatedTicket!);
        using var bytes=new MemoryStream(actual.Selection.ValidatedTicket!);var ticket=new PrintTicket(bytes);Assert.Equal(PageOrientation.Landscape,ticket.PageOrientation);
        var model=request with {IsModelSpace=true,PaperBounds=CadRectD.FromXYWH(0,0,100,20),CurrentViewBounds=CadRectD.FromXYWH(0,0,50,20)};
        var current=await CadPrintService.ValidatePreviewAsync(model,selection with {UseCurrentView=true});Assert.Equal(50*CadPrintPlacement.DipsPerMillimeter,current.Selection.Placement!.Output.Width,8);
        var evidence=Environment.GetEnvironmentVariable("DIRECT2DCAD_PRINT_EVIDENCE_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            await CadPrintService.RunOnStaThreadAsync(()=>
            {
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(actual.Image,new Rect(0,0,actual.Selection.PageWidth,actual.Selection.PageHeight));
                var image=new RenderTargetBitmap((int)Math.Ceiling(actual.Selection.PageWidth),(int)Math.Ceiling(actual.Selection.PageHeight),96,96,PixelFormats.Pbgra32);image.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(evidence,"a3-on-a4-clipping-preview.png"));encoder.Save(file);return true;
            });
            await File.WriteAllTextAsync(Path.Combine(evidence,"pdf-driver-preview.json"),JsonSerializer.Serialize(new{queue=queue.Name,submitted=false,
                actual=actual.Selection.Placement,fit=fit.Selection.Placement,custom=custom.Selection.Placement,currentView=current.Selection.Placement,
                viewportScale=layout.Viewports.Single().Scale,annotationPaperHeight=2.5},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
