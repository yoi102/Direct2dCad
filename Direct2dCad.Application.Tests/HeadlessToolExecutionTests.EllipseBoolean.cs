using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EllipticalNearestPointUsesTheNormalRatherThanRadialProjection(bool regionBoundary)
    {
        using var workspace = new HeadlessWorkspace();
        var document = workspace.CreateDocument("Ellipse projection").Session.CadEditor.Document;
        var ellipse = document.AddEllipse(default, 20, 10);
        var target = regionBoundary ? (CadEntity)document.AddRegion([
            new([Direct2dCad.Db.Geometry.CadPlanarPrimitive.EllipseArc(default, 20, 10, 0, 0, Math.PI * 2)])]) : ellipse;
        const double angle = .71;
        var x = 20 * Math.Cos(angle); var y = 10 * Math.Sin(angle);
        var nx = Math.Cos(angle) / 20; var ny = Math.Sin(angle) / 10;
        var normalLength = Math.Sqrt(nx * nx + ny * ny);
        var output = await Execute(new CadWorkspaceToolExecutor(workspace), "measure_geometry", new {
            operation = "nearest_point", entity_ids = new[] { target.Id.Value },
            point = new { x = x + 7 * nx / normalLength, y = y + 7 * ny / normalLength }
        });
        var data = output.GetProperty("result");
        Assert.Equal(x, data.GetProperty("projected_point").GetProperty("x").GetDouble(), 7);
        Assert.Equal(y, data.GetProperty("projected_point").GetProperty("y").GetDouble(), 7);
        Assert.Equal(7, data.GetProperty("distance_millimeters").GetDouble(), 7);
    }

    [Fact]
    public async Task EllipseBooleanToolReturnsExactCurveParametersAndSupportsUndoRedo()
    {
        using var workspace = new HeadlessWorkspace();
        var host = workspace.CreateDocument("Ellipse workflow");
        var document = host.Session.CadEditor.Document;
        var ellipse = document.AddEllipse(default, 20, 10); ellipse.SetRotation(.6);
        var circle = document.AddCircle(default, 3);
        var executor = new CadWorkspaceToolExecutor(workspace);
        var output = await Execute(executor, "boolean_regions", new {
            operation = "difference", entity_ids = new[] { ellipse.Id.Value, circle.Id.Value }, subject_entity_id = ellipse.Id.Value
        });
        var id = output.GetProperty("result").GetProperty("result").GetProperty("result_entity_id").GetInt64();
        var geometry = await Execute(executor, "get_entity_geometry", new { entity_id = id });
        var data = geometry.GetProperty("result").GetProperty("result").GetProperty("geometry");
        Assert.True(data.GetProperty("perimeter_approximate").GetBoolean());
        Assert.Equal(Math.PI * 191, data.GetProperty("area").GetDouble(), 7);
        var edges = data.GetProperty("contours").EnumerateArray().SelectMany(c => c.GetProperty("edges").EnumerateArray()).ToArray();
        var elliptical = edges.Where(e => e.GetProperty("kind").GetString() == "EllipseArc").ToArray();
        Assert.NotEmpty(elliptical);
        Assert.All(elliptical, e => {
            Assert.Equal(20, e.GetProperty("radius_x").GetDouble());
            Assert.Equal(10, e.GetProperty("radius_y").GetDouble());
            Assert.Equal(.6 * 180 / Math.PI, e.GetProperty("rotation_degrees").GetDouble(), 8);
        });
        await Execute(executor, "undo", new { });
        Assert.False(ellipse.IsErased); Assert.False(circle.IsErased); Assert.True(document.GetEntity(new(id)).IsErased);
        await Execute(executor, "redo", new { });
        Assert.True(ellipse.IsErased); Assert.True(circle.IsErased);
        Assert.IsType<CadRegion>(Assert.Single(document.Entities.Values, e => !e.IsErased));
    }
}
