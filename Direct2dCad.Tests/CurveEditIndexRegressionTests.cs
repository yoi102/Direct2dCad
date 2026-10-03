using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.HitTesting;

namespace Direct2dCad.Tests;

public sealed class CurveEditIndexRegressionTests
{
    [Theory]
    [InlineData("break")]
    [InlineData("break-gap")]
    [InlineData("trim-middle")]
    [InlineData("trim-end")]
    [InlineData("extend")]
    [InlineData("offset")]
    [InlineData("join")]
    [InlineData("fillet")]
    [InlineData("chamfer")]
    public void CurveEditsKeepLiveResultsInTheRenderAndHitTestIndexThroughHistory(string operation)
    {
        var document = CadDocument.Create(operation);
        var line = document.AddLine(default, new(20, 0));
        var plan = operation switch
        {
            "break" => CadCurveEditing.Break(line, new(7, 0)),
            "break-gap" => CadCurveEditing.Break(line, new(7, 0), new(13, 0)),
            "trim-middle" => CadCurveEditing.Trim(line,
                [document.AddLine(new(7, -10), new(7, 10)), document.AddLine(new(13, -10), new(13, 10))], new(10, 0)),
            "trim-end" => CadCurveEditing.Trim(line,
                [document.AddLine(new(7, -10), new(7, 10))], new(15, 0)),
            "extend" => CadCurveEditing.Extend(line,
                [document.AddLine(new(30, -10), new(30, 10))], new(19, 0)),
            "offset" => CadCurveEditing.Offset(line, 3, new(10, 8)),
            "join" => CadCurveEditing.Join([line, document.AddLine(new(20, 0), new(40, 0))]),
            _ => CadCurveEditing.Corner(line, document.AddLine(default, new(0, 20)),
                new(18, 0), new(0, 18), 3, 4, operation == "fillet")
        };
        var editor = new CadEditor(document);
        var command = new EditCurvesCommand(operation, plan);

        editor.Execute(command);
        AssertIndexMatchesDocument(editor);
        AssertPublishedLifetimeMatchesDocument(editor);
        var resultIds = command.ResultEntityIds.ToArray();
        Assert.NotEmpty(resultIds);
        foreach (var id in resultIds)
        {
            var entity = document.GetEntity(id);
            var point = CadPlanarCurves.Get(entity)[0].At(.5);
            Assert.Contains(id, editor.SpatialIndex.Query(entity.OwnerBlockId, CadRectD.FromCenter(point, 1, 1)));
            Assert.True(CadEntityHitTester.HitTestEdge(document, entity, point, .01, out _));
        }

        editor.Undo();
        AssertIndexMatchesDocument(editor);
        AssertPublishedLifetimeMatchesDocument(editor);
        Assert.False(line.IsErased);
        Assert.Equal(new CadPointD(20, 0), line.End);

        editor.Redo();
        AssertIndexMatchesDocument(editor);
        AssertPublishedLifetimeMatchesDocument(editor);
        Assert.Equal(resultIds, command.ResultEntityIds);
    }

    [Fact]
    public void BreakingABlockChildDoesNotDeleteItsLiveNestedReferences()
    {
        var document = CadDocument.Create("nested break");
        var inner = document.CreateBlockDefinition("inner", default);
        var outer = document.CreateBlockDefinition("outer", default);
        var line = document.AddLine(default, new(20, 0));
        document.MoveEntityToBlock(line.Id, inner);
        var nested = document.AddBlockReference(inner, default);
        document.MoveEntityToBlock(nested.Id, outer);
        var reference = document.AddBlockReference(outer, new(100, 100));
        var editor = new CadEditor(document);
        var command = new EditCurvesCommand("break", CadCurveEditing.Break(line, new(7, 0), new(13, 0)));

        void Verify()
        {
            AssertIndexMatchesDocument(editor);
            foreach (var id in new[] { nested.Id, reference.Id })
            {
                var change = Assert.Single(editor.LastDocumentChanges.EntityChanges, c => c.EntityId == id);
                Assert.True(change.Kind.HasFlag(CadEntityChangeKind.Geometry));
                Assert.Equal(CadEntityChangeKind.None, change.Kind & (CadEntityChangeKind.Created | CadEntityChangeKind.Deleted));
            }
        }

        editor.Execute(command); Verify();
        editor.Undo(); Verify();
        editor.Redo(); Verify();
        Assert.Equal(2, command.ResultEntityIds.Count);
        Assert.All(command.ResultEntityIds, id => Assert.Equal(inner, document.GetEntity(id).OwnerBlockId));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("rotate")]
    [InlineData("scale")]
    [InlineData("mirror")]
    [InlineData("duplicate")]
    [InlineData("rectangular-array")]
    [InlineData("polar-array")]
    public void OtherEditCommandsKeepTheirIndexConsistentThroughHistory(string operation)
    {
        var document = CadDocument.Create(operation);
        var source = document.AddLine(new(10, 10), new(20, 10));
        var editor = new CadEditor(document);
        ICadCommand command = operation switch
        {
            "move" => new MoveEntitiesCommand([source.Id], new(-20, 15)),
            "rotate" => new RotateEntitiesCommand([source.Id], default, Math.PI / 2),
            "scale" => new ScaleEntitiesCommand([source.Id], default, 2),
            "mirror" => new MirrorEntitiesCommand([source.Id], default, 0),
            "duplicate" => new DuplicateEntitiesCommand([source.Id], new(0, 30)),
            "rectangular-array" => new ArrayEntitiesCommand([source.Id], 2, 3, 40, 40),
            _ => new ArrayEntitiesCommand([source.Id], default, 4, Math.PI * 2)
        };
        void Verify()
        {
            AssertIndexMatchesDocument(editor);
            AssertPublishedLifetimeMatchesDocument(editor);
            foreach (var line in document.Entities.Values.OfType<CadLine>().Where(e => !e.IsErased))
                Assert.Contains(line.Id, editor.SpatialIndex.Query(line.OwnerBlockId,
                    CadRectD.FromCenter(line.Start + (line.End - line.Start) * .5, 1, 1)));
        }
        editor.Execute(command); Verify();
        editor.Undo(); Verify();
        Assert.Single(document.Entities.Values, e => !e.IsErased);
        editor.Redo(); Verify();
    }

    private static void AssertIndexMatchesDocument(CadEditor editor)
    {
        foreach (var owner in editor.Document.Entities.Values.Select(e => e.OwnerBlockId).Distinct())
        {
            var expected = editor.Document.Entities.Values.Where(e => e.OwnerBlockId == owner && !e.IsErased && e.IsVisible)
                .Select(e => e.Id).OrderBy(id => id.Value).ToArray();
            var actual = editor.SpatialIndex.Query(owner, CadRectD.FromXYWH(-100, -100, 400, 400))
                .OrderBy(id => id.Value).ToArray();
            Assert.Equal(expected, actual);
        }
    }

    private static void AssertPublishedLifetimeMatchesDocument(CadEditor editor)
    {
        foreach (var change in editor.LastDocumentChanges.EntityChanges)
        {
            var entity = editor.Document.GetEntity(change.EntityId);
            Assert.False(change.Kind.HasFlag(CadEntityChangeKind.Created) && change.Kind.HasFlag(CadEntityChangeKind.Deleted));
            Assert.Equal(entity.IsErased, change.Kind.HasFlag(CadEntityChangeKind.Deleted));
        }
    }
}
