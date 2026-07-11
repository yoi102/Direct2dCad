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
    EmbeddedData = 512
}

public readonly record struct CadEntityChange(
    EntityId EntityId,
    CadEntityChangeKind Kind);

public sealed class CadDocumentChangeSet
{
    private static readonly CadDocumentChangeSet EmptyResult = new([]);

    public IReadOnlyList<CadEntityChange> EntityChanges { get; }
    public bool AffectsDocumentStructure { get; init; }
    public bool AffectsViewSettings { get; init; }
    public bool DocumentChanged => EntityChanges.Count > 0 || AffectsDocumentStructure || AffectsViewSettings;

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

    public CadDocumentChangeSet WithDocumentStructureChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = true,
            AffectsViewSettings = AffectsViewSettings
        };
    }

    public CadDocumentChangeSet WithViewSettingsChanged()
    {
        return new CadDocumentChangeSet(EntityChanges)
        {
            AffectsDocumentStructure = AffectsDocumentStructure,
            AffectsViewSettings = true
        };
    }
}
