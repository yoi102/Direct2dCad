using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;
namespace Direct2dCad.IO.Tests;
public sealed class DxfExchangeTests
{
    [Fact]public async Task ExportByteRecordAndTextBudgetsLeaveNoFinalOrTemporaryFiles()
    {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(directory);var path=Path.Combine(directory,"budget.dxf");
        try
        {
            var doc=CadDocument.Create("budget");doc.AddLine(default,new(100,0));
            await Assert.ThrowsAsync<InvalidDataException>(()=>new CadDxfStorage{Limits=new(MaximumBytes:100)}.ExportAsync(doc,path));Assert.Empty(Directory.GetFiles(directory));
            await Assert.ThrowsAsync<InvalidDataException>(()=>new CadDxfStorage{Limits=new(MaximumEntities:1)}.ExportAsync(doc,path));Assert.Empty(Directory.GetFiles(directory));
            doc.AddText(new string('中',65537),default,2.5);await Assert.ThrowsAsync<InvalidDataException>(()=>new CadDxfStorage().ExportAsync(doc,path,true));Assert.Empty(Directory.GetFiles(directory));
        }
        finally{Directory.Delete(directory);}
    }
    [Fact]public async Task HiddenGeometrySurvivesAndLossesRequireAcknowledgment()
    {
        var doc=CadDocument.Create("hidden");var line=doc.AddLine(default,new(100,0));line.SetVisible(false);line.SetLocked(true);line.SetStrokeStyle(line.StrokeStyle with{StartCap=CadStrokeCap.Round});
        var io=new CadDxfStorage();Assert.Contains("Entity lock omitted",io.AnalyzeExport(doc).Keys);Assert.Contains("Stroke caps/joins omitted",io.AnalyzeExport(doc).Keys);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");
        try{await Assert.ThrowsAsync<InvalidOperationException>(()=>io.ExportAsync(doc,path));await io.ExportAsync(doc,path,true);Assert.False(Assert.Single((await io.ImportAsync(path)).Document.Entities.Values).IsVisible);}
        finally{File.Delete(path);}
    }
    [Fact]public async Task SupportedEntitiesUnitsLayersBlocksAndUnicodeRoundTrip()
    {
        var doc=CadDocument.Create("exchange");doc.DocumentSettings.SetUnit(CadUnit.Inch);var layer=doc.CreateLayer("轮廓",CadColor.Red,new(.25));
        doc.AddLine(new(25.4,50.8),new(100,50.8),layer);doc.AddCircle(new(40,20),10,layer);doc.AddArc(new(60,30),5,.4,-1.2,layer);
        doc.AddPolyline([new(0,0),new(20,0),new(20,10)],true,layer);doc.AddText("尺寸 中文 日本語",new(5,5),2.5,layerId:layer);
        var block=doc.CreateBlockDefinition("零件",new(5,5));var child=doc.AddLine(new(5,5),new(10,5));doc.MoveEntityToBlock(child.Id,block);doc.AddBlockReference(block,new(70,70),layer,rotationRadians:.3,scaleX:2,scaleY:2);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");var dxf=new CadDxfStorage();
        try
        {
            Assert.Equal(1,dxf.AnalyzeExport(doc)["TEXT font substituted"]);await dxf.ExportAsync(doc,path,true);var result=await dxf.ImportAsync(path);
            Assert.Equal(1,result.Unsupported["TEXT font substituted"]);Assert.Single(result.Unsupported);Assert.Equal(7,result.Imported);Assert.Equal(CadUnit.Inch,result.Document.DocumentSettings.Unit);
            var line=result.Document.GetEntitiesInBlock(BlockId.ModelSpace).OfType<CadLine>().Single();Assert.True(line.Start.NearEquals(new(25.4,50.8)));Assert.True(line.End.NearEquals(new(100,50.8)));
            Assert.Equal("尺寸 中文 日本語",result.Document.Entities.Values.OfType<CadText>().Single().Text);
            var reference=result.Document.Entities.Values.OfType<CadBlockReference>().Single();Assert.Equal(2,reference.ScaleX);Assert.Equal(.3,reference.RotationRadians,8);Assert.Equal("零件",result.Document.GetBlock(reference.DefinitionBlockId).Name);
            Assert.Equal(CadColor.Red,result.Document.Layers.Values.Single(l=>l.Name=="轮廓").Color);
        }
        finally{File.Delete(path);}
    }
    [Fact]public async Task MissingUnitsBudgetsTruncationCancellationAndOverwriteAreExplicit()
    {
        var doc=CadDocument.Create("d");doc.DocumentSettings.SetUnit(CadUnit.Unitless);doc.AddLine(default,new(10,0));
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");var dxf=new CadDxfStorage();
        try
        {
            await dxf.ExportAsync(doc,path,true);await Assert.ThrowsAsync<CadDxfUnitRequiredException>(()=>dxf.ImportAsync(path));Assert.Equal(254,(await dxf.ImportAsync(path,CadUnit.Inch)).Document.Entities.Values.OfType<CadLine>().Single().End.X);
            var revision=CadFileRevision.Capture(path);await Assert.ThrowsAsync<CadFileConflictException>(()=>dxf.ExportAsync(doc,path,true));Assert.True(revision.Matches(CadFileRevision.Capture(path)));
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>dxf.ExportAsync(doc,path,true,revision,cancellation.Token));Assert.True(revision.Matches(CadFileRevision.Capture(path)));
            await Assert.ThrowsAsync<InvalidDataException>(()=>new CadDxfStorage{Limits=new(MaximumBytes:10)}.ImportAsync(path,CadUnit.Millimeter));
            File.WriteAllText(path,"0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n");await Assert.ThrowsAsync<InvalidDataException>(()=>dxf.ImportAsync(path,CadUnit.Millimeter));
        }
        finally{File.Delete(path);}
    }
    [Fact]public async Task CircularBulgesPreserveExactArcGeometry()
    {
        var doc=CadDocument.Create("arcs");var pathEntity=doc.AddCompositePath(new(0,0),[new CadCompositeArcSegment(new(10,0),Math.PI),new CadCompositeLineSegment(new(0,0))],true);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");try{var io=new CadDxfStorage();await io.ExportAsync(doc,path);var copy=Assert.Single((await io.ImportAsync(path)).Document.Entities.Values.OfType<CadCompositePath>());Assert.Equal(pathEntity.Length,copy.Length,6);Assert.True(pathEntity.Bounds.NearEquals(copy.Bounds));}finally{File.Delete(path);}
    }
    [Fact]public async Task ExternalEzdxfSampleLoadsCircularEdges()
    {
        var dir=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(dir,"Direct2dCad.slnx")))dir=Directory.GetParent(dir)?.FullName??throw new IOException("Repository not found.");
        var result=await new CadDxfStorage().ImportAsync(Path.Combine(dir,"docs/samples/dxf/ezdxf-closed-loop-arcs.dxf"),CadUnit.Millimeter);
        Assert.True(result.Imported>0);Assert.Contains(result.Document.Entities.Values,e=>e is CadArc or CadCompositePath);
    }
}
