using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadMultiPointDrawingPreviewBuilderTests
{
    [Fact]
    public void PolygonPreview_StaysClosedAndUsesDashedGuidesForBothUncommittedEdges()
    {
        var document = CadDocument.Create("Polygon preview");
        var fillStyleId = document.CreateSolidFillStyle("Preview fill", CadColor.FromArgb(64, 0, 255, 0));
        var builder = CreateBuilder(document, fillStyleId);
        var points = new[]
        {
            new CadPointD(0, 0),
            new CadPointD(10, 0),
            new CadPointD(10, 10)
        };
        var mouse = new CadPointD(4, 12);
        var items = new List<CadTransientItem>();

        builder.AddPolygonPreview(items, points, mouse);

        var fillPreview = Assert.IsType<CadTransientPolyline>(items[0]);
        Assert.True(fillPreview.Closed);
        Assert.Equal(new[] { points[0], points[1], points[2], mouse }, fillPreview.Points);
        Assert.True(fillPreview.Style.StrokeColor.IsTransparent);
        Assert.Equal(CadColor.FromArgb(64, 0, 255, 0), fillPreview.Style.FillColor!.Value);

        var confirmedOutline = Assert.IsType<CadTransientPolyline>(items[1]);
        Assert.False(confirmedOutline.Closed);
        Assert.Equal(points, confirmedOutline.Points);
        Assert.Equal(CadTransientLinePattern.Solid, confirmedOutline.Style.LinePattern);
        Assert.Null(confirmedOutline.Style.FillColor);

        var nextEdge = Assert.IsType<CadTransientLine>(items[2]);
        Assert.Equal(points[^1], nextEdge.Start);
        Assert.Equal(mouse, nextEdge.End);
        Assert.Equal(CadTransientLinePattern.Dash, nextEdge.Style.LinePattern);

        var closingEdge = Assert.IsType<CadTransientLine>(items[3]);
        Assert.Equal(mouse, closingEdge.Start);
        Assert.Equal(points[0], closingEdge.End);
        Assert.Equal(CadTransientLinePattern.Dash, closingEdge.Style.LinePattern);
    }

    [Fact]
    public void PolygonPreview_WhenCursorIsNearFirstPoint_ShowsClosedPolygonWithoutGuide()
    {
        var builder = CreateBuilder(out _);
        var points = new[]
        {
            new CadPointD(0, 0),
            new CadPointD(10, 0),
            new CadPointD(10, 10)
        };
        var items = new List<CadTransientItem>();

        builder.AddPolygonPreview(items, points, new CadPointD(0.25, 0));

        var polygon = Assert.IsType<CadTransientPolyline>(Assert.Single(items));
        Assert.True(polygon.Closed);
        Assert.Equal(points, polygon.Points);
        Assert.Equal(CadTransientLinePattern.Solid, polygon.Style.LinePattern);
    }

    private static CadMultiPointDrawingPreviewBuilder CreateBuilder(out CadDocument document)
    {
        document = CadDocument.Create("Polygon preview");
        return CreateBuilder(document);
    }

    private static CadMultiPointDrawingPreviewBuilder CreateBuilder(CadDocument document, StyleId? polygonFillStyleId = null)
    {
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(0, 1000));
        var defaults = new CadDrawingDefaultsViewModel
        {
            PolygonUseLayerColor = false,
            PolygonStrokeColor = CadColor.Green,
            PolygonUseLayerLineWeight = false,
            PolygonLineWeight = 2.0,
            PolygonFillStyleId = polygonFillStyleId
        };
        var styles = new CadPreviewStyleService(document, CadUserSettings.CreateDefault());
        var resolver = new CadDrawingStyleResolver(
            document,
            document.GetLayer(LayerId.Default),
            defaults,
            styles);
        return new CadMultiPointDrawingPreviewBuilder(document, viewport, resolver);
    }
}
