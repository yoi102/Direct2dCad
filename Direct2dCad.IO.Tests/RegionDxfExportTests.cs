using System.Globalization;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;

namespace Direct2dCad.IO.Tests;

public sealed class RegionDxfExportTests
{
    [Theory]
    [InlineData(CadBooleanOperation.Union)]
    [InlineData(CadBooleanOperation.Intersection)]
    [InlineData(CadBooleanOperation.Difference)]
    public async Task BooleanCircularResultsKeepExactBoundariesAfterSourcesAreErased(CadBooleanOperation operation)
    {
        var document = CadDocument.Create("boolean");
        var a = document.AddCircle(default, 10); var b = document.AddCircle(new(10, 0), 10);
        var region = document.AddRegion(CadRegionBoolean.Compute([a, b], operation));
        a.Erase(); b.Erase();
        await WithExport(document, async (io, path) =>
        {
            var result = await io.ImportAsync(path);
            Assert.Empty(result.Unsupported);
            Assert.Equal(region.Contours.Count, result.Imported);
            AssertEquivalent(region, result.Document);
            Assert.All(result.Document.Entities.Values, e => Assert.IsType<CadCompositePath>(e));
            Assert.All(result.Document.Entities.Values.SelectMany(CadRegionBoolean.GetContours).SelectMany(c => c.Edges),
                edge => Assert.Equal(10, edge.Radius, 8));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullCirclesHolesNestedIslandsAndUnitsSurvive(bool mirrored)
    {
        var document = CadDocument.Create("nested"); document.DocumentSettings.SetUnit(CadUnit.Inch);
        var center = new CadPointD(80, -35);
        var contours = new[] { 20d, 10d, 3d }.Select((radius, i) => new CadRegionContour([
            CadPlanarPrimitive.Arc(center, radius, .37, (i == 1 ? -1 : 1) * 2 * Math.PI)]));
        var region = document.AddRegion(contours.Select(c => mirrored ? c.Transform(p => new(-p.X, p.Y), true) : c));
        await WithExport(document, async (io, path) =>
        {
            var result = await io.ImportAsync(path);
            Assert.Equal(CadUnit.Inch, result.Document.DocumentSettings.Unit);
            Assert.Equal(3, result.Imported);
            AssertEquivalent(region, result.Document);
            var material = result.Document.AddRegion(result.Document.Entities.Values.ToArray().SelectMany(CadRegionBoolean.GetContours));
            var c = mirrored ? new CadPointD(-80, -35) : center;
            Assert.True(material.Contains(c)); Assert.False(material.Contains(c + new CadVectorD(5, 0)));
            Assert.True(material.Contains(c + new CadVectorD(15, 0)));
            Assert.All(ReadRecords(path).Where(r => r.Type == "LWPOLYLINE"), r =>
            {
                Assert.Equal("2", Value(r, 90));
                Assert.All(r.Pairs.Where(p => p.Code == 42), p => Assert.InRange(Math.Abs(double.Parse(p.Value, CultureInfo.InvariantCulture)), .999999999, 1.000000001));
            });
        });
    }

    [Fact]
    public async Task SplitSubjectKeepsAllDisconnectedLineContours()
    {
        var document = CadDocument.Create("split");
        var a = document.AddRectangle(CadRectD.FromLTRB(0, 0, 20, 10)); var b = document.AddRectangle(CadRectD.FromLTRB(8, -5, 12, 15));
        var region = document.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Difference)); a.Erase(); b.Erase();
        await WithExport(document, async (io, path) =>
        {
            var result = await io.ImportAsync(path);
            Assert.Equal(2, result.Imported); Assert.All(result.Document.Entities.Values, e => Assert.IsType<CadPolyline>(e));
            AssertEquivalent(region, result.Document);
        });
    }

    [Fact]
    public async Task MixedLineAndCircularEdgesKeepTheirMaterial()
    {
        var document = CadDocument.Create("mixed");
        var a = document.AddRectangle(CadRectD.FromLTRB(0, 0, 20, 10)); var b = document.AddCircle(new(10, 5), 8);
        var region = document.AddRegion(CadRegionBoolean.Compute([a, b], CadBooleanOperation.Intersection)); a.Erase(); b.Erase();
        Assert.Contains(region.Contours.SelectMany(c => c.Edges), e => e.IsLine);
        Assert.Contains(region.Contours.SelectMany(c => c.Edges), e => !e.IsLine);
        await WithExport(document, async (io, path) => AssertEquivalent(region, (await io.ImportAsync(path)).Document));
    }

    [Theory]
    [InlineData(CadColorSource.ByLayer)]
    [InlineData(CadColorSource.ByBlock)]
    public async Task BoundaryRetainsInheritedColorSources(CadColorSource source)
    {
        var document = CadDocument.Create("color");
        var region = document.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, 2 * Math.PI)])]);
        region.SetColorSource(source);
        await WithExport(document, async (io, path) => Assert.Equal(source, Assert.Single((await io.ImportAsync(path)).Document.Entities.Values).ColorSource));
    }

    [Fact]
    public async Task BlockOwnershipStrokeColorVisibilityAndWeightAreKept()
    {
        var document = CadDocument.Create("style"); var layer = document.CreateLayer("region", CadColor.Blue, new(.3));
        var style = document.CreateGraphicStyle("stroke", CadColor.Red, new(.5), LineTypeId.Continuous);
        var region = document.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, 2 * Math.PI)])], layer, style);
        region.SetColorSource(CadColorSource.Explicit); region.SetLineWeight(new(.5)); region.SetVisible(false);
        region.SetStrokeStyle(region.StrokeStyle with { DashStyle = CadStrokeDashStyle.Dash });
        var block = document.CreateBlockDefinition("region-block", new(2, 1)); document.MoveEntityToBlock(region.Id, block);
        var reference = document.AddBlockReference(block, new(100, 0), rotationRadians: .4, scaleX: 2, scaleY: 1.5);
        document.RefreshBlockReferenceBounds();
        await WithExport(document, async (io, path) =>
        {
            var result = await io.ImportAsync(path); Assert.Empty(result.Unsupported);
            var insert = Assert.Single(result.Document.Entities.Values.OfType<CadBlockReference>());
            var copy = Assert.Single(result.Document.GetEntitiesInBlock(insert.DefinitionBlockId));
            Assert.Equal("region-block", result.Document.GetBlock(copy.OwnerBlockId).Name);
            Assert.Equal("region", result.Document.GetLayer(copy.LayerId).Name);
            Assert.False(copy.IsVisible); Assert.Equal(CadColorSource.Explicit, copy.ColorSource);
            Assert.Equal(.5, copy.LineWeight!.Value.Value); Assert.Equal(CadStrokeDashStyle.Dash, copy.StrokeStyle.DashStyle);
            Assert.Equal(CadColor.Red, result.Document.GetStyle<Direct2dCad.Db.Data.Styles.CadGraphicStyle>(((CadCompositePath)copy).GraphicStyleId!.Value).StrokeColor);
            Assert.True(reference.Bounds.NearEquals(insert.Bounds));
        });
    }

    [Fact]
    public async Task SolidFillUsesOneEvenOddHatchWithEveryExactBoundaryAndIndependentColor()
    {
        var document = CadDocument.Create("fill"); var fill = document.CreateSolidFillStyle("fill", CadColor.FromArgb(128, 0, 255, 0));
        var region = document.AddRegion(new[] { 20d, 10d, 3d }.Select(r => new CadRegionContour([CadPlanarPrimitive.Arc(default, r, 0, 2 * Math.PI)])), fillStyleId: fill);
        await WithExport(document, async (io, path) =>
        {
            Assert.DoesNotContain("Fill omitted", io.AnalyzeExport(document).Keys);
            var records = ReadRecords(path); var hatch = Assert.Single(records, r => r.Type == "HATCH");
            Assert.Equal("SOLID", Value(hatch, 2)); Assert.Equal("1", Value(hatch, 70)); Assert.Equal("0", Value(hatch, 71));
            Assert.Equal("3", Value(hatch, 91)); Assert.Equal("0", Value(hatch, 75));
            Assert.Equal("65280", Value(hatch, 420)); Assert.Equal((0x02000000 | 127).ToString(), Value(hatch, 440));
            Assert.Equal(3, hatch.Pairs.Count(p => p.Code == 92 && p.Value == "2"));
            Assert.Equal(6, hatch.Pairs.Count(p => p.Code == 42));
            Assert.Equal(3, records.Count(r => r.Type == "LWPOLYLINE"));
            Assert.All(records.Where(r => r.Type == "LWPOLYLINE"), r => Assert.Equal("256", Value(r, 62)));
            var result = await io.ImportAsync(path);
            Assert.Equal(1, result.Unsupported["HATCH"]); // HATCH import is outside this export contract; its boundaries remain usable.
            AssertEquivalent(region, result.Document);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutlineOnlyDoesNotAcquireFillAndUnsupportedFillIsReported(bool pattern)
    {
        var document = CadDocument.Create("outline");
        var region = document.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, 2 * Math.PI)])]);
        await WithExport(document, (_, path) => { Assert.DoesNotContain(ReadRecords(path), r => r.Type == "HATCH"); return Task.CompletedTask; });
        var unsupportedFill = pattern
            ? document.CreateHatchFillStyle("pattern", document.CreateHatchPattern("lines", CadHatchPatternLines.Horizontal()), CadColor.Red)
            : document.CreateGradientFillStyle("gradient", CadGradientKind.Linear, [new(0, CadColor.Red), new(1, CadColor.Blue)]);
        region.SetFillStyleInternal(unsupportedFill);
        await WithExport(document, (io, path) =>
        {
            Assert.Equal(1, io.AnalyzeExport(document)["Fill omitted"]);
            Assert.DoesNotContain(ReadRecords(path), r => r.Type == "HATCH");
            Assert.Single(ReadRecords(path), r => r.Type == "LWPOLYLINE"); return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ConversionLossGuardAndExpandedGroupBudgetKeepExistingDestinationUntouched()
    {
        var document = CadDocument.Create("budget"); document.AddRegion([new([CadPlanarPrimitive.Arc(default, 10, 0, 2 * Math.PI)])]);
        var io = new CadDxfStorage(); Assert.Equal(1, io.AnalyzeExport(document)["Region converted to boundary polylines"]);
        Assert.DoesNotContain(nameof(CadRegion), io.AnalyzeExport(document).Keys);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "region.dxf");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => io.ExportAsync(document, path)); Assert.Empty(Directory.GetFiles(directory));
            await io.ExportAsync(document, path, true); var groups = File.ReadAllLines(path).Length / 2;
            File.WriteAllText(path, "existing"); var revision = CadFileRevision.Capture(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => new CadDxfStorage { Limits = new(MaximumGroups: groups - 4) }.ExportAsync(document, path, true, revision));
            Assert.True(revision.Matches(CadFileRevision.Capture(path))); Assert.Single(Directory.GetFiles(directory));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => io.ExportAsync(document, path, true, revision, cancel.Token));
            Assert.True(revision.Matches(CadFileRevision.Capture(path))); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task WithExport(CadDocument document, Func<CadDxfStorage, string, Task> check)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try { var io = new CadDxfStorage(); await io.ExportAsync(document, path, true); await check(io, path); }
        finally { File.Delete(path); }
    }

    private static void AssertEquivalent(CadRegion source, CadDocument document)
    {
        var curves = document.Entities.Values.OfType<Curve>().ToArray();
        var contours = curves.SelectMany(CadRegionBoolean.GetContours).ToArray();
        Assert.Equal(source.Contours.Count, contours.Length);
        var material = CadDocument.Create("material").AddRegion(contours);
        Assert.Equal(source.Area, material.Area, 7); Assert.Equal(source.Length, material.Length, 7);
        Assert.True(source.Bounds.NearEquals(material.Bounds));
        var bounds = source.Bounds;
        for (var x = 1; x < 17; x++) for (var y = 1; y < 17; y++)
        {
            var point = new CadPointD(bounds.Left + bounds.Width * x / 17, bounds.Top + bounds.Height * y / 17);
            Assert.Equal(source.Contains(point), material.Contains(point));
        }
    }

    private sealed record DxfRecord(string Type, List<(int Code, string Value)> Pairs);
    private static List<DxfRecord> ReadRecords(string path)
    {
        var lines = File.ReadAllLines(path); var records = new List<DxfRecord>(); DxfRecord? current = null;
        for (var i = 0; i < lines.Length; i += 2)
        {
            var code = int.Parse(lines[i], CultureInfo.InvariantCulture); var value = lines[i + 1];
            if (code == 0) { current = new(value, []); records.Add(current); }
            else current?.Pairs.Add((code, value));
        }
        return records;
    }
    private static string Value(DxfRecord record, int code) => record.Pairs.First(p => p.Code == code).Value;
}
