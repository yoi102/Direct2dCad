using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Snapping;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadSnapInteractionServiceTests
{
    [Fact]
    public void SnapWorld_UsesOriginAndIndependentMinorSpacing()
    {
        var document = CadDocument.Create("Grid snap");
        document.ViewSettings.Origin.Position = new CadPointD(1, -2);
        document.ViewSettings.Grid.SpacingX = 20;
        document.ViewSettings.Grid.SpacingY = 30;
        document.ViewSettings.Grid.MinorSpacingX = 2;
        document.ViewSettings.Grid.MinorSpacingY = 5;
        var viewport = CreateViewport();
        var service = new CadSnapInteractionService(document, viewport);

        var snapped = service.SnapWorld(new CadPointD(4.1, 5.4));

        Assert.Equal(new CadPointD(5, 3), snapped);
    }

    [Theory]
    [InlineData(CadSnapMarkerType.None, 0)]
    [InlineData(CadSnapMarkerType.Cross, 2)]
    [InlineData(CadSnapMarkerType.X, 2)]
    [InlineData(CadSnapMarkerType.Square, 1)]
    [InlineData((CadSnapMarkerType)99, 2)]
    public void AddSnapMarker_CreatesExpectedMarkerShape(
        CadSnapMarkerType markerType,
        int expectedItemCount)
    {
        var document = CadDocument.Create("Snap marker");
        document.ViewSettings.Grid.SnapMarkerType = markerType;
        var service = new CadSnapInteractionService(document, CreateViewport());
        var items = new List<CadTransientItem>();

        service.AddSnapMarker(
            items,
            new CadPointD(10.25, -4.75),
            new CadPointD(10, -5));

        Assert.Equal(expectedItemCount, items.Count);
        if (markerType == CadSnapMarkerType.Square)
            Assert.IsType<CadTransientRectangle>(Assert.Single(items));
        else if (markerType == CadSnapMarkerType.X || markerType == CadSnapMarkerType.Cross || (int)markerType == 99)
            Assert.All(items, item => Assert.IsType<CadTransientLine>(item));
    }

    [Fact]
    public void AddSnapMarker_DoesNothingWhenPointIsAlreadySnapped()
    {
        var document = CadDocument.Create("Snap marker");
        var service = new CadSnapInteractionService(document, CreateViewport());
        var items = new List<CadTransientItem>();

        service.AddSnapMarker(items, new CadPointD(10, -5), new CadPointD(10, -5));

        Assert.Empty(items);
    }

    [Fact]
    public void InfiniteCross_CreatesViewportIndependentTransientMarker()
    {
        var document = CadDocument.Create("Infinite snap marker");
        document.ViewSettings.Grid.SnapMarkerType = CadSnapMarkerType.InfiniteCross;
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(4.0, new CadPointD(160, 120));
        var service = new CadSnapInteractionService(document, viewport);
        var items = new List<CadTransientItem>();
        var snappedWorld = new CadPointD(10, -5);

        service.AddSnapMarker(
            items,
            new CadPointD(10.25, -4.75),
            snappedWorld);

        var marker = Assert.IsType<CadTransientInfiniteCross>(Assert.Single(items));
        Assert.Equal(snappedWorld, marker.Center);
    }

    private static CadViewport CreateViewport()
    {
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(1.0, new CadPointD(160, 120));
        return viewport;
    }
}
