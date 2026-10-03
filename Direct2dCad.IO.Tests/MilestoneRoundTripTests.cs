using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using MessagePack;

namespace Direct2dCad.IO.Tests;

public sealed class MilestoneRoundTripTests
{
    private static void Rewrite<T>(CadDocumentStorage storage,string path,CadSectionKind kind,T payload)
    {
        var entries=storage.ReadSectionTable(path);var bytes=MessagePackSerializer.Serialize(payload);
        using var stream=File.Open(path,FileMode.Open,FileAccess.ReadWrite);using var writer=new BinaryWriter(stream);
        var offset=stream.Length;stream.Position=offset;writer.Write(bytes);
        stream.Position=25+entries.ToList().FindIndex(e=>e.Kind==kind)*19+6;writer.Write((byte)CadCompressionKind.None);writer.Write(offset);writer.Write(bytes.Length);
    }
    [Fact]public async Task OrientationAndSnappingRoundTripThroughIndependentSnapshotsAndFiles()
    {
        var d=CadDocument.Create("orientation");var ellipse=d.AddEllipse(new(10,20),30,5);ellipse.SetRotation(.37);var arc=d.AddEllipseArc(new(70,20),20,8,.2,1.7);arc.SetRotation(.41);
        var rectangle=d.AddRectangle(CadRectD.FromXYWH(0,50,30,10));rectangle.SetRotation(.52);
        d.ViewSettings.Snap=d.ViewSettings.Snap with { GridEnabled=true,ObjectsEnabled=false,PolarEnabled=true,Modes=CadObjectSnapModes.Tangent };
        var storage=new CadDocumentStorage();var snapshot=await storage.CreateIndependentSnapshotAsync(d,new(()=>true,ct=>ValueTask.CompletedTask));
        Assert.NotSame(d,snapshot);ellipse.SetRotation(.7);Assert.Equal(.37,((CadEllipse)snapshot.GetEntity(ellipse.Id)).RotationRadians);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try
        {
            await storage.SaveAsync(snapshot,path);var loaded=await storage.LoadAsync(path);
            Assert.Equal(snapshot.ViewSettings.Snap,loaded.ViewSettings.Snap);
            foreach(var entity in snapshot.Entities.Values) Assert.Equal(entity.Bounds,loaded.GetEntity(entity.Id).Bounds);
            Assert.Equal(rectangle.FrameBounds,((CadRectangle)loaded.GetEntity(rectangle.Id)).FrameBounds);
            Assert.Equal(arc.StartPoint,((CadEllipseArc)loaded.GetEntity(arc.Id)).StartPoint);
        }
        finally{File.Delete(path);}
    }
    [Fact]public async Task MissingKnownStyleIsRejectedBeforeReadOnlyDisplay()
    {
        var d=CadDocument.Create("missing reference");d.AddLine(default,new(10,0));var storage=new CadDocumentStorage();var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try{storage.Save(d,path);var lines=storage.ReadSection<CadLinesSection>(path,CadSectionKind.Lines);lines.Lines[0].GraphicStyleId=999999;Rewrite(storage,path,CadSectionKind.Lines,lines);await Assert.ThrowsAsync<InvalidDataException>(()=>storage.LoadAsync(path));}
        finally{File.Delete(path);}
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task CyclicAndDeepBlockGraphsAreRejectedBeforeMapping(bool cycle)
    {
        var d=CadDocument.Create("blocks");var child=d.CreateBlockDefinition("child",default);var parent=d.CreateBlockDefinition("parent",default);d.AddBlockReference(child,default,ownerBlockId:parent);d.AddBlockReference(parent,default);
        var storage=new CadDocumentStorage {LoadLimits=new(){MaximumBlockDepth=1}};var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try
        {
            storage.Save(d,path);
            if(cycle){var section=storage.ReadSection<CadBlockReferencesSection>(path,CadSectionKind.BlockReferences);section.BlockReferences[0].DefinitionBlockId=parent.Value;Rewrite(storage,path,CadSectionKind.BlockReferences,section);}
            await Assert.ThrowsAsync<InvalidDataException>(()=>storage.LoadAsync(path));
        }
        finally{File.Delete(path);}
    }
    [Fact]public async Task ImageDimensionsHaveAnIndependentPixelBudget()
    {
        var storage=new CadDocumentStorage {LoadLimits=new(){MaximumImagePixels=10}};var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try{storage.Save(CadDocument.Create("pixels"),path);Rewrite(storage,path,CadSectionKind.Images,new CadImagesSection {Images=[new(){PixelWidth=100,PixelHeight=100,Stride=400,Pixels=new byte[40000]}]});await Assert.ThrowsAsync<InvalidDataException>(()=>storage.LoadAsync(path));}
        finally{File.Delete(path);}
    }
    [Fact]public async Task CaptureRejectsStaleVersionAndCancellationWithoutChangingDestination()
    {
        var storage=new CadDocumentStorage();var d=CadDocument.Create("cancel");var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try
        {
            storage.Save(d,path);var revision=CadFileRevision.Capture(path);var current=true;
            await Assert.ThrowsAsync<CadSnapshotChangedException>(()=>storage.SaveAsync(d,path,new CadSnapshotCaptureOptions(()=>current,ct=>{current=false;return ValueTask.CompletedTask;})));
            using var cancel=new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>storage.SaveAsync(d,path,new CadSnapshotCaptureOptions(()=>true,ct=>{cancel.Cancel();return ValueTask.CompletedTask;}),cancel.Token));
            Assert.True(revision.Matches(CadFileRevision.Capture(path)));Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,Path.GetFileName(path)+"*.tmp"));
        }
        finally{File.Delete(path);}
    }
}
