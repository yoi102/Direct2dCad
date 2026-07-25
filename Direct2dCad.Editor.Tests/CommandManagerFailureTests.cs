using Direct2dCad.Commands;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Tests;

public sealed class CommandManagerFailureTests
{
    [Fact]
    public void ExecuteRange_WhenLaterCommandFailsKeepsSuccessfulCommandUndoable()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));
        var successful = new TestAddLineCommand();
        var failing = new ThrowingCommand();

        Assert.Throws<InvalidOperationException>(() =>
            editor.ExecuteRange([successful, failing]));

        var line = Assert.IsType<CadLine>(
            editor.Document.GetEntity(Assert.IsType<EntityId>(successful.CreatedEntityId)));
        Assert.False(line.IsErased);
        Assert.True(editor.DocumentCommands.CanUndo);

        editor.UndoDocument();

        Assert.True(line.IsErased);
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Fact]
    public void Execute_WhenCommandFailsDoesNotCreateHistoryEntry()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));

        Assert.Throws<InvalidOperationException>(() =>
            editor.Execute(new ThrowingCommand()));

        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.False(editor.DocumentCommands.CanRedo);
    }

    private sealed class TestAddLineCommand : ICadCommand
    {
        public string Name => "Test Add Line";
        public EntityId? CreatedEntityId { get; private set; }

        public CadDocumentChangeSet Execute(CadDocument document)
        {
            if (CreatedEntityId is { } id)
            {
                document.GetEntity(id).Restore();
                return CadDocumentChangeSet.ForEntity(id, CadEntityChangeKind.Created);
            }

            var line = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
            CreatedEntityId = line.Id;
            return CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Created);
        }

        public CadDocumentChangeSet Undo(CadDocument document)
        {
            var entity = document.GetEntity(CreatedEntityId!.Value);
            entity.Erase();
            return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted);
        }
    }

    private sealed class ThrowingCommand : ICadCommand
    {
        public string Name => "Throw";
        public CadDocumentChangeSet Execute(CadDocument document) =>
            throw new InvalidOperationException("Expected test failure.");
        public CadDocumentChangeSet Undo(CadDocument document) =>
            throw new InvalidOperationException("Expected test failure.");
    }
}
