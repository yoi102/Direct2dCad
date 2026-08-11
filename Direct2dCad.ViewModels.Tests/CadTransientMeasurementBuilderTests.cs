using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Drawing;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadTransientMeasurementBuilderTests
{
    [Fact]
    public void FormatLengthLabel_UsesDocumentPrecisionAndUnit()
    {
        var document = CadDocument.Create("Measurements");
        document.DocumentSettings.SetLengthPrecision(2);
        document.DocumentSettings.SetUnit(CadUnit.Centimeter);
        var builder = CreateBuilder(document);

        Assert.Equal("12.35 cm", builder.FormatLengthLabel(12.345));
    }

    [Fact]
    public void FormatDirectionLabel_UsesNormalizedCadDirection()
    {
        var builder = CreateBuilder(CadDocument.Create("Measurements"));

        Assert.Equal("0.00°", builder.FormatDirectionLabel(new CadPointD(0, 0), new CadPointD(1, 0)));
        Assert.Equal("90.00°", builder.FormatDirectionLabel(new CadPointD(0, 0), new CadPointD(0, 1)));
        Assert.Equal("270.00°", builder.FormatDirectionLabel(new CadPointD(0, 0), new CadPointD(0, -1)));
    }

    [Fact]
    public void AddSegmentMeasurements_AddsLengthAndAngleLabels()
    {
        var document = CadDocument.Create("Measurements");
        var builder = CreateBuilder(document);
        var items = new List<CadTransientItem>();

        builder.AddSegmentMeasurements(
            items,
            new CadPointD(0, 0),
            new CadPointD(3, 4),
            CadTransientStyle.Construction);

        var labels = items.OfType<CadTransientText>().Select(item => item.Text).ToArray();
        Assert.Equal(["L 5.000 mm", "A 53.13°"], labels);
        Assert.NotEqual(
            ((CadTransientText)items[0]).Position,
            ((CadTransientText)items[1]).Position);
    }

    private static CadTransientMeasurementBuilder CreateBuilder(CadDocument document)
    {
        var viewport = new CadViewport();
        viewport.SetSize(1000, 1000);
        viewport.SetView(1.0, new CadPointD(0, 1000));
        return new CadTransientMeasurementBuilder(document, viewport);
    }
}
