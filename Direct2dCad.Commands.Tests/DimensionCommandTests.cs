using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
namespace Direct2dCad.Commands.Tests;
public sealed class DimensionCommandTests
{
    [Fact]public void CrossDocumentBlockCopyKeepsInternalDimensionAssociationAcrossUndoRedo()
    {
        var (source,line,dimension)=Create();var block=source.CreateBlockDefinition("annotated",default);
        source.MoveEntityToBlock(line.Id,block);source.MoveEntityToBlock(dimension.Id,block);
        dimension.SetDefinition(dimension.Definition with{Anchors=dimension.Definition.Anchors.Select(a=>a with{Reference=a.Reference! with{OwnerBlockId=block.Value}}).ToArray()});
        var reference=source.AddBlockReference(block,new(200,200));var target=CadDocument.Create("target");
        var paste=new PasteEntitiesCommand(CadClipboardSnapshotFactory.Create(source,[reference.Id])!,new(100,0));paste.Execute(target);
        var copied=Assert.Single(target.Entities.Values.OfType<CadDimension>());var copiedLine=Assert.Single(target.Entities.Values.OfType<CadLine>());
        Assert.Equal(CadAssociationState.Valid,copied.AssociationState);Assert.Equal(copied.OwnerBlockId,copiedLine.OwnerBlockId);
        Assert.All(copied.Definition.Anchors,a=>Assert.Equal(copiedLine.Id.Value,a.Reference!.EntityId));
        paste.Undo(target);paste.Execute(target);copied.RefreshAssociation(target);Assert.Equal(CadAssociationState.Valid,copied.AssociationState);Assert.Equal(100,copied.Measurement);
    }
    [Fact]public void CommandCapturesDefinitionAtConstructionAndCrossDocumentCopiesRebindTheirOwnSources()
    {
        var (doc,line,dim)=Create();var definition=dim.Definition;var add=new AddDimensionCommand(definition);
        definition.Anchors[1]=new(new(999,0));add.Execute(doc);Assert.Equal(100,((CadDimension)doc.GetEntity(add.CreatedEntityId!.Value)).Measurement);
        var target=CadDocument.Create("other");target.AddLine(default,new(1,0));
        var copy=new PasteEntitiesCommand(CadClipboardSnapshotFactory.Create(doc,[dim.Id,line.Id])!,new(200,0));copy.Execute(target);
        var pasted=copy.CreatedEntityIds.Select(target.GetEntity).OfType<CadDimension>().Single();var source=copy.CreatedEntityIds.Select(target.GetEntity).OfType<CadLine>().Single();
        Assert.All(pasted.Definition.Anchors,a=>Assert.Equal(source.Id.Value,a.Reference!.EntityId));Assert.Equal(CadAssociationState.Valid,pasted.AssociationState);
    }
    private static (CadDocument Document,CadLine Line,CadDimension Dimension) Create()
    {
        var doc=CadDocument.Create("d");var line=doc.AddLine(default,new(100,0));
        var dim=doc.AddDimension(new(CadDimensionKind.Aligned,[new(line.Start,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineStart)),new(line.End,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineEnd))],new(50,10),new()));return(doc,line,dim);
    }
    [Theory][InlineData(true)][InlineData(false)]public void CopyOnlyBindsToGeometryCopiedInSameOperation(bool withSource)
    {
        var (doc,line,dim)=Create();var ids=withSource?new[]{dim.Id,line.Id}:new[]{dim.Id};
        var copy=new PasteEntitiesCommand(CadClipboardSnapshotFactory.Create(doc,ids)!,new(200,0));copy.Execute(doc);
        var d=copy.CreatedEntityIds.Select(doc.GetEntity).OfType<CadDimension>().Single();
        Assert.Equal(withSource?CadAssociationState.Valid:CadAssociationState.Detached,d.AssociationState);
        if(withSource)Assert.All(d.Definition.Anchors,a=>Assert.NotEqual(line.Id.Value,a.Reference!.EntityId));
        copy.Undo(doc);Assert.True(d.IsErased);copy.Execute(doc);Assert.False(d.IsErased);Assert.Equal(100,d.Measurement);
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]public void TransformDetachAndUndoRestoresReferences(int op)
    {
        var (doc,_,d)=Create();var before=d.Definition.Copy();ICadCommand cmd=op switch {0=>new RotateEntitiesCommand([d.Id],default,.7),1=>new ScaleEntitiesCommand([d.Id],default,2),_=>new MirrorEntitiesCommand([d.Id],default,.3)};
        cmd.Execute(doc);Assert.Equal(CadAssociationState.Detached,d.AssociationState);cmd.Undo(doc);d.RefreshAssociation(doc);Assert.Equal(CadAssociationState.Valid,d.AssociationState);Assert.Equal(before.Anchors,d.Definition.Anchors);Assert.Equal(before.Placement,d.Definition.Placement);
        cmd.Execute(doc);Assert.Equal(CadAssociationState.Detached,d.AssociationState);
    }
}
