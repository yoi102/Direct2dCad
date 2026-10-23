using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;

namespace Direct2dCad.IO.Tests;

public sealed class NurbsExchangeTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeAndDxfPreserveExactNurbsDefinition(bool periodic)
    {
        var doc = CadDocument.Create("NURBS"); doc.DocumentSettings.SetUnit(CadUnit.Inch);
        var curve = periodic ? CadNurbsCurve.CreateUniform([new(0,0),new(10,0),new(10,10),new(0,10)], 3, [1,2,3,1], true)
            : new CadNurbsCurve(2, [new(25.4,0),new(25.4,25.4),new(0,25.4)], [2,2,2,7,7,7], [1,Math.Sqrt(.5),1]);
        var spline = doc.AddSpline(new CadSplineGeometry(curve, periodic));
        foreach (var extension in new[] { ".d2cad", ".dxf" })
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
            try
            {
                CadDocument loaded;
                if (extension == ".d2cad") { var storage = new CadDocumentStorage(); await storage.SaveAsync(doc,path); loaded = await storage.LoadAsync(path); }
                else { var storage = new CadDxfStorage(); Assert.Empty(storage.AnalyzeExport(doc)); await storage.ExportAsync(doc,path); loaded = (await storage.ImportAsync(path)).Document; }
                var copy = Assert.Single(loaded.Entities.Values.OfType<CadSpline>());
                Assert.Equal(spline.Degree, copy.Degree); Assert.Equal(spline.Knots, copy.Knots); Assert.Equal(spline.Weights, copy.Weights);
                Assert.Equal(periodic,copy.Nurbs.IsPeriodic); Assert.Empty(copy.FitPoints);
                for (var i=0;i<=100;i++) Assert.InRange(spline.Nurbs.EvaluateNormalized(i/100.0).DistanceTo(copy.Nurbs.EvaluateNormalized(i/100.0)),0,1e-10);
            }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public async Task LegacyFitOnlyPayloadMigratesWithoutChangingShape()
    {
        var doc=CadDocument.Create("legacy"); var source=doc.AddSpline([new(0,0),new(10,20),new(30,0)],true);
        var storage=new CadDocumentStorage(); var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");
        try
        {
            storage.Save(doc,path);
            var section=storage.ReadSection<CadSplinesSection>(path,CadSectionKind.Splines);
            Assert.NotNull(section.Splines[0].Nurbs);
            section.Splines[0].Nurbs=null;
            var entries=storage.ReadSectionTable(path);
            var bytes=MessagePack.MessagePackSerializer.Serialize(section);
            using(var stream=File.Open(path,FileMode.Open,FileAccess.ReadWrite))
            using(var writer=new BinaryWriter(stream))
            {
                var offset=stream.Length;stream.Position=offset;writer.Write(bytes);
                stream.Position=25+entries.ToList().FindIndex(e=>e.Kind==CadSectionKind.Splines)*19+2;
                writer.Write(1);writer.Write((byte)CadCompressionKind.None);writer.Write(offset);writer.Write(bytes.Length);
            }
            var copy=Assert.Single((await storage.LoadAsync(path)).Entities.Values.OfType<CadSpline>());
            Assert.Equal(source.FitPoints,copy.FitPoints);
            for(var i=0;i<=100;i++) Assert.InRange(source.Nurbs.EvaluateNormalized(i/100.0).DistanceTo(copy.Nurbs.EvaluateNormalized(i/100.0)),0,1e-10);
        }
        finally { File.Delete(path); }
    }
}
