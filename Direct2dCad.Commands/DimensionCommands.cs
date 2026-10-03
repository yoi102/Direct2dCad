using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
namespace Direct2dCad.Commands;

public sealed class AddDimensionCommand(CadDimensionDefinition definition, LayerId? layer = null, BlockId? owner = null) : ICadCommand
{
    private readonly CadDimensionDefinition capturedDefinition = definition.Copy();
    public string Name => "Add Dimension";
    public EntityId? CreatedEntityId { get; private set; }
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadEntityAccessPolicy.EnsureCanAddToLayer(document,layer??LayerId.Default);
        var entity=CreatedEntityId is { } id && document.TryGetEntity(id,out var found) && found is CadDimension existing
            ? existing : document.AddDimension(capturedDefinition,layer,owner);
        entity.Restore();entity.RefreshAssociation(document);CreatedEntityId=entity.Id;
        return CadDocumentChangeSet.ForEntity(entity.Id,CadEntityChangeKind.Created|CadEntityChangeKind.Geometry|CadEntityChangeKind.Appearance);
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if(CreatedEntityId is not { } id)return CadDocumentChangeSet.Empty;
        document.GetEntity(id).Erase();return CadDocumentChangeSet.ForEntity(id,CadEntityChangeKind.Deleted);
    }
}
public sealed class SetDimensionCommand(EntityId id,CadDimensionDefinition definition) : ICadCommand
{
    private readonly CadDimensionDefinition capturedDefinition = definition.Copy();
    private CadDimensionDefinition? previous;
    public string Name=>"Set Dimension";
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        CadCommandEntityAccess.EnsureEditable(document,id);
        var entity=(CadDimension)document.GetEntity(id);previous=entity.Definition.Copy();entity.SetDefinition(capturedDefinition);entity.RefreshAssociation(document);
        return CadDocumentChangeSet.ForEntity(id,CadEntityChangeKind.Geometry|CadEntityChangeKind.Appearance);
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if(previous is null)return CadDocumentChangeSet.Empty;
        var entity=(CadDimension)document.GetEntity(id);entity.SetDefinition(previous);entity.RefreshAssociation(document);
        return CadDocumentChangeSet.ForEntity(id,CadEntityChangeKind.Geometry|CadEntityChangeKind.Appearance);
    }
}
