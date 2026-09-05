using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Tools;
using static Direct2dCad.ViewModels.Tests.ToolExecutionWorkspace;

namespace Direct2dCad.ViewModels.Tests;

public sealed class AgentResourceMutationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkTextContentAndInversionAreOneUndoGroup(bool shapeText)
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Text").DocumentViewModel.CadEditor;
        var kind = shapeText ? TestEntityKind.ShapeText : TestEntityKind.Text;
        var first = CadEntityTestCases.Add(editor.Document, kind);
        var second = CadEntityTestCases.Add(editor.Document, kind);
        var history = editor.CreateDocumentHistorySnapshot();
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "set_entity_specific_properties", new { entity_ids = new[] { first.Id.Value, second.Id.Value },
            text = "Updated", inverted = true, inverted_margin_factor = 0.25 });
        AssertText(first, "Updated", true);
        AssertText(second, "Updated", true);
        editor.Undo();
        Assert.True(editor.DocumentHistoryEquals(history));
        AssertText(first, "CAD", false);
        AssertText(second, "CAD", false);
        editor.Redo();
        AssertText(first, "Updated", true);
        AssertText(second, "Updated", true);
    }

    [Fact]
    public async Task FontStyleIsReusedAndCreationIsRolledBackWithEntityChanges()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Font").DocumentViewModel.CadEditor;
        var one = editor.Document.AddText("One", new(0, 0), 10);
        var two = editor.Document.AddText("Two", new(20, 0), 10);
        var count = editor.Document.Styles.Count;
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "set_entity_specific_properties", new { entity_ids = new[] { one.Id.Value }, font_family = "Arial" });
        var style = one.TextStyleId;
        Assert.NotNull(style);
        Assert.Equal("Arial", editor.Document.GetStyle<CadTextStyle>(style.Value).FontFamily);
        await Execute(executor, "set_entity_specific_properties", new { entity_ids = new[] { two.Id.Value }, font_family = "Arial" });
        Assert.Equal(style, two.TextStyleId);
        Assert.Equal(count + 1, editor.Document.Styles.Count);
        editor.Undo();
        Assert.Null(one.TextStyleId);
        Assert.Null(two.TextStyleId);
        Assert.Equal(count, editor.Document.Styles.Count);
        editor.Redo();
        Assert.Equal(style, one.TextStyleId);
        Assert.Equal(style, two.TextStyleId);
    }

    [Fact]
    public async Task ImportedImagePreservesBytesRotationOpacityAndHistory()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Image").DocumentViewModel.CadEditor;
        var images = new RecordingImageImport();
        await Execute(new(workspace, images), "insert_image_from_file", new { file_path = "fixture.png",
            bounds = new { min_x = 0, min_y = 0, max_x = 40, max_y = 30 },
            rotation_degrees = 30, opacity = 0.4, name = "Imported" });
        Assert.Equal(Path.GetFullPath("fixture.png"), images.RequestedPath);
        var image = Assert.IsType<CadImage>(Assert.Single(editor.Document.Entities.Values));
        Assert.Equal(0.4, image.Opacity);
        Assert.Equal(Math.PI / 6, image.RotationRadians, 8);
        Assert.Equal("Imported", image.Name);
        Assert.Equal(1, image.PixelWidth);
        editor.Undo();
        Assert.True(image.IsErased);
        editor.Redo();
        Assert.False(image.IsErased);
        await Execute(new(workspace), "set_entity_specific_properties", new { entity_ids = new[] { image.Id.Value }, opacity = 0.7 });
        Assert.Equal(0.7, image.Opacity);
        editor.Undo();
        Assert.Equal(0.4, image.Opacity);
    }

    [Fact]
    public async Task OleStorageAndOpacityUpdatesRestorePreviousPayloadOnUndo()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("OLE").DocumentViewModel.CadEditor;
        await Execute(new(workspace), "add_ole_object", new { ole_base64 = "AQID",
            bounds = new { min_x = 0, min_y = 0, max_x = 40, max_y = 30 }, opacity = 0.4 });
        var ole = Assert.IsType<CadOleObject>(Assert.Single(editor.Document.Entities.Values));
        Assert.Equal(new byte[] { 1, 2, 3 }, ole.CopyOleBytes());
        var executor = new CadWorkspaceToolExecutor(workspace);
        await Execute(executor, "set_ole_object_data", new { entity_id = ole.Id.Value, ole_base64 = "BAUG", source_name = "Changed" });
        await Execute(executor, "set_entity_specific_properties", new { entity_ids = new[] { ole.Id.Value }, opacity = 0.8 });
        Assert.Equal(new byte[] { 4, 5, 6 }, ole.CopyOleBytes());
        Assert.Equal(0.8, ole.Opacity);
        editor.Undo();
        Assert.Equal(new byte[] { 1, 2, 3 }, ole.CopyOleBytes());
        Assert.Equal(0.4, ole.Opacity);
        editor.Redo();
        Assert.Equal(new byte[] { 4, 5, 6 }, ole.CopyOleBytes());
        Assert.Equal("Changed", ole.SourceName);
        Assert.Equal(0.8, ole.Opacity);
    }

    [Fact]
    public async Task OleStorageCannotBeChangedFromAnotherEditingSpace()
    {
        using var workspace = new ToolExecutionWorkspace();
        var editor = workspace.CreateDocument("Owner").DocumentViewModel.CadEditor;
        var ole = (CadOleObject)CadEntityTestCases.Add(editor.Document, TestEntityKind.Ole);
        editor.Document.MoveEntityToBlock(ole.Id, BlockId.PaperSpace);
        var history = editor.CreateDocumentHistorySnapshot();
        await Execute(new(workspace), "set_ole_object_data", new { entity_id = ole.Id.Value, ole_base64 = "BAUG" }, success: false);
        Assert.Equal(new byte[] { 1, 2, 3 }, ole.CopyOleBytes());
        Assert.True(editor.DocumentHistoryEquals(history));
    }

    private static void AssertText(CadEntity entity, string expected, bool inverted)
    {
        var (text, isInverted) = entity switch
        {
            CadText item => (item.Text, item.IsInverted),
            CadShapeText item => (item.Text, item.IsInverted),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, text);
        Assert.Equal(inverted, isInverted);
    }

    private sealed class RecordingImageImport : IImageImportService
    {
        public string? RequestedPath { get; private set; }
        public CadImageImportData LoadFromFile(string filePath)
        {
            RequestedPath = filePath;
            return new(1, 1, 4, [0, 0, 255, 255], "image/png", "fixture.png");
        }
        public CadImageImportData? LoadFromClipboard() => null;
        public string CreatePngDataUrl(CadImageImportData image) => throw new NotSupportedException();
    }
}
