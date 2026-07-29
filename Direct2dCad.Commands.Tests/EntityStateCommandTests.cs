using Direct2dCad.ChangeTracking;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class EntityStateCommandTests
{
    [Fact]
    public void SetEntityLockedCommand_CanUnlockAndRestoreLockedEntities()
    {
        var document = CadDocument.Create("Lock state");
        var first = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        var second = document.AddCircle(new CadPointD(20, 0), 5);
        first.SetLocked(true);

        var command = new SetEntityLockedCommand([first.Id, second.Id], false);
        var execute = command.Execute(document);

        Assert.False(first.IsLocked);
        Assert.False(second.IsLocked);
        Assert.All(execute.EntityChanges, change => Assert.Equal(CadEntityChangeKind.Metadata, change.Kind));

        command.Undo(document);
        Assert.True(first.IsLocked);
        Assert.False(second.IsLocked);

        command.Execute(document);
        Assert.False(first.IsLocked);
        Assert.False(second.IsLocked);
    }
}
