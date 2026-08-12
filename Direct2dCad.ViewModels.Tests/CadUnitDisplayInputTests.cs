using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadUnitDisplayInputTests
{
    [Fact]
    public void GridSpacingEditor_UsesSelectedUnitForDisplayAndConvertsBackToMillimeters()
    {
        var editor = new GridSpacingPresetEditorViewModel(
            new GridSpacingPresetDialogRequest(
                IsEditing: true,
                Name: "Inches",
                SpacingX: 25.4,
                SpacingY: 50.8,
                LinkAxes: false,
                UnavailableNames: [],
                Unit: CadUnit.Inch));

        Assert.Equal(1.0, editor.SpacingX, precision: 10);
        Assert.Equal(2.0, editor.SpacingY, precision: 10);
        Assert.Equal("in", editor.UnitSymbol);

        editor.SpacingX = 3;
        editor.SpacingY = 4;

        var result = editor.CreateResult();
        Assert.Equal(76.2, result.SpacingX, precision: 10);
        Assert.Equal(101.6, result.SpacingY, precision: 10);
    }

    [Fact]
    public void GridSpacingEditor_ValidationErrorUsesSelectedUnit()
    {
        var editor = new GridSpacingPresetEditorViewModel(
            new GridSpacingPresetDialogRequest(
                IsEditing: false,
                Name: "Invalid",
                SpacingX: 25.4,
                SpacingY: 25.4,
                LinkAxes: true,
                UnavailableNames: [],
                Unit: CadUnit.Inch));

        editor.SpacingX = 0.00001;

        Assert.Contains("in", editor.ValidationError, StringComparison.Ordinal);
        Assert.DoesNotContain("mm", editor.ValidationError, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginSettings_DisplayPositionUsesSelectedUnit()
    {
        var origin = new DocumentOriginSettingsViewModel(
            new CadOriginSettings { Position = new CadPointD(25.4, -50.8) },
            CadUnit.Inch);

        Assert.Equal(1.0, origin.OriginX, precision: 10);
        Assert.Equal(-2.0, origin.OriginY, precision: 10);
    }
}
