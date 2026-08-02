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
    public void ExecuteRange_WhenLaterCommandFailsRollsBackEverySuccessfulCommand()
    {
        var editor = new CadEditor(CadDocument.Create("Test"));
        var successful = new TestAddLineCommand();
        var failing = new ThrowingCommand();

        Assert.Throws<InvalidOperationException>(() =>
            editor.ExecuteRange([successful, failing]));

        var line = Assert.IsType<CadLine>(
            editor.Document.GetEntity(Assert.IsType<EntityId>(successful.CreatedEntityId)));
        Assert.True(line.IsErased);
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.False(editor.DocumentCommands.CanRedo);
        Assert.True(editor.Document.Entities.Values.All(entity => entity.IsErased));
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

    [Fact]
    public void Undo_WhenLaterUndoFailsRestoresDocumentAndKeepsHistory()
    {
        var editor = new CadEditor(CadDocument.Create("Undo failure"));
        var failing = new ThrowingUndoCommand();
        var restorable = new TestAddLineCommand();
        editor.ExecuteRange([failing, restorable]);

        Assert.Throws<InvalidOperationException>(() => editor.UndoDocument());

        Assert.False(editor.Document.GetEntity(failing.CreatedEntityId!.Value).IsErased);
        Assert.False(editor.Document.GetEntity(restorable.CreatedEntityId!.Value).IsErased);
        Assert.True(editor.DocumentCommands.CanUndo);
        Assert.False(editor.DocumentCommands.CanRedo);
    }

    [Fact]
    public void Redo_WhenLaterExecuteFailsRestoresDocumentAndKeepsRedoHistory()
    {
        var editor = new CadEditor(CadDocument.Create("Redo failure"));
        var restorable = new TestAddLineCommand();
        var failing = new ThrowingRedoCommand();
        editor.ExecuteRange([restorable, failing]);
        editor.UndoDocument();

        Assert.Throws<InvalidOperationException>(() => editor.RedoDocument());

        Assert.True(editor.Document.GetEntity(restorable.CreatedEntityId!.Value).IsErased);
        Assert.True(editor.Document.GetEntity(failing.CreatedEntityId!.Value).IsErased);
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.True(editor.DocumentCommands.CanRedo);
    }

    [Fact]
    public void Undo_WhenRecoveryFailsBlocksFurtherHistoryOperations()
    {
        var editor = new CadEditor(CadDocument.Create("Unrecoverable undo"));
        var throwingUndo = new ThrowingUndoCommand();
        var throwingRestore = new ThrowingRestoreCommand();
        editor.ExecuteRange([throwingUndo, throwingRestore]);

        Assert.Throws<InvalidOperationException>(() => editor.UndoDocument());

        Assert.False(editor.DocumentCommands.IsHistoryHealthy);
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.False(editor.DocumentCommands.CanRedo);
        Assert.Throws<InvalidOperationException>(() =>
            editor.Execute(new TestAddLineCommand()));
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

    private sealed class ThrowingUndoCommand : ICadCommand
    {
        public string Name => "Throwing Undo";
        public EntityId? CreatedEntityId { get; private set; }

        public CadDocumentChangeSet Execute(CadDocument document)
        {
            if (CreatedEntityId is { } id)
            {
                document.GetEntity(id).Restore();
                return CadDocumentChangeSet.ForEntity(id, CadEntityChangeKind.Created);
            }

            var line = document.AddLine(new CadPointD(20, 0), new CadPointD(30, 0));
            CreatedEntityId = line.Id;
            return CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Created);
        }

        public CadDocumentChangeSet Undo(CadDocument document) =>
            throw new InvalidOperationException("Expected undo failure.");
    }

    private sealed class ThrowingRedoCommand : ICadCommand
    {
        public string Name => "Throwing Redo";
        public EntityId? CreatedEntityId { get; private set; }
        private bool _hasExecuted;

        public CadDocumentChangeSet Execute(CadDocument document)
        {
            if (_hasExecuted)
                throw new InvalidOperationException("Expected redo failure.");

            var line = document.AddLine(new CadPointD(40, 0), new CadPointD(50, 0));
            CreatedEntityId = line.Id;
            _hasExecuted = true;
            return CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Created);
        }

        public CadDocumentChangeSet Undo(CadDocument document)
        {
            var entity = document.GetEntity(CreatedEntityId!.Value);
            entity.Erase();
            return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted);
        }
    }

    private sealed class ThrowingRestoreCommand : ICadCommand
    {
        public string Name => "Throwing Restore";
        public EntityId? CreatedEntityId { get; private set; }
        private bool _hasExecuted;

        public CadDocumentChangeSet Execute(CadDocument document)
        {
            if (_hasExecuted)
                throw new InvalidOperationException("Expected recovery failure.");

            var line = document.AddLine(new CadPointD(60, 0), new CadPointD(70, 0));
            CreatedEntityId = line.Id;
            _hasExecuted = true;
            return CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Created);
        }

        public CadDocumentChangeSet Undo(CadDocument document)
        {
            var entity = document.GetEntity(CreatedEntityId!.Value);
            entity.Erase();
            return CadDocumentChangeSet.ForEntity(entity.Id, CadEntityChangeKind.Deleted);
        }
    }
}
