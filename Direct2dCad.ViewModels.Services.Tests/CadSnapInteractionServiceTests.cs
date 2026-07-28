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
}
