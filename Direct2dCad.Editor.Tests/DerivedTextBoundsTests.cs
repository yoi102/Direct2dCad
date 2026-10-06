using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;

namespace Direct2dCad.Editor.Tests;

public sealed class DerivedTextBoundsTests
{
    [Fact]
    public void AcceptedMeasurementUpdatesSpatialIndexAndPreservesUndoRedoState()
    {
        var document = CadDocument.Create("Measured text");
        var text = document.AddText("Text", new CadPointD(10, 20), 5);
        var editor = new CadEditor(document);
        editor.AddLine(CadPointD.Origin, new CadPointD(1, 1));
        editor.UndoDocument();
        editor.DrainDirtyChanges();
        var history = editor.CreateDocumentHistorySnapshot();
        var historyBytes = editor.DocumentCommands.EstimatedHistoryBytes;
        var version = editor.DocumentChangeVersion;
        var publications = new List<CadDocumentChangeSet>();
        editor.DocumentChanged += (_, changes) => publications.Add(changes);
        var newlyCovered = CadRectD.FromXYWH(40, 21, 1, 1);
        Assert.DoesNotContain(text.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newlyCovered));

        var result = editor.ApplyDerivedTextBounds([Measure(text, CadRectD.FromXYWH(0, 0, 40, 8))]);

        Assert.True(result.IsDerivedGeometry);
        Assert.False(text.RequiresBoundsMeasurement);
        Assert.Equal(CadRectD.FromXYWH(10, 20, 40, 8), text.Bounds);
        Assert.Contains(text.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newlyCovered));
        Assert.True(Assert.Single(publications).IsDerivedGeometry);
        Assert.True(editor.DrainDirtyChanges().IsDerivedGeometry);
        Assert.Equal(version + 1, editor.DocumentChangeVersion);
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.Equal(historyBytes, editor.DocumentCommands.EstimatedHistoryBytes);
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.True(editor.DocumentCommands.CanRedo);
    }

    [Fact]
    public void MeasurementExpandsBlockBoundsAndPublishesDerivedReferenceChanges()
    {
        var document = CadDocument.Create("Block text");
        var blockId = document.CreateBlockDefinition("Label", CadPointD.Origin);
        var text = document.AddText("Label", CadPointD.Origin, 5);
        document.MoveEntityToBlock(text.Id, blockId);
        var reference = document.AddBlockReference(blockId, new CadPointD(100, 100));
        document.RefreshBlockReferenceBounds();
        var editor = new CadEditor(document);
        var newlyCovered = CadRectD.FromXYWH(170, 101, 1, 1);
        Assert.DoesNotContain(reference.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newlyCovered));
        CadDocumentChangeSet? published = null;
        editor.DocumentChanged += (_, changes) => published = changes;

        editor.ApplyDerivedTextBounds([Measure(text, CadRectD.FromXYWH(0, 0, 80, 8))]);

        Assert.Equal(CadRectD.FromXYWH(100, 100, 80, 8), reference.Bounds);
        Assert.Contains(reference.Id, editor.SpatialIndex.Query(BlockId.ModelSpace, newlyCovered));
        Assert.NotNull(published);
        Assert.True(published.IsDerivedGeometry);
        Assert.Contains(published.EntityChanges, change => change.EntityId == reference.Id &&
            change.Kind.HasFlag(CadEntityChangeKind.Geometry));
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("height")]
    [InlineData("style")]
    [InlineData("erased")]
    public void StaleOrDeletedTextMeasurementsDoNotChangeGeometryOrPublish(string change)
    {
        var document = CadDocument.Create("Stale measurement");
        var text = document.AddText("Original", CadPointD.Origin, 5);
        var measurement = Measure(text, CadRectD.FromXYWH(0, 0, 80, 8));
        switch (change)
        {
            case "text": text.SetText("Changed"); break;
            case "height": text.SetHeight(10); break;
            case "style": measurement = measurement with { TextStyleId = new StyleId(12345) }; break;
            case "erased": text.Erase(); break;
        }
        var editor = new CadEditor(document);
        var before = text.LocalBounds;
        var publications = 0;
        editor.DocumentChanged += (_, _) => publications++;

        var result = editor.ApplyDerivedTextBounds([measurement]);

        Assert.False(result.DocumentChanged);
        Assert.Equal(before, text.LocalBounds);
        Assert.True(text.RequiresBoundsMeasurement);
        Assert.Equal(0, publications);
        Assert.Equal(0, editor.DocumentChangeVersion);
        Assert.False(editor.DirtySet.HasChanges);
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Fact]
    public void DerivedOriginSurvivesEmptyCombiningAndDrainingButNeverMasksRealEdits()
    {
        var derived = new CadDocumentChangeSet([new(new EntityId(1), CadEntityChangeKind.Geometry)])
        { IsDerivedGeometry = true };
        var edit = CadDocumentChangeSet.ForEntity(new EntityId(2), CadEntityChangeKind.Appearance);
        Assert.True(CadDocumentChangeSet.Combine([CadDocumentChangeSet.Empty, derived]).IsDerivedGeometry);
        Assert.False(CadDocumentChangeSet.Combine([derived, edit]).IsDerivedGeometry);
        Assert.False(CadDocumentChangeSet.Combine([edit, derived]).IsDerivedGeometry);
        Assert.False(derived.WithViewSettingsChanged().IsDerivedGeometry);

        var dirty = new DirtySet();
        dirty.Add(CadDocumentChangeSet.Empty);
        dirty.Add(derived);
        Assert.True(dirty.Snapshot().IsDerivedGeometry);
        dirty.Add(edit);
        Assert.False(dirty.Drain().IsDerivedGeometry);
        Assert.False(dirty.Drain().IsDerivedGeometry);
        dirty.Add(derived);
        Assert.True(dirty.Drain().IsDerivedGeometry);
        dirty.Add(derived);
        dirty.Add(new EntityId(3), CadEntityChangeKind.Geometry);
        Assert.False(dirty.Drain().IsDerivedGeometry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredPublicationRetainsDerivedOriginOnlyWithoutRealEdits(bool includeRealEdit)
    {
        var document = CadDocument.Create("Deferred measurement");
        var text = document.AddText("Text", CadPointD.Origin, 5);
        var editor = new CadEditor(document);
        var publications = new List<CadDocumentChangeSet>();
        editor.DocumentChanged += (_, changes) => publications.Add(changes);
        var batch = Guid.NewGuid();

        editor.DocumentCommands.ExecuteAtomicBatch(batch, () =>
        {
            editor.ApplyDerivedTextBounds([Measure(text, CadRectD.FromXYWH(0, 0, 40, 8))]);
            if (includeRealEdit)
                editor.ExecuteInBatch(new SetBackgroundColorCommand(CadColor.Green), batch);
            Assert.Empty(publications);
            return true;
        });

        Assert.Equal(!includeRealEdit, Assert.Single(publications).IsDerivedGeometry);
        Assert.Equal(!includeRealEdit, editor.DrainDirtyChanges().IsDerivedGeometry);
        Assert.Equal(includeRealEdit, editor.DocumentCommands.CanUndo);
    }

    [Fact]
    public void DerivedGeometryDoesNotSplitTheCurrentPanGestureHistory()
    {
        var document = CadDocument.Create("Pan and measurement");
        var text = document.AddText("Text", CadPointD.Origin, 5);
        var editor = new CadEditor(document);
        var gesture = Guid.NewGuid();
        editor.EditorCommands.ExecuteCoalesced(new PanViewportCommand(new CadVectorD(10, 0)), gesture);

        editor.ApplyDerivedTextBounds([Measure(text, CadRectD.FromXYWH(0, 0, 40, 8))]);
        editor.EditorCommands.ExecuteCoalesced(new PanViewportCommand(new CadVectorD(20, 0)), gesture);

        editor.UndoEditor();
        Assert.Equal(CadPointD.Origin, editor.Viewport.Offset);
        Assert.False(editor.EditorCommands.CanUndo);
        editor.RedoEditor();
        Assert.Equal(new CadPointD(30, 0), editor.Viewport.Offset);
        Assert.False(editor.DocumentCommands.CanUndo);
        Assert.Equal(CadRectD.FromXYWH(0, 0, 40, 8), text.LocalBounds);
    }

    private static CadTextBoundsMeasurement Measure(CadText text, CadRectD bounds) =>
        new(text.Id, text.Text, text.Height, text.TextStyleId, bounds);
}
