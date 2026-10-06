using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;

namespace Direct2dCad.Benchmarks;

/// <summary>Deterministic exchange fixtures for the independent DXF parser, using real Boolean commands.</summary>
internal static class RegionDxfExportRunner
{
    public static void Run(string outputDirectory)
    {
        var directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        var rows = new List<object>(); var io = new CadDxfStorage();
        void Export(string name, CadDocument document, CadRegion region)
        {
            var path = Path.Combine(directory, name + ".dxf");
            io.Export(document, path, true, Direct2dCad.IO.CadFileRevision.Capture(path));
            rows.Add(new { file = name + ".dxf", unit_code = document.DocumentSettings.Unit == CadUnit.Inch ? 1 : 4,
                area_mm2 = region.Area, length_mm = region.Length, contour_count = region.Contours.Count,
                bounds_mm = new[] { region.Bounds.Left, region.Bounds.Top, region.Bounds.Right, region.Bounds.Bottom },
                block = region.OwnerBlockId == Direct2dCad.Db.BlockId.ModelSpace ? null : document.GetBlock(region.OwnerBlockId).Name,
                solid_fill = region.FillStyleId is not null,
                contours = region.Contours.Select(c => c.Edges.Select(e => new { line = e.IsLine,
                    start = new[] { e.Start.X, e.Start.Y }, end = new[] { e.End.X, e.End.Y },
                    center = new[] { e.Center.X, e.Center.Y }, radius = e.Radius, sweep = e.Sweep }).ToArray()).ToArray(),
                export_losses = io.AnalyzeExport(document) });
        }
        foreach (var operation in Enum.GetValues<CadBooleanOperation>())
        {
            var document = CadDocument.Create("circle-" + operation);
            var a = document.AddCircle(default, 10); var b = document.AddCircle(new(10, 0), 10);
            var command = new BooleanRegionsCommand([a.Id, b.Id], operation, a.Id); command.Execute(document);
            Export("circle-" + operation.ToString().ToLowerInvariant(), document, (CadRegion)document.GetEntity(command.ResultEntityId!.Value));
        }
        {
            var document = CadDocument.Create("split"); var fill = document.CreateSolidFillStyle("fill", CadColor.Green);
            var a = document.AddRectangle(CadRectD.FromLTRB(0, 0, 20, 10), fillStyleId: fill);
            var b = document.AddRectangle(CadRectD.FromLTRB(8, -5, 12, 15));
            var command = new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Difference, a.Id); command.Execute(document);
            Export("split-solid", document, (CadRegion)document.GetEntity(command.ResultEntityId!.Value));
        }
        foreach (var solid in new[] { false, true })
        {
            var document = CadDocument.Create("nested"); document.DocumentSettings.SetUnit(CadUnit.Inch);
            var a = document.AddCircle(default, 20); var b = document.AddCircle(default, 10); var c = document.AddCircle(default, 3);
            var subtract = new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Difference, a.Id); subtract.Execute(document);
            var add = new BooleanRegionsCommand([subtract.ResultEntityId!.Value, c.Id], CadBooleanOperation.Union); add.Execute(document);
            var region = (CadRegion)document.GetEntity(add.ResultEntityId!.Value);
            region.ReplaceGeometry(region.Contours.Select(contour => contour.Transform(p => new(-p.X + 80, p.Y - 35), true)));
            if (solid) region.SetFillStyleInternal(document.CreateSolidFillStyle("fill", CadColor.FromArgb(128, 0, 255, 0)));
            Export(solid ? "nested-solid-inch" : "nested-outline-inch", document, region);
        }
        {
            var document = CadDocument.Create("full-circle");
            var region = document.AddRegion(new[] { 10d, 3d }.Select((r, i) => new CadRegionContour([
                CadPlanarPrimitive.Arc(new(70, 20), r, .37, (i == 0 ? 1 : -1) * Math.Tau)])),
                fillStyleId: document.CreateSolidFillStyle("fill", CadColor.Green));
            Export("full-circle-solid", document, region);
        }
        {
            var document = CadDocument.Create("mixed"); var fill = document.CreateSolidFillStyle("fill", CadColor.Green);
            var a = document.AddRectangle(CadRectD.FromLTRB(0, 0, 20, 10), fillStyleId: fill); var b = document.AddCircle(new(10, 5), 8);
            var command = new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Intersection, a.Id); command.Execute(document);
            Export("mixed-solid", document, (CadRegion)document.GetEntity(command.ResultEntityId!.Value));
        }
        {
            var document = CadDocument.Create("block"); var layer = document.CreateLayer("region", CadColor.Blue, new(.3));
            var a = document.AddCircle(default, 10, layer); var b = document.AddCircle(default, 3, layer);
            var command = new BooleanRegionsCommand([a.Id, b.Id], CadBooleanOperation.Difference, a.Id); command.Execute(document);
            var region = (CadRegion)document.GetEntity(command.ResultEntityId!.Value);
            region.SetGraphicStyleInternal(document.CreateGraphicStyle("stroke", CadColor.Red, new(.5), Direct2dCad.Db.LineTypeId.Continuous));
            region.SetColorSource(CadColorSource.Explicit); region.SetLineWeight(new(.5)); region.SetVisible(false);
            region.SetStrokeStyle(region.StrokeStyle with { DashStyle = CadStrokeDashStyle.Dash });
            var block = document.CreateBlockDefinition("region-block", new(2, 1)); document.MoveEntityToBlock(region.Id, block);
            document.AddBlockReference(block, new(100, 0), rotationRadians: .4, scaleX: 2, scaleY: 1.5);
            Export("block-outline", document, region);
        }
        var report = new { recorded_at = DateTimeOffset.Now, fixtures = rows };
        File.WriteAllText(Path.Combine(directory, "region-source.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Exported {rows.Count} Boolean region DXF fixtures to {directory}.");
    }
}
