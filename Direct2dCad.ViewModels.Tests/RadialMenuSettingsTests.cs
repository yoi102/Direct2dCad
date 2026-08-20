using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Interactions;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class RadialMenuSettingsTests
{
    [Fact]
    public void Normalize_RepairsMissingAndInvalidSectorsUsingGestureDefaults()
    {
        var settings = new CadRadialMenuSettings
        {
            MiddleActions =
            [
                CadRadialMenuAction.Line,
                (CadRadialMenuAction)999
            ],
            ShiftMiddleActions = []
        };

        settings.Normalize();

        Assert.Equal(CadRadialMenuSettings.SectorCount, settings.MiddleActions.Length);
        Assert.Equal(CadRadialMenuAction.Line, settings.MiddleActions[0]);
        Assert.Equal(CadRadialMenuAction.Line, settings.MiddleActions[1]);
        Assert.Equal(CadRadialMenuSettings.SectorCount, settings.ShiftMiddleActions.Length);
        Assert.Equal(CadRadialMenuAction.Undo, settings.ShiftMiddleActions[0]);
    }

    [Fact]
    public void UserSettingsClone_PreservesIndependentRadialMenuConfiguration()
    {
        var settings = CadUserSettings.CreateDefault();
        settings.Interaction.RadialMenu.IsEnabled = false;
        settings.Interaction.RadialMenu.MiddleActions[0] = CadRadialMenuAction.Spline;

        var clone = settings.Clone();
        clone.Interaction.RadialMenu.MiddleActions[0] = CadRadialMenuAction.Line;

        Assert.False(clone.Interaction.RadialMenu.IsEnabled);
        Assert.Equal(CadRadialMenuAction.Spline, settings.Interaction.RadialMenu.MiddleActions[0]);
        Assert.Equal(CadRadialMenuAction.Line, clone.Interaction.RadialMenu.MiddleActions[0]);
    }

    [Fact]
    public void SettingsViewModel_AppliesEachEditedProfileBackToSettings()
    {
        var viewModel = new RadialMenuSettingsViewModel(new CadRadialMenuSettings());
        viewModel.IsEnabled = false;
        viewModel.Profiles.Single(profile => profile.Gesture == CadRadialMenuGesture.AltMiddle)
            .Slots[3].SelectedAction = viewModel.ActionOptions.Single(option =>
                option.Action == CadRadialMenuAction.DeleteSelection);
        var target = new CadRadialMenuSettings();

        viewModel.ApplyTo(target);

        Assert.False(target.IsEnabled);
        Assert.Equal(CadRadialMenuAction.DeleteSelection, target.AltMiddleActions[3]);
    }

    [Theory]
    [InlineData(CadRadialMenuAction.Select, CadCanvasToolMode.Select)]
    [InlineData(CadRadialMenuAction.Line, CadCanvasToolMode.Line)]
    [InlineData(CadRadialMenuAction.CircleCenterRadius, CadCanvasToolMode.CircleCenterRadius)]
    [InlineData(CadRadialMenuAction.EllipseArc, CadCanvasToolMode.EllipseArc)]
    [InlineData(CadRadialMenuAction.ArcCenterStartLength, CadCanvasToolMode.ArcCenterStartLength)]
    [InlineData(CadRadialMenuAction.Rectangle, CadCanvasToolMode.Rectangle)]
    [InlineData(CadRadialMenuAction.Polyline, CadCanvasToolMode.Polyline)]
    [InlineData(CadRadialMenuAction.Polygon, CadCanvasToolMode.Polygon)]
    [InlineData(CadRadialMenuAction.Spline, CadCanvasToolMode.Spline)]
    [InlineData(CadRadialMenuAction.Text, CadCanvasToolMode.Text)]
    [InlineData(CadRadialMenuAction.SetOrigin, CadCanvasToolMode.SetOrigin)]
    public void ActionMapper_MapsEveryRepresentativeDrawingAction(
        CadRadialMenuAction action,
        CadCanvasToolMode expectedMode)
    {
        var mapped = CadRadialMenuActionMapper.TryGetToolMode(action, out var actualMode);

        Assert.True(mapped);
        Assert.Equal(expectedMode, actualMode);
    }

    [Fact]
    public void ActionMapper_DoesNotTreatEditingActionAsDrawingMode()
    {
        Assert.False(CadRadialMenuActionMapper.TryGetToolMode(
            CadRadialMenuAction.Undo,
            out _));
    }
}
