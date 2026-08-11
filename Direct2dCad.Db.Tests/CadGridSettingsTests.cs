using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Db.Tests;

public sealed class CadGridSettingsTests
{
    [Fact]
    public void SnapSpacing_PrefersExplicitSpacingAndFallsBackToMinorSpacing()
    {
        var grid = new CadGridSettings
        {
            SpacingX = 20,
            SpacingY = 30,
            MinorSpacingX = 2,
            MinorSpacingY = 3,
            SnapSpacingX = 5,
            SnapSpacingY = 0
        };

        Assert.Equal(5, grid.GetSnapSpacingX());
        Assert.Equal(3, grid.GetSnapSpacingY());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacing_UsesGuardedSubdivision(double invalidSpacing)
    {
        var grid = new CadGridSettings
        {
            SpacingX = 20,
            MinorSpacingX = invalidSpacing,
            Subdivision = 4
        };

        Assert.Equal(5, grid.GetMinorSpacingX());
    }

    [Fact]
    public void ReplaceSpacingPresets_FiltersInvalidDuplicateAndLinkedEntries()
    {
        var grid = new CadGridSettings();
        var validId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();

        grid.ReplaceSpacingPresets(
        [
            new(validId, " Main ", 2, 9, LinkAxes: true),
            new(Guid.Empty, "invalid id", 2, 2, LinkAxes: false),
            new(duplicateId, "main", 3, 3, LinkAxes: false),
            new(Guid.NewGuid(), "invalid range", 0, 1, LinkAxes: false)
        ],
        validId,
        validId);

        var preset = Assert.Single(grid.SpacingPresets);
        Assert.Equal(validId, preset.Id);
        Assert.Equal("Main", preset.Name);
        Assert.Equal(2, preset.SpacingX);
        Assert.Equal(2, preset.SpacingY);
        Assert.Equal(validId, grid.MajorSpacingPresetId);
        Assert.Equal(validId, grid.MinorSpacingPresetId);
    }

    [Fact]
    public void ReplaceSpacingPresets_EmptyInputRestoresDefaultsAndClearsInvalidSelections()
    {
        var grid = new CadGridSettings();

        grid.ReplaceSpacingPresets([], Guid.NewGuid(), Guid.NewGuid());

        Assert.NotEmpty(grid.SpacingPresets);
        Assert.NotNull(grid.MajorSpacingPresetId);
        Assert.NotNull(grid.MinorSpacingPresetId);
        Assert.Contains(grid.SpacingPresets, preset => preset.Id == grid.MajorSpacingPresetId);
        Assert.Contains(grid.SpacingPresets, preset => preset.Id == grid.MinorSpacingPresetId);
    }

    [Fact]
    public void EnsurePresetSelections_AddsPresetsForCurrentSpacing()
    {
        var grid = new CadGridSettings
        {
            SpacingX = 7,
            SpacingY = 9,
            MinorSpacingX = 0,
            MinorSpacingY = 0,
            Subdivision = 3
        };
        grid.SpacingPresets.Clear();
        grid.MajorSpacingPresetId = null;
        grid.MinorSpacingPresetId = null;

        grid.EnsurePresetSelections();

        Assert.Equal(2, grid.SpacingPresets.Count);
        Assert.NotNull(grid.MajorSpacingPresetId);
        Assert.NotNull(grid.MinorSpacingPresetId);
        Assert.Contains(grid.SpacingPresets, preset => preset.Id == grid.MajorSpacingPresetId);
        Assert.Contains(grid.SpacingPresets, preset => preset.Id == grid.MinorSpacingPresetId);
    }
}
