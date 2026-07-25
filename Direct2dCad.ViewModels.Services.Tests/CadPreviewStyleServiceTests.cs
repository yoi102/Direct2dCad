using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Styling;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadPreviewStyleServiceTests
{
    [Fact]
    public void SelectionStylesUseUserInteractionSettings()
    {
        var document = CadDocument.Create("Test");
        var settings = CadUserSettings.CreateDefault();
        settings.Interaction.SelectionWindowStrokeColor = CadColor.FromRgb(10, 20, 30);
        settings.Interaction.SelectionWindowFillColor = CadColor.FromArgb(40, 50, 60, 70);
        settings.Interaction.SelectionWindowStrokeWidth = 3.5;
        var service = new CadPreviewStyleService(document, settings);

        var style = service.CreateSelectionWindowStyle();

        Assert.Equal(settings.Interaction.SelectionWindowStrokeColor, style.StrokeColor);
        Assert.Equal(settings.Interaction.SelectionWindowFillColor, style.FillColor);
        Assert.Equal(3.5, style.StrokeWidth);
    }

    [Fact]
    public void EntityPreviewResolvesLayerAppearanceAndSolidFill()
    {
        var document = CadDocument.Create("Test");
        var layerId = document.CreateLayer(
            "Layer",
            CadColor.FromRgb(20, 40, 60),
            new CadLineWeight(2.25));
        var fillColor = CadColor.FromArgb(180, 80, 100, 120);
        var fillStyleId = document.CreateSolidFillStyle("Solid", fillColor);
        var circle = document.AddCircle(
            new CadPointD(5, 5),
            3,
            layerId,
            fillStyleId: fillStyleId);
        var service = new CadPreviewStyleService(
            document,
            CadUserSettings.CreateDefault());

        var style = service.CreateEntityPreviewStyle(circle);

        Assert.Equal(document.GetLayer(layerId).Color, style.StrokeColor);
        Assert.Equal(2.25, style.StrokeWidth);
        Assert.Equal(fillColor, style.FillColor);
        Assert.Null(style.HatchFill);
    }

    [Fact]
    public void ExplicitEntityLineWeightOverridesLayerForPreview()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        line.SetLineWeight(new CadLineWeight(4.5));
        var service = new CadPreviewStyleService(
            document,
            CadUserSettings.CreateDefault());

        var style = service.CreateEntityPreviewStyle(line);

        Assert.Equal(4.5, style.StrokeWidth);
    }
}
