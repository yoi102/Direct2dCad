using System.Text.Json;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;
using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using MessagePack;

namespace Direct2dCad.IO.Tests;

public sealed class EllipticalRegionExchangeTests
{
    [Fact]
    public async Task FileSnapshotAndVersionPreserveRotatedEllipticalHoleExactly()
    {
        var document = CadDocument.Create("elliptical hole");
        var region = document.AddRegion(new[] { (20d, 8d), (6d, 2d) }.Select(axes => new CadRegionContour([
            CadPlanarPrimitive.EllipseArc(new(12, -7), axes.Item1, axes.Item2, .73, 0, Math.PI * 2)])));
        var storage = new CadDocumentStorage(); var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            await storage.SaveAsync(document, path);
            Assert.Equal(2, storage.ReadSectionTable(path).Single(s => s.Kind == CadSectionKind.Regions).Version);
            var restored = Assert.IsType<CadRegion>((await storage.LoadAsync(path)).GetEntity(region.Id));
            Assert.Equal(region.Contours.SelectMany(c => c.Edges), restored.Contours.SelectMany(c => c.Edges));
            Assert.Equal(Math.PI * (160 - 12), restored.Area, 7);
            Assert.False(restored.Contains(new(12, -7)));
            var snapshot = await storage.CreateIndependentSnapshotAsync(document, new(() => true, _ => ValueTask.CompletedTask));
            region.ReplaceGeometry(region.Contours.Select(c => c.Transform(p => p + new CadVectorD(100, 0))));
            var immutable = Assert.IsType<CadRegion>(snapshot.GetEntity(region.Id));
            Assert.Equal(restored.Bounds, immutable.Bounds);
            Assert.Equal(restored.Contours.SelectMany(c => c.Edges), immutable.Contours.SelectMany(c => c.Edges));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacySixFieldEdgesMigrateWithoutChangingCircularGeometry()
    {
        // Historical v1 wire shape really omits keys 6 and 7.
        // Build the outer section with the actual entity layout, avoiding assumptions
        // about unrelated entity fields that have their own migration contract.
        var doc = CadDocument.Create("legacy");
        var region = doc.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, Math.PI * 2)])]);
        var storage = new CadDocumentStorage(); var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad");
        try
        {
            await storage.SaveAsync(doc, path);
            var current = storage.ReadSection<CadRegionsSection>(path, CadSectionKind.Regions);
            var wireJson = JsonNodeFromSection(current);
            var edge = wireJson[0]![0]![1]![0]![0]!.AsArray();
            edge.RemoveAt(7); edge.RemoveAt(6);
            var payload = MessagePackSerializer.ConvertFromJson(wireJson.ToJsonString());
            var entries = storage.ReadSectionTable(path);
            using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
            using (var writer = new BinaryWriter(stream))
            {
                var offset = stream.Length; stream.Position = offset; writer.Write(payload);
                stream.Position = 25 + entries.ToList().FindIndex(e => e.Kind == CadSectionKind.Regions) * 19 + 2;
                writer.Write(1); writer.Write((byte)CadCompressionKind.None); writer.Write(offset); writer.Write(payload.Length);
            }
            var loaded = await storage.LoadAsync(path);
            var result = Assert.IsType<CadRegion>(loaded.GetEntity(region.Id));
            Assert.All(result.Contours.SelectMany(c => c.Edges), e => Assert.False(e.IsEllipse));
            Assert.Equal(region.Area, result.Area, 9);
            Assert.Equal(region.Contours[0].Edges, result.Contours[0].Edges);
        }
        finally { File.Delete(path); }
    }

    private static System.Text.Json.Nodes.JsonNode JsonNodeFromSection(CadRegionsSection section) =>
        System.Text.Json.Nodes.JsonNode.Parse(MessagePackSerializer.ConvertToJson(MessagePackSerializer.Serialize(section)))!;

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task DxfEllipseEdgesKeepRadiiRotationDirectionAndSolidHole(bool swappedAxes, bool mirrored)
    {
        var document = CadDocument.Create("ellipse exchange");
        var fill = document.CreateSolidFillStyle("solid", CadColor.Green);
        var rx = swappedAxes ? 6d : 14d; var ry = swappedAxes ? 14d : 6d;
        var contours = new[] { 1d, .25 }.Select(scale => new CadRegionContour([
            CadPlanarPrimitive.EllipseArc(new(13, 9), rx * scale, ry * scale, .6, .31, Math.PI * 2)]));
        var region = document.AddRegion(mirrored ? contours.Select(c => c.Transform(p => new(-p.X, p.Y), true)) : contours, fillStyleId: fill);
        await AssertExport(document, region, $"ellipse-hole-{swappedAxes}-{mirrored}");
    }

    [Theory]
    [InlineData(CadBooleanOperation.Union)] [InlineData(CadBooleanOperation.Intersection)] [InlineData(CadBooleanOperation.Difference)]
    public async Task BooleanMixedBoundariesExportWithoutPolylineApproximation(CadBooleanOperation operation)
    {
        var document = CadDocument.Create("mixed ellipse Boolean");
        var ellipse = document.AddEllipse(default, 15, 8); ellipse.SetRotation(.43);
        var circle = document.AddCircle(new(9, 0), 7);
        var fill = document.CreateSolidFillStyle("solid", CadColor.Green);
        var region = document.AddRegion(CadRegionBoolean.Compute([ellipse, circle], operation), fillStyleId: fill);
        ellipse.Erase(); circle.Erase();
        Assert.Contains(region.Contours.SelectMany(c => c.Edges), p => p.IsEllipse);
        await AssertExport(document, region, $"mixed-{operation}");
    }

    private static async Task AssertExport(CadDocument document, CadRegion region, string name)
    {
        var evidence = Environment.GetEnvironmentVariable("DIRECT2DCAD_ELLIPSE_DXF_EVIDENCE");
        var directory = evidence ?? Path.Combine(Path.GetTempPath(), "ellipse-dxf-" + Guid.NewGuid());
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, name + ".dxf");
        try
        {
            var storage = new CadDxfStorage();
            Assert.Equal(1, storage.AnalyzeExport(document)["Region converted to exact boundary curves"]);
            await storage.ExportAsync(document, path, allowLoss: true, CadFileRevision.Capture(path));
            var text = await File.ReadAllTextAsync(path); Assert.Contains("ELLIPSE", text); Assert.DoesNotContain("LWPOLYLINE", text);
            var lines = text.Split('\n', StringSplitOptions.TrimEntries);
            var inHatch = false; var ellipseHatchEdges = 0;
            for (var i = 0; i + 1 < lines.Length; i += 2)
            {
                if (lines[i] == "0") inHatch = lines[i + 1] == "HATCH";
                else if (inHatch && lines[i] == "72" && lines[i + 1] == "3") ellipseHatchEdges++;
            }
            Assert.Equal(region.Contours.SelectMany(c => c.Edges).Where(e => e.IsEllipse)
                .Sum(e => Math.Abs(e.Sweep) >= Math.PI * 2 - 1e-10 ? 2 : 1), ellipseHatchEdges);
            var imported = await storage.ImportAsync(path);
            Assert.Single(imported.Unsupported); Assert.Equal(1, imported.Unsupported["HATCH"]);
            var edges = imported.Document.Entities.Values.SelectMany(CadPlanarCurves.Get).ToArray();
            var expected = region.Contours.SelectMany(c => c.Edges).ToArray();
            Assert.Equal(expected.Length, edges.Length);
            foreach (var edge in expected)
                foreach (var t in new[] { 0d, .17, .5, .81, 1d })
                    Assert.True(edges.Min(p => CadRegionGeometry.Distance(p, edge.At(t))) < 1e-7);
            foreach (var edge in edges)
                foreach (var t in new[] { 0d, .17, .5, .81, 1d })
                    Assert.True(expected.Min(p => CadRegionGeometry.Distance(p, edge.At(t))) < 1e-7);
            if (evidence is not null)
                await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(new {
                    region.Area, contours = region.Contours.Select(c => c.Edges.Select(e => new {
                        e.IsLine, e.IsEllipse, e.Start, e.End, e.Center, e.RadiusX, e.RadiusY, e.Rotation, e.StartAngle, e.Sweep
                    }).ToArray()).ToArray()
                }));
        }
        finally { if (evidence is null) Directory.Delete(directory, true); }
    }
}
