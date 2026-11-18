using Direct2dCad.AI.Contracts;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class JsonSettingsStoresTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d2cad-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void UserSettingsAdoptTheFallbackFileOnlyUntilTheirOwnFileExists()
    {
        var fallback = new JsonUserSettingsStore(PathOf("wpf.json"));
        var adopted = CadUserSettings.CreateDefault();
        adopted.Rendering.IsZoomSnapshotPreviewEnabled = !adopted.Rendering.IsZoomSnapshotPreviewEnabled;
        fallback.Save(adopted);

        var store = new JsonUserSettingsStore(PathOf("avalonia.json"), PathOf("wpf.json"));
        Assert.Equal(adopted.Rendering.IsZoomSnapshotPreviewEnabled, store.Load().Rendering.IsZoomSnapshotPreviewEnabled);
        Assert.False(File.Exists(PathOf("avalonia.json")));

        var own = CadUserSettings.CreateDefault();
        store.Save(own);
        Assert.Equal(own.Rendering.IsZoomSnapshotPreviewEnabled, store.Load().Rendering.IsZoomSnapshotPreviewEnabled);
    }

    [Fact]
    public void MalformedFilesFallBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathOf("user.json"), "{ nope");
        File.WriteAllText(PathOf("workspace.json"), "[1,2");

        Assert.NotNull(new JsonUserSettingsStore(PathOf("user.json")).Load());
        Assert.NotNull(new JsonWorkspaceSettingsStore(PathOf("workspace.json")).LoadDocument(PathOf("a.d2cad")));
    }

    [Fact]
    public void WorkspaceAndToolboxStoresRoundTripAndLeaveNoTemporaryFiles()
    {
        var workspace = new JsonWorkspaceSettingsStore(PathOf("workspace.json"));
        workspace.SaveDocument(PathOf("a.d2cad"), new CadDocumentWorkspaceSettings());
        Assert.NotNull(new JsonWorkspaceSettingsStore(PathOf("workspace.json")).LoadDocument(PathOf("a.d2cad")));

        var toolboxes = new JsonToolboxLayoutSettingsStore(PathOf("toolbox.json"));
        toolboxes.Save([new(" toolbox.layers ", new CadToolboxState { IsOpen = true }), new(" ", new CadToolboxState())]);
        var reloaded = new JsonToolboxLayoutSettingsStore(PathOf("toolbox.json"));
        Assert.True(reloaded.Load("toolbox.layers")?.IsOpen);

        Assert.Equal(["toolbox.json", "workspace.json"], Directory.GetFiles(_directory).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void AiSettingsWrittenWithPascalCaseNamesStillLoad()
    {
        // The Avalonia host used to write this file with default (PascalCase) property names.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathOf("ai.json"), """{ "Model": "pascal-model", "Provider": 1 }""");

        var loaded = new JsonAiAssistantSettingsStore(PathOf("ai.json")).Load();

        Assert.Equal("pascal-model", loaded.Model);
        Assert.Equal(AiAssistantProvider.Codex, loaded.Provider);
    }
}
