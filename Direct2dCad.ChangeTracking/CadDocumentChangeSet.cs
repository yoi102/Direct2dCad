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

public sealed class CadDocumentChangeSet
{
    private static readonly CadDocumentChangeSet EmptyResult = new([]);

    public IReadOnlyList<CadEntityChange> EntityChanges { get; }
    public bool AffectsDocumentStructure { get; init; }
    public bool AffectsLayouts { get; init; }
    public bool AffectsLayoutStructure { get; init; }
    public bool AffectsViewSettings { get; init; }
    public bool DocumentChanged =>
        EntityChanges.Count > 0 ||
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

        foreach (var changeSet in changeSets)
        {
            foreach (var change in changeSet.EntityChanges)
            {
                entityChanges[change.EntityId] =
                    entityChanges.GetValueOrDefault(change.EntityId) | change.Kind;
            }

            affectsDocumentStructure |= changeSet.AffectsDocumentStructure;
            affectsLayouts |= changeSet.AffectsLayouts;
            affectsLayoutStructure |= changeSet.AffectsLayoutStructure;
            affectsViewSettings |= changeSet.AffectsViewSettings;
        }

        if (entityChanges.Count == 0 &&
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
            AffectsViewSettings = affectsViewSettings
        };
    }

    public CadDocumentChangeSet WithDocumentStructureChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = true,
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
            AffectsLayouts = AffectsLayouts,
            AffectsLayoutStructure = AffectsLayoutStructure,
            AffectsViewSettings = true
        };
    }
}
