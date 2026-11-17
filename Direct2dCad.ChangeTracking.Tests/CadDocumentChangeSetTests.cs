using Direct2dCad.Db;

namespace Direct2dCad.ChangeTracking.Tests;

public sealed class CadDocumentChangeSetTests
{
    private static readonly EntityId A = new(1);
    private static readonly EntityId B = new(2);

    [Fact]
    public void EmptyChangeSetReportsNoDocumentChange()
    {
        Assert.False(CadDocumentChangeSet.Empty.DocumentChanged);
        Assert.Empty(CadDocumentChangeSet.Empty.EntityChanges);
        Assert.Same(CadDocumentChangeSet.Empty, CadDocumentChangeSet.Combine([]));
        Assert.Same(CadDocumentChangeSet.Empty, CadDocumentChangeSet.Combine([CadDocumentChangeSet.Empty, new([])]));
    }

    [Fact]
    public void ConstructorRejectsNullAndSnapshotsTheSequence()
    {
        Assert.Throws<ArgumentNullException>(() => new CadDocumentChangeSet(null!));
        var source = new List<CadEntityChange> { new(A, CadEntityChangeKind.Geometry) };
        var changeSet = new CadDocumentChangeSet(source);
        source.Add(new(B, CadEntityChangeKind.Created));
        Assert.Single(changeSet.EntityChanges);
    }

    [Fact]
    public void CombineMergesKindsPerEntityAndUnionsDocumentFlags()
    {
        var combined = CadDocumentChangeSet.Combine([
            CadDocumentChangeSet.ForEntity(A, CadEntityChangeKind.Geometry),
            CadDocumentChangeSet.ForEntities([A, B], CadEntityChangeKind.Appearance).WithLayoutsChanged(),
            CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.LayerOrder),
            CadDocumentChangeSet.Empty.WithViewSettingsChanged()
        ]);

        var kinds = combined.EntityChanges.ToDictionary(change => change.EntityId, change => change.Kind);
        Assert.Equal(CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance, kinds[A]);
        Assert.Equal(CadEntityChangeKind.Appearance, kinds[B]);
        Assert.True(combined.AffectsLayouts);
        Assert.False(combined.AffectsLayoutStructure);
        Assert.True(combined.AffectsViewSettings);
        Assert.True(combined.AffectsLayerOrder);
        Assert.True(combined.AffectsLayerProperties);
        Assert.False(combined.AffectsLayerAccess);
        Assert.True(combined.DocumentChanged);
    }

    [Fact]
    public void CombineKeepsResolvedAndDerivedFlagsOnlyWhenEveryChangingSetHasThem()
    {
        var allResolved = CadDocumentChangeSet.Combine([
            new([new(A, CadEntityChangeKind.Geometry)]) { HasResolvedBlockReferenceChanges = true, IsDerivedGeometry = true },
            new([new(B, CadEntityChangeKind.Geometry)]) { HasResolvedBlockReferenceChanges = true, IsDerivedGeometry = true },
            CadDocumentChangeSet.Empty // unchanged sets do not veto either flag
        ]);
        Assert.True(allResolved.HasResolvedBlockReferenceChanges);
        Assert.True(allResolved.IsDerivedGeometry);

        var mixed = CadDocumentChangeSet.Combine([
            new([new(A, CadEntityChangeKind.Geometry)]) { HasResolvedBlockReferenceChanges = true, IsDerivedGeometry = true },
            CadDocumentChangeSet.ForEntity(B, CadEntityChangeKind.Geometry)
        ]);
        Assert.False(mixed.HasResolvedBlockReferenceChanges);
        Assert.False(mixed.IsDerivedGeometry);
    }

    [Fact]
    public void LayoutStructureChangeImpliesLayoutChange()
    {
        var changeSet = CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
        Assert.True(changeSet.AffectsLayouts);
        Assert.True(changeSet.AffectsLayoutStructure);
        Assert.True(changeSet.DocumentChanged);
    }

    [Fact]
    public void WithMethodsKeepEntityChangesAndEarlierFlags()
    {
        var changeSet = CadDocumentChangeSet.ForEntity(A, CadEntityChangeKind.Deleted)
            .WithTableChanges(CadDocumentTableChangeKind.Styles)
            .WithDocumentStructureChanged()
            .WithLayoutsChanged()
            .WithViewSettingsChanged()
            .WithTableChanges(CadDocumentTableChangeKind.BlockMetadata);

        Assert.Equal(new[] { new CadEntityChange(A, CadEntityChangeKind.Deleted) }, changeSet.EntityChanges);
        Assert.Equal(CadDocumentTableChangeKind.Styles | CadDocumentTableChangeKind.BlockMetadata, changeSet.TableChanges);
        Assert.True(changeSet.AffectsDocumentStructure);
        Assert.True(changeSet.AffectsLayouts);
        Assert.True(changeSet.AffectsViewSettings);
        Assert.False(changeSet.AffectsLayerProperties);
    }

    [Fact]
    public void AddingARealChangeClearsDerivedGeometry()
    {
        // Derived geometry means "no user edit"; any added document-level change is a real edit.
        var derived = new CadDocumentChangeSet([new(A, CadEntityChangeKind.Geometry)])
        {
            IsDerivedGeometry = true,
            HasResolvedBlockReferenceChanges = true
        };

        Assert.False(derived.WithTableChanges(CadDocumentTableChangeKind.LayerAppearance).IsDerivedGeometry);
        Assert.True(derived.WithTableChanges(CadDocumentTableChangeKind.LayerAppearance).HasResolvedBlockReferenceChanges);
        Assert.False(derived.WithDocumentStructureChanged().IsDerivedGeometry);
        // Structural edits can move block bounds, so resolved block references are no longer guaranteed.
        Assert.False(derived.WithDocumentStructureChanged().HasResolvedBlockReferenceChanges);
        Assert.False(derived.WithLayoutsChanged().HasResolvedBlockReferenceChanges);
    }
}
