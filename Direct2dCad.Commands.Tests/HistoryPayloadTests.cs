using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
namespace Direct2dCad.Commands.Tests;
public sealed class HistoryPayloadTests
{
    [Fact]public void EmbeddedPixelsHaveAMateriallyLargerEstimateThanALine()
    {
        var doc=CadDocument.Create("d");var line=doc.AddLine(default,new(1,0));var image=doc.AddImage(CadRectD.FromXYWH(0,0,10,10),256,256,1024,new byte[256*1024]);
        var a=new PasteEntitiesCommand(CadClipboardSnapshotFactory.Create(doc,[line.Id])!,default);var b=new PasteEntitiesCommand(CadClipboardSnapshotFactory.Create(doc,[image.Id])!,default);
        Assert.True(CadCommandPayloadEstimate.Estimate(b)>CadCommandPayloadEstimate.Estimate(a)+256*1024-1024);
    }
}
