using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class DocumentSettingsCommandTests
{
    [Fact]
    public void SetDocumentSettings_UndoRestoresUnitAndPrecision()
    {
        var document = CadDocument.Create("Test");
        var command = new SetDocumentSettingsCommand(CadUnit.Inch, 5, 4);

        command.Execute(document);

        Assert.Equal(CadUnit.Inch, document.DocumentSettings.Unit);
        Assert.Equal(5, document.DocumentSettings.LengthPrecision);
        Assert.Equal(4, document.DocumentSettings.AnglePrecision);

        command.Undo(document);

        Assert.Equal(CadUnit.Millimeter, document.DocumentSettings.Unit);
        Assert.Equal(3, document.DocumentSettings.LengthPrecision);
        Assert.Equal(2, document.DocumentSettings.AnglePrecision);
    }

    [Fact]
    public void SetDocumentSettings_RejectsInvalidPrecision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SetDocumentSettingsCommand(CadUnit.Millimeter, 13, 2));
    }

    [Fact]
    public void SetViewSettingsCommand_UndoRestoresGridPresetCollection()
    {
        var document = CadDocument.Create("Test");
        var original = document.ViewSettings.Grid.SpacingPresets.ToArray();
        var replacement = original
            .Skip(1)
            .Append(new CadGridSpacingPreset(Guid.NewGuid(), "AI Custom", 12, 3, false))
            .ToArray();
        var settings = new CadViewSettings
        {
            BackgroundColor = document.ViewSettings.BackgroundColor,
            Grid = new CadGridSettings
            {
                SpacingX = 12,
                SpacingY = 12,
                MinorSpacingX = 3,
                MinorSpacingY = 3,
                Subdivision = 4
            },
            Origin = new CadOriginSettings { Position = CadPointD.Origin }
        };
        settings.Grid.ReplaceSpacingPresets(replacement, replacement[0].Id, replacement[1].Id);
        var command = new SetViewSettingsCommand(settings);

        command.Execute(document);
        Assert.Contains(document.ViewSettings.Grid.SpacingPresets, preset => preset.Name == "AI Custom");

        command.Undo(document);
        Assert.Equal(original.Select(preset => preset.Id), document.ViewSettings.Grid.SpacingPresets.Select(preset => preset.Id));
    }
}
