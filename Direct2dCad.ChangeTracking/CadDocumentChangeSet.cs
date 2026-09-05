using Direct2dCad.Db;

namespace Direct2dCad.ChangeTracking;

[Flags]
public enum CadEntityChangeKind
{
    None = 0,
    Geometry = 1,
    Appearance = 2,
    Visibility = 4,
    Layer = 8,
    Created = 16,
    Deleted = 32,
    DrawOrder = 64,
    Fill = 128,
    Metadata = 256,
    EmbeddedData = 512,
    Opacity = 1024,
    Rotation = 2048
}

public readonly record struct CadEntityChange(
    EntityId EntityId,
    CadEntityChangeKind Kind);

[Flags]
public enum CadDocumentTableChangeKind
{
    None = 0,
    LayerMetadata = 1,
    LayerAppearance = 2,
    LayerAccess = 4,
    LayerOrder = 8,
    Styles = 16,
    BlockMetadata = 32
}

public sealed class CadDocumentChangeSet
{
    private static readonly CadDocumentChangeSet EmptyResult = new([]);

    public IReadOnlyList<CadEntityChange> EntityChanges { get; }
    /// <summary>
    /// All dependent block bounds are current and their geometry changes are included.
    /// Valid only for immediate publication against the document that produced this set.
    /// </summary>
    public bool HasResolvedBlockReferenceChanges { get; init; }
    public bool AffectsDocumentStructure { get; init; }
    public bool AffectsLayouts { get; init; }
    public bool AffectsLayoutStructure { get; init; }
    public bool AffectsViewSettings { get; init; }
    public CadDocumentTableChangeKind TableChanges { get; init; }
    public bool AffectsLayerProperties => (TableChanges & (CadDocumentTableChangeKind.LayerMetadata |
        CadDocumentTableChangeKind.LayerAppearance | CadDocumentTableChangeKind.LayerAccess |
        CadDocumentTableChangeKind.LayerOrder)) != 0;
    public bool AffectsLayerAccess => (TableChanges & CadDocumentTableChangeKind.LayerAccess) != 0;
    public bool AffectsLayerOrder => (TableChanges & CadDocumentTableChangeKind.LayerOrder) != 0;
    public bool DocumentChanged =>
        EntityChanges.Count > 0 ||
        TableChanges != CadDocumentTableChangeKind.None ||
        AffectsDocumentStructure ||
        AffectsLayouts ||
        AffectsLayoutStructure ||
        AffectsViewSettings;

    public static CadDocumentChangeSet Empty => EmptyResult;

    public CadDocumentChangeSet(IEnumerable<CadEntityChange> entityChanges)
    {
        EntityChanges = entityChanges?.ToArray() ?? throw new ArgumentNullException(nameof(entityChanges));
    }

    public static CadDocumentChangeSet ForEntity(EntityId entityId, CadEntityChangeKind kind)
    {
        return new CadDocumentChangeSet([new CadEntityChange(entityId, kind)]);
    }

    public static CadDocumentChangeSet ForEntities(IEnumerable<EntityId> entityIds, CadEntityChangeKind kind)
    {
        return new CadDocumentChangeSet(entityIds.Select(x => new CadEntityChange(x, kind)));
    }

    public static CadDocumentChangeSet Combine(IEnumerable<CadDocumentChangeSet> changeSets)
    {
        ArgumentNullException.ThrowIfNull(changeSets);

        var entityChanges = new Dictionary<EntityId, CadEntityChangeKind>();
        var affectsDocumentStructure = false;
        var affectsLayouts = false;
        var affectsLayoutStructure = false;
        var affectsViewSettings = false;
        var tableChanges = CadDocumentTableChangeKind.None;
        var resolvedBlockReferences = true;

        foreach (var changeSet in changeSets)
        {
            if (changeSet.DocumentChanged)
                resolvedBlockReferences &= changeSet.HasResolvedBlockReferenceChanges;
            foreach (var change in changeSet.EntityChanges)
            {
                entityChanges[change.EntityId] =
                    entityChanges.GetValueOrDefault(change.EntityId) | change.Kind;
            }

            affectsDocumentStructure |= changeSet.AffectsDocumentStructure;
            affectsLayouts |= changeSet.AffectsLayouts;
            affectsLayoutStructure |= changeSet.AffectsLayoutStructure;
            affectsViewSettings |= changeSet.AffectsViewSettings;
            tableChanges |= changeSet.TableChanges;
        }

        if (entityChanges.Count == 0 &&
            tableChanges == CadDocumentTableChangeKind.None &&
            !affectsDocumentStructure &&
            !affectsLayouts &&
            !affectsLayoutStructure &&
            !affectsViewSettings)
        {
            return Empty;
        }

        return new CadDocumentChangeSet(
            entityChanges.Select(static pair => new CadEntityChange(pair.Key, pair.Value)))
        {
            AffectsDocumentStructure = affectsDocumentStructure,
            AffectsLayouts = affectsLayouts,
            AffectsLayoutStructure = affectsLayoutStructure,
            AffectsViewSettings = affectsViewSettings,
            TableChanges = tableChanges,
            HasResolvedBlockReferenceChanges = resolvedBlockReferences
        };
    }

    public CadDocumentChangeSet WithTableChanges(CadDocumentTableChangeKind kind) => new(EntityChanges)
    {
        HasResolvedBlockReferenceChanges = HasResolvedBlockReferenceChanges,
        TableChanges = TableChanges | kind,
        AffectsDocumentStructure = AffectsDocumentStructure,
        AffectsLayouts = AffectsLayouts,
        AffectsLayoutStructure = AffectsLayoutStructure,
        AffectsViewSettings = AffectsViewSettings
    };

    public CadDocumentChangeSet WithDocumentStructureChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = true,
            TableChanges = TableChanges,
            AffectsLayouts = AffectsLayouts,
            AffectsLayoutStructure = AffectsLayoutStructure,
            AffectsViewSettings = AffectsViewSettings
        };
    }

    public CadDocumentChangeSet WithLayoutsChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = AffectsDocumentStructure,
            TableChanges = TableChanges,
            AffectsLayouts = true,
            AffectsLayoutStructure = AffectsLayoutStructure,
            AffectsViewSettings = AffectsViewSettings
        };
    }

    public CadDocumentChangeSet WithLayoutStructureChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = AffectsDocumentStructure,
            TableChanges = TableChanges,
            AffectsLayouts = true,
            AffectsLayoutStructure = true,
            AffectsViewSettings = AffectsViewSettings
        };
    }

    public CadDocumentChangeSet WithViewSettingsChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = AffectsDocumentStructure,
            TableChanges = TableChanges,
            AffectsLayouts = AffectsLayouts,
            AffectsLayoutStructure = AffectsLayoutStructure,
            AffectsViewSettings = true
        };
    }
}
