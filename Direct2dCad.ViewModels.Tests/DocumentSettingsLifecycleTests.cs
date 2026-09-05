using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class DocumentSettingsLifecycleTests
{
    [Fact]
    public void DialogApplyIsOneUndoStepAndResetOnlyChangesTheDraft()
    {
        using var context = new CadToolboxTestContext();
        var dialogs = new RecordingDialogService();
        using var tab = context.CreateEditorTab(dialogs, new RecordingFileDialogs(), new RecordingDocumentWriter());
        var editor = context.Document.CadEditor;
        var history = editor.CreateDocumentHistorySnapshot();
        var originalColor = editor.Document.ViewSettings.BackgroundColor;
        var vm = new DocumentSettingsViewModel(tab, dialogs);
        vm.Display.BackgroundColor = CadColor.Blue;
        vm.Origin.OriginX = 50;
        vm.Origin.OriginY = 60;
        vm.Origin.OriginSize = 30;
        Assert.Equal(originalColor, editor.Document.ViewSettings.BackgroundColor);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.True(vm.TryApply());
        var changed = editor.CreateDocumentHistorySnapshot();
        Assert.False(editor.DocumentHistoryEquals(history));
        Assert.Equal(CadColor.Blue, tab.ViewModelCadBackgroundColor);
        Assert.Equal(50, tab.ViewModelCadOriginX);
        Assert.True(vm.TryApply());
        Assert.True(editor.DocumentHistoryEquals(changed));
        vm.ResetToDefaults();
        Assert.True(editor.DocumentHistoryEquals(changed));
        Assert.Equal(CadColor.Blue, editor.Document.ViewSettings.BackgroundColor);
        tab.UndoCommand.Execute(null);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Equal(originalColor, tab.ViewModelCadBackgroundColor);
        tab.RedoCommand.Execute(null);
        Assert.Equal(CadColor.Blue, tab.ViewModelCadBackgroundColor);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e308)]
    public void OriginRejectsNonFiniteMillimeterCoordinatesWithoutChangingDocument(double input)
    {
        var original = new CadViewSettings();
        var vm = new DocumentOriginSettingsViewModel(original.Origin, CadUnit.Inch) { OriginX = input };
        var target = new CadViewSettings();
        var position = target.Origin.Position;
        Assert.False(vm.TryApplyTo(target));
        Assert.Equal(position, target.Origin.Position);
    }

    [Theory]
    [InlineData(CadUnit.Millimeter)]
    [InlineData(CadUnit.Inch)]
    public void OriginRoundTripsDisplayUnits(CadUnit unit)
    {
        var settings = new CadViewSettings();
        settings.Origin.Position = new(25.4, -50.8);
        var vm = new DocumentOriginSettingsViewModel(settings.Origin, unit);
        var target = new CadViewSettings();
        Assert.True(vm.TryApplyTo(target));
        Assert.Equal(settings.Origin.Position, target.Origin.Position);
    }

    [Fact]
    public async Task GridPresetAddEditMoveDeleteKeepsSelectionAndApplyInSync()
    {
        var dialogs = new RecordingDialogService();
        var vm = new DocumentGridSettingsViewModel(new CadGridSettings(), dialogs);
        var count = vm.GridSpacingPresets.Count;
        await vm.AddGridSpacingPresetCommand.ExecuteAsync(null);
        Assert.Equal(count, vm.GridSpacingPresets.Count);
        dialogs.GridResult = new("Custom", 20, 20, true);
        await vm.AddGridSpacingPresetCommand.ExecuteAsync(null);
        var added = vm.SelectedGridSpacingPreset!;
        vm.SelectedMajorGridPreset = added;
        vm.SelectedMinorGridPreset = vm.GridSpacingPresets.First(item => item.SpacingX == 1 && item.SpacingY == 1);
        Assert.Equal(count + 1, vm.GridSpacingPresets.Count);
        vm.MoveGridSpacingPresetUpCommand.Execute(null);
        Assert.Same(added, vm.SelectedGridSpacingPreset);
        dialogs.GridResult = new("Updated", 10, 10, true);
        await vm.EditGridSpacingPresetCommand.ExecuteAsync(null);
        Assert.Equal(added.Id, vm.SelectedMajorGridPreset!.Id);
        Assert.Equal("Updated", vm.SelectedMajorGridPreset.Name);
        var target = new CadViewSettings();
        Assert.True(vm.TryApplyTo(target));
        Assert.Equal(10, target.Grid.SpacingX);
        Assert.Contains(target.Grid.SpacingPresets, item => item.Name == "Updated");
        vm.DeleteGridSpacingPresetCommand.Execute(null);
        Assert.Equal(count, vm.GridSpacingPresets.Count);
        Assert.DoesNotContain(vm.GridSpacingPresets, item => item.Id == added.Id);
        Assert.Contains(vm.SelectedMajorGridPreset, vm.GridSpacingPresets);
        while (vm.GridSpacingPresets.Count > 2)
            vm.DeleteGridSpacingPresetCommand.Execute(null);
        Assert.False(vm.DeleteGridSpacingPresetCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(CadUnit.Millimeter)]
    [InlineData(CadUnit.Inch)]
    public void GridPresetEditorEnforcesNamesUnitsAndRanges(CadUnit unit)
    {
        var vm = new GridSpacingPresetEditorViewModel(new(false, "New", 25.4, 50.8, false, ["Used"], unit));
        Assert.True(vm.IsValid);
        Assert.Equal(25.4, vm.CreateResult().SpacingX, 6);
        Assert.Equal(50.8, vm.CreateResult().SpacingY, 6);
        vm.Name = " used ";
        Assert.False(vm.IsValid);
        vm.Name = " Available ";
        vm.LinkAxes = true;
        Assert.Equal(vm.SpacingX, vm.SpacingY);
        vm.SpacingX = vm.MinimumSpacing;
        Assert.True(vm.IsValid);
        Assert.Equal("Available", vm.CreateResult().Name);
        vm.SpacingX = vm.MaximumSpacing;
        Assert.True(vm.IsValid);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0, vm.MaximumSpacing * 2 })
        {
            vm.SpacingX = value;
            Assert.False(vm.IsValid);
        }
    }
}
