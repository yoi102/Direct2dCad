using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
namespace Direct2dCad.IO.Tests;
public sealed class DimensionRoundTripTests
{
    [Fact]public async Task FileAndIndependentSnapshotPreserveAssociationStyleAndOverride()
    {
        var doc=CadDocument.Create("d");var line=doc.AddLine(default,new(100,0));
        var d=doc.AddDimension(new(CadDimensionKind.Aligned,[new(line.Start,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineStart)),new(line.End,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineEnd))],new(50,20),CadDimensionStyles.Get("Fine"),100,"TYP"));
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".d2cad");var storage=new CadDocumentStorage();
        try
        {
            await storage.SaveAsync(doc,path);var loaded=await storage.LoadAsync(path);var copy=(CadDimension)loaded.GetEntity(d.Id);
            Assert.Equal(d.Definition.Anchors,copy.Definition.Anchors);Assert.Equal(d.Definition.Style,copy.Definition.Style);Assert.Equal(100,copy.Definition.AnnotationScale);Assert.Equal("TYP",copy.Definition.TextOverride);Assert.Equal(CadAssociationState.Valid,copy.AssociationState);
            var snapshot=await storage.CreateIndependentSnapshotAsync(doc,new(()=>true,ct=>ValueTask.CompletedTask));
            line.SetGeometry(default,new(200,0));d.RefreshAssociation(doc);Assert.Equal(100,((CadDimension)snapshot.GetEntity(d.Id)).Measurement);
        }
        finally { File.Delete(path); }
    }
}
