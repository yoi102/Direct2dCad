using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Tests;

public sealed class CommandExecutionPolicyTests
{
    [Theory]
    [InlineData("single")]
    [InlineData("range")]
    [InlineData("batch")]
    [InlineData("atomic")]
    public void EveryExecutionRouteRejectsReadOnlyDocumentBeforeCreatingALayer(string route)
    {
        var editor = new CadEditor(CadDocument.Create("Compatibility"));
        editor.Document.SetCompatibilityReadOnly("Unsupported version");
        var before = editor.CreateDocumentHistorySnapshot();
        var command = new CreateLayerCommand("New layer", CadColor.Green, CadLineWeight.Default);
        var batch = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (route)
            {
                case "single": editor.Execute(command); break;
                case "range": editor.ExecuteRange([command]); break;
                case "batch": editor.ExecuteInBatch(command, batch); break;
                default:
                    editor.DocumentCommands.ExecuteAtomicBatch(batch, () => editor.ExecuteInBatch(command, batch));
                    break;
            }
        });

        Assert.Single(editor.Document.Layers);
        Assert.True(editor.DocumentHistoryEquals(before));
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.Equal(0, editor.DocumentChangeVersion);
    }

    [Fact]
    public void ReadOnlyDocumentStillAllowsQueriesInsideAnAtomicScope()
    {
        var editor = new CadEditor(CadDocument.Create("Compatibility"));
        editor.Document.SetCompatibilityReadOnly("Unsupported version");

        Assert.Equal(1, editor.DocumentCommands.ExecuteAtomicBatch(Guid.NewGuid(), () => editor.Document.Layers.Count));
    }

    [Fact]
    public void BatchPayloadBudgetRetainsNewestBatchButExpiresItWhenASubsequentCommandArrives()
    {
        var editor = new CadEditor(CadDocument.Create("Images"));
        editor.DocumentHistorySettings.MaximumUndoBytes = 1024;
        var first = CreateImage();
        var second = CreateImage();

        editor.ExecuteRange([first, second]);

        Assert.True(editor.DocumentCommands.EstimatedHistoryBytes >= 2 * 64 * 1024);
        editor.Undo();
        Assert.True(editor.Document.GetEntity(first.CreatedEntityId!.Value).IsErased);
        Assert.True(editor.Document.GetEntity(second.CreatedEntityId!.Value).IsErased);
        Assert.False(editor.DocumentCommands.CanUndo);
        editor.Redo();
        editor.Execute(new SetBackgroundColorCommand(CadColor.Green));
        Assert.True(editor.DocumentCommands.EstimatedHistoryBytes < 1024);
        editor.Undo();
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.False(editor.Document.GetEntity(first.CreatedEntityId.Value).IsErased);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedLayerDeletionFailurePreservesPriorityHistoryAndNotifications(bool inBatch)
    {
        var editor = new CadEditor(CadDocument.Create("Protected"));
        editor.Document.DocumentSettings.LayerDrawingPriority.SetPriority(LayerId.Default, 42);
        var before = editor.CreateDocumentHistorySnapshot();
        var notifications = 0;
        editor.DocumentChanged += (_, _) => notifications++;
        var command = new DeleteLayerCommand(LayerId.Default);

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (inBatch)
                editor.ExecuteRange([new SetBackgroundColorCommand(CadColor.Green), command]);
            else
                editor.Execute(command);
        });

        Assert.Equal(42, editor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(LayerId.Default));
        Assert.True(editor.DocumentHistoryEquals(before));
        Assert.Equal(0, notifications);
        Assert.Equal(0, editor.DocumentChangeVersion);
        Assert.True(editor.DocumentCommands.IsHistoryHealthy);
    }

    private static AddImageCommand CreateImage() => new(
        CadRectD.FromXYWH(0, 0, 10, 10), 128, 128, 512, new byte[64 * 1024]);
}
