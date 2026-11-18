using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
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

    [Fact]
    public void ReferenceFreeStructCollectionsAreSizedByElementSize()
    {
        const int count = 200_000;
        var payload = new StructPayload(
            Enumerable.Range(0, count).Select(i => new CadPointD(i, i)).ToArray(),
            Enumerable.Range(1, count).Select(i => new EntityId(i)).ToList());

        var estimate = CadCommandPayloadEstimate.Estimate(payload);

        // CadPointD is two doubles (16 bytes) and EntityId wraps a long (8 bytes).
        Assert.InRange(estimate, count * 16L + count * 8L, count * 16L + count * 8L * 2 + 4096);
    }

    [Fact]
    public void ReferencesInsideStructArraysAreStillFollowed()
    {
        var payload = new KeyValuePair<int, byte[]>[] { new(1, new byte[64 * 1024]), new(2, new byte[64 * 1024]) };

        Assert.True(CadCommandPayloadEstimate.Estimate(new ArrayHolder(payload)) >= 128 * 1024);
    }

    private sealed record StructPayload(CadPointD[] Points, List<EntityId> Ids);
    private sealed record ArrayHolder(object Items);
}
