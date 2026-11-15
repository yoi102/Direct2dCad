using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.ViewModels.Services.Documents;

namespace Direct2dCad.ViewModels.Tests;

public sealed class WorkspaceAiCoverageTests
{
    [Theory]
    [InlineData("跨图纸复制", "transfer_entities")]
    [InlineData("工程模板", "list_document_templates")]
    [InlineData("恢复图纸", "list_recovery_copies")]
    [InlineData("保存兼容副本", "save_compatible_copy")]
    [InlineData("覆盖文件", "get_file_revision")]
    [InlineData("设置应用主题", "get_application_settings")]
    [InlineData("粘贴图片", "insert_image_from_clipboard")]
    public void NewCapabilitiesAreDiscoveredByTheBuiltInAgent(string prompt, string expectedTool)
    {
        var tools = Agents.CadAgentToolSelector.Select(prompt, CadWorkspaceToolExecutor.ToolDefinitions);
        Assert.Equal(expectedTool, tools[0].Name);
    }

    private static async Task<JsonElement> Call(ICadToolWorkspace workspace, string name, object args, bool success = true)
    {
        using var result = JsonDocument.Parse(await new CadWorkspaceToolExecutor(workspace)
            .ExecuteAsync(new(Guid.NewGuid().ToString(), name, JsonSerializer.Serialize(args)), default));
        Assert.Equal(success, result.RootElement.GetProperty("success").GetBoolean());
        return result.RootElement.Clone();
    }

    [Theory]
    [InlineData("A4", 1)]
    [InlineData("A3-100", 100)]
    [InlineData("Letter", 1)]
    public async Task AiCreatesEveryEngineeringTemplateWithHostAnnotationDefaults(string key, double annotationScale)
    {
        using var context = new MainWindowTestContext();
        var workspace = context.CreateToolWorkspace();
        await Call(workspace, "create_document", new { template = key, name = "Engineering" });
        var tab = Assert.IsType<EditorTabViewModel>(Assert.Single(workspace.GetDocuments()).Host);
        Assert.Equal("Engineering", tab.DocumentName);
        Assert.Equal(annotationScale, tab.CadDocumentViewModel.DimensionAnnotationScale);
        Assert.Equal(CadEngineeringTemplates.Create(key).DocumentSettings.Unit, tab.CadDocumentViewModel.DimensionUnit);
        Assert.Single(tab.CadDocumentViewModel.CadEditor.Document.Layouts);
    }

    [Fact]
    public async Task SettingsSchemaCoversAllPersistedPreferencesAndPatchesPreserveOtherValues()
    {
        using var context = new MainWindowTestContext();
        var workspace = context.CreateToolWorkspace();
        var before = (await Call(workspace, "get_application_settings", new { })).GetProperty("result").GetProperty("settings");
        var schema = CadWorkspaceToolExecutor.ToolDefinitions.Single(tool => tool.Name == "set_application_settings")
            .Parameters.GetProperty("properties").GetProperty("settings");
        static void Check(JsonElement settings, JsonElement schema)
        {
            foreach (var property in settings.EnumerateObject())
            {
                if (property.Name == "version") continue;
                Assert.True(schema.GetProperty("properties").TryGetProperty(property.Name, out var child), property.Name);
                if (property.Value.ValueKind == JsonValueKind.Object) Check(property.Value, child);
            }
        }
        Check(before, schema);
        var result = await Call(workspace, "set_application_settings", new
        {
            settings = new { general = new { history_budget_megabytes = 128, is_dark_theme = false },
                rendering = new { show_frames_per_second = false }, interaction = new { grip_size = 12 } }
        });
        var after = result.GetProperty("result").GetProperty("settings");
        Assert.Equal(128, after.GetProperty("general").GetProperty("history_budget_megabytes").GetInt32());
        Assert.False(context.Appearance.IsDarkTheme);
        Assert.Equal(12, after.GetProperty("interaction").GetProperty("grip_size").GetDouble());
        Assert.True(JsonElement.DeepEquals(before.GetProperty("general").GetProperty("primary_color"), after.GetProperty("general").GetProperty("primary_color")));
        await Call(workspace, "set_application_settings", new { settings = new { }, reset = true });
        Assert.Equal(256, context.Settings.Load().General.HistoryBudgetMegabytes);
    }

    [Fact]
    public async Task InvalidSettingsDoNotPersistOrApply()
    {
        using var context = new MainWindowTestContext();
        var workspace = context.CreateToolWorkspace();
        await Call(workspace, "set_application_settings", new { settings = new { interaction = new
        { radial_menu = new { middle_actions = Enumerable.Repeat("MissingAction", 8).ToArray() } } } }, false);
        Assert.Equal(256, context.Settings.Load().General.HistoryBudgetMegabytes);
        Assert.Empty(context.Dialogs.Errors);
    }

    [Theory]
    [InlineData("cancel", false, 0)]
    [InlineData("discard", true, 0)]
    [InlineData("save", true, 1)]
    public async Task ExplicitCloseDecisionsRunWithoutDialogs(string decision, bool closed, int writes)
    {
        using var context = new MainWindowTestContext();
        var (tab, writer) = context.AddDocument("Close decision");
        var workspace = context.CreateToolWorkspace();
        var result = await Call(workspace, "close_document", new { on_unsaved = decision,
            file_path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad") }, closed);
        Assert.Equal(writes, writer.Writes);
        Assert.Equal(closed, result.GetProperty("result").GetProperty("closed").GetBoolean());
        Assert.Equal(closed ? 0 : 1, workspace.GetDocuments().Count);
        Assert.Equal(0, context.Dialogs.CloseRequests);
        Assert.Empty(context.Dialogs.Errors);
    }

    [Fact]
    public async Task FailedUnattendedSaveKeepsTheTabOpenAndReturnsErrorWithoutDialogs()
    {
        using var context = new MainWindowTestContext();
        var (tab, writer) = context.AddDocument("Failed save");
        writer.Failure = new IOException("Disk full");
        var workspace = context.CreateToolWorkspace();
        await Call(workspace, "save_document", new { interactive = false }, false);
        await Call(workspace, "close_document", new { on_unsaved = "save", file_path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".d2cad") }, false);
        Assert.Single(workspace.GetDocuments());
        Assert.True(tab.IsModified);
        Assert.Equal(0, context.Dialogs.CloseRequests);
        Assert.Empty(context.Dialogs.Errors);
    }

    [Fact]
    public async Task ExplicitOverwriteChecksTheRequestedRevisionAndReportsConflicts()
    {
        var path = Path.Combine(Path.GetTempPath(), "cad-ai-overwrite-" + Guid.NewGuid() + ".d2cad");
        try
        {
            await File.WriteAllTextAsync(path, "Existing revision");
            using var context = new MainWindowTestContext();
            var (tab, writer) = context.AddDocument("Save conflict");
            var workspace = context.CreateToolWorkspace();
            var failure = await Call(workspace, "save_document", new { file_path = path, interactive = false }, false);
            Assert.Equal("file_conflict", failure.GetProperty("code").GetString());
            var hash = failure.GetProperty("current_revision").GetProperty("sha256").GetString();
            await Call(workspace, "save_document", new { file_path = path, overwrite = true, expected_sha256 = new string('0', 64) }, false);
            Assert.Equal(0, writer.Writes);
            await Call(workspace, "save_document", new { file_path = path, overwrite = true, expected_sha256 = hash });
            Assert.Equal(1, writer.Writes);
            Assert.Equal(path, tab.CurrentFilePath);
            Assert.Empty(context.Dialogs.Errors);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RecoveryToolsListVersionsReuseIndependentCopyAndPreserveLiveBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cad-ai-recovery-" + Guid.NewGuid());
        try
        {
            using var store = new CadRecoveryStore(directory, trackSession: true);
            using var seed = new CadRecoveryStore(directory);
            var source = new CadEditor(CadDocument.Create("Abandoned"));
            source.AddLine(default, new(10, 0));
            await seed.SaveAsync(source, "original.d2cad");
            source.AddLine(default, new(20, 0));
            await seed.SaveAsync(source, "original.d2cad");
            using var context = new MainWindowTestContext(store);
            var live = context.AddDocument("Live").Tab;
            await store.SaveAsync(live.CadDocumentViewModel.CadEditor, "");
            var workspace = context.CreateToolWorkspace();
            var copies = (await Call(workspace, "list_recovery_copies", new { })).GetProperty("result").GetProperty("copies");
            Assert.Equal(2, copies.GetArrayLength());
            var recoveryId = copies[0].GetProperty("recovery_id").GetString();
            await Call(workspace, "open_recovery_copy", new { recovery_id = recoveryId });
            var recovered = Assert.IsType<EditorTabViewModel>(workspace.GetActiveDocument()!.Host);
            Assert.NotEqual(source.Document.Id, recovered.CadDocumentViewModel.CadEditor.Document.Id);
            Assert.Empty(recovered.CurrentFilePath);
            Assert.Equal(source.Document.Id.ToString(), recovered.RecoverySourceDocumentId);
            await Call(workspace, "open_recovery_copy", new { recovery_id = recoveryId });
            Assert.Same(recovered, workspace.GetActiveDocument()!.Host);
            await Call(workspace, "clear_recovery_copies", new { confirm = true });
            Assert.Empty(store.ListRecoverable());
            Assert.Equal(live.CadDocumentViewModel.CadEditor.Document.Id.ToString(), Assert.Single(store.List()).DocumentId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CompatibleCopyUsesASeparateRealFileAndReloadsWritableGeometry()
    {
        var path = Path.Combine(Path.GetTempPath(), "cad-ai-compatible-" + Guid.NewGuid() + ".d2cad");
        try
        {
            using var context = new CadToolboxTestContext();
            var dialogs = new RecordingDialogService();
            using var tab = context.CreateEditorTab(dialogs, new RecordingFileDialogs(), new CadDocumentStorage());
            var document = tab.CadDocumentViewModel.CadEditor.Document;
            document.AddLine(default, new(10, 0));
            document.SetCompatibilityReadOnly("Future sections");
            Assert.True(await tab.SaveCompatibleCopyForToolAsync(path, new(), default));
            Assert.Equal(path, tab.CurrentFilePath);
            Assert.False(tab.IsCompatibilityReadOnly);
            Assert.IsType<CadLine>(Assert.Single(tab.CadDocumentViewModel.CadEditor.Document.Entities.Values));
            Assert.Empty(dialogs.Errors);
        }
        finally { File.Delete(path); }
    }
}
