using System.IO;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Cad;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Settings.UserSettings;

namespace Direct2dCad.ViewModels.Tests;

public sealed class UserSettingsLifecycleTests
{
    public static IEnumerable<object[]> InvalidSizes =>
        from property in Enumerable.Range(0, 6)
        from value in new[] { 0.0, -1, double.NaN, double.PositiveInfinity }
        select new object[] { property, value };

    [Theory]
    [MemberData(nameof(InvalidSizes))]
    public void InvalidInteractionSettingsCannotBePersistedOrApplied(int property, double value)
    {
        var store = new RecordingSettingsStore();
        var applied = 0;
        var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), store, _ => applied++);
        switch (property)
        {
            case 0: vm.Interaction.SelectedEntityStrokeWidth = value; break;
            case 1: vm.Interaction.GripSize = value; break;
            case 2: vm.Interaction.GripStrokeWidth = value; break;
            case 3: vm.Interaction.GripPreviewStrokeWidth = value; break;
            case 4: vm.Interaction.SelectionWindowStrokeWidth = value; break;
            case 5: vm.Interaction.SelectionCrossingStrokeWidth = value; break;
        }
        Assert.False(vm.TryApply());
        Assert.Same(vm.Interaction, vm.SelectedSection);
        Assert.False(string.IsNullOrWhiteSpace(vm.ValidationError));
        Assert.Empty(store.Saved);
        Assert.Equal(0, applied);
        vm.ResetToDefaults();
        Assert.Null(vm.ValidationError);
        Assert.True(vm.TryApply());
        Assert.Single(store.Saved);
        Assert.Equal(1, applied);
    }

    [Theory]
    [InlineData(1033)]
    [InlineData(1041)]
    [InlineData(2052)]
    public void EditingAndResetAreIsolatedUntilApply(int lcid)
    {
        var settings = CadUserSettings.CreateDefault();
        var original = settings.Clone();
        var store = new RecordingSettingsStore();
        CadUserSettings? applied = null;
        var vm = new UserSettingsViewModel(settings, store, value => applied = value);
        vm.General.IsDarkTheme = !settings.General.IsDarkTheme;
        vm.General.PrimaryColor = CadColor.Red;
        vm.General.SecondaryColor = CadColor.Blue;
        vm.General.SelectedCulture = vm.General.CultureOptions.Single(option => option.Lcid == lcid);
        vm.Rendering.IsLevelOfDetailEnabled = true;
        vm.Rendering.IsParallelRenderingEnabled = true;
        vm.Rendering.ParallelRenderingWorkerCount = 3;
        vm.Interaction.GripSize = 20;
        Assert.Empty(store.Saved);
        Assert.Null(applied);
        Assert.Equal(original.General.IsDarkTheme, settings.General.IsDarkTheme);
        Assert.Equal(original.Interaction.GripSize, settings.Interaction.GripSize);
        Assert.True(vm.TryApply());
        Assert.NotNull(applied);
        Assert.Equal(lcid, applied.General.CultureLcid);
        Assert.Equal(CadColor.Red, applied.General.PrimaryColor);
        Assert.Equal(CadColor.Blue, applied.General.SecondaryColor);
        Assert.True(applied.Rendering.IsLevelOfDetailEnabled);
        Assert.True(applied.Rendering.IsParallelRenderingEnabled);
        Assert.Equal(3, applied.Rendering.ParallelRenderingWorkerCount);
        Assert.Equal(20, applied.Interaction.GripSize);
        vm.ResetToDefaults();
        Assert.Single(store.Saved);
        Assert.Equal(20, applied.Interaction.GripSize);
        Assert.True(vm.TryApply());
        Assert.Equal(original.Interaction.GripSize, applied.Interaction.GripSize);
        Assert.Equal(original.General.PrimaryColor, applied.General.PrimaryColor);
        Assert.Equal(2, store.Saved.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistenceFailureDoesNotApplyAndCanBeRetried(bool unauthorized)
    {
        var store = new RecordingSettingsStore
        {
            Failure = unauthorized ? new UnauthorizedAccessException("denied") : new IOException("disk full")
        };
        var applied = 0;
        var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), store, _ => applied++);
        Assert.False(vm.TryApply());
        Assert.Equal(store.Failure.Message, vm.ValidationError);
        Assert.Equal(0, applied);
        store.Failure = null;
        Assert.True(vm.TryApply());
        Assert.Null(vm.ValidationError);
        Assert.Equal(1, applied);
    }

    [Fact]
    public void MissingCultureSelectsGeneralSectionWithoutSaving()
    {
        var store = new RecordingSettingsStore();
        var vm = new UserSettingsViewModel(CadUserSettings.CreateDefault(), store, _ => Assert.Fail("Unexpected apply"));
        vm.General.SelectedCulture = null;
        vm.SelectedSection = vm.Rendering;
        Assert.False(vm.TryApply());
        Assert.Same(vm.General, vm.SelectedSection);
        Assert.Empty(store.Saved);
    }
}

internal sealed class RecordingSettingsStore : IUserSettingsStore
{
    public Exception? Failure { get; set; }
    public List<CadUserSettings> Saved { get; } = [];
    public CadUserSettings Load() => CadUserSettings.CreateDefault();
    public void Save(CadUserSettings settings)
    {
        if (Failure is not null) throw Failure;
        Saved.Add(settings.Clone());
    }
}
