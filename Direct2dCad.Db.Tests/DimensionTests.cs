using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
namespace Direct2dCad.Db.Tests;

public sealed class DimensionTests
{
    [Fact]public void AnnotationGlyphsHaveReadableWorldOrientation()
    {
        var dimension=CadDocument.Create("font").AddDimension(new(CadDimensionKind.Aligned,[new(default),new(new(100,0))],new(50,20),new(),TextOverride:"F"));
        var letter=dimension.Strokes.TakeLast(3).ToArray();
        Assert.True(letter[0].Start.Y>letter[0].End.Y); // F's stem starts at its top in world space.
        Assert.True(letter[1].Start.Y>letter[2].Start.Y); // Top bar is above the middle bar.
    }
    [Fact] public void DefinitionIsAnIndependentValueAndDegenerateAssociationKeepsLastGeometry()
    {
        var doc=CadDocument.Create("value");var line=doc.AddLine(default,new(100,0));
        var anchors=new[]{new CadDimensionAnchor(line.Start,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineStart)),new CadDimensionAnchor(line.End,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineEnd))};
        var dimension=doc.AddDimension(new(CadDimensionKind.Aligned,anchors,new(50,10),new()));
        anchors[1]=new(new(999,0));var returned=dimension.Definition;returned.Anchors[1]=new(new(888,0));
        Assert.Equal(100,dimension.Measurement);Assert.Equal(new CadPointD(100,0),dimension.Definition.Anchors[1].Point);
        line.SetGeometry(default,new(1e-10,0));dimension.RefreshAssociation(doc);
        Assert.Equal(CadAssociationState.Broken,dimension.AssociationState);Assert.Equal(100,dimension.Measurement);
    }
    [Theory]
    [InlineData(CadDimensionKind.LinearX,3)][InlineData(CadDimensionKind.LinearY,4)]
    [InlineData(CadDimensionKind.Aligned,5)][InlineData(CadDimensionKind.Radius,5)]
    [InlineData(CadDimensionKind.Diameter,10)][InlineData(CadDimensionKind.Leader,5)]
    public void MeasurementsUseCanonicalDistances(CadDimensionKind kind,double expected)
    {
        var d=CadDocument.Create("dimensions").AddDimension(new(kind,[new(default),new(new(3,4))],new(5,10),new()));
        Assert.Equal(expected,d.Measurement,9);Assert.NotEmpty(d.Strokes);Assert.False(d.Bounds.IsEmpty);
    }
    [Fact] public void AssociationFailurePreservesWholeLastGeometryAndUndoCanRestoreIt()
    {
        var doc=CadDocument.Create("d");var line=doc.AddLine(default,new(100,0));
        var d=doc.AddDimension(new(CadDimensionKind.Aligned,[new(line.Start,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineStart)),new(line.End,new(line.Id.Value,line.OwnerBlockId.Value,CadReferenceFeature.LineEnd))],new(50,10),new()));
        line.SetGeometry(new(5,0),new(125,0));Assert.True(d.RefreshAssociation(doc));Assert.Equal(120,d.Measurement);
        line.Erase();Assert.True(d.RefreshAssociation(doc));Assert.Equal(CadAssociationState.Broken,d.AssociationState);Assert.Contains("!",d.DisplayText);
        line.SetGeometry(new(10,0),new(130,0));d.RefreshAssociation(doc);Assert.Equal(new CadPointD(5,0),d.Definition.Anchors[0].Point);
        line.Restore();d.RefreshAssociation(doc);Assert.Equal(CadAssociationState.Valid,d.AssociationState);Assert.Equal(new CadPointD(10,0),d.Definition.Anchors[0].Point);
    }
    [Fact] public void AngularUnitOverrideAndPaperScaleAreExplicit()
    {
        var doc=CadDocument.Create("d");var angle=doc.AddDimension(new(CadDimensionKind.Angular,[new(default),new(new(10,0)),new(new(0,10))],new(5,5),new()));Assert.Equal(90,angle.Measurement,9);
        var d=doc.AddDimension(new(CadDimensionKind.Aligned,[new(default),new(new(25.4,0))],new(10,10),new(Unit:CadUnit.Inch),100,"TYP"));
        Assert.Contains("*",d.DisplayText);Assert.True(d.HasTextOverride);Assert.Equal(250,d.Definition.Style.TextHeight*d.Definition.AnnotationScale);
        Assert.Throws<ArgumentException>(()=>d.SetDefinition(d.Definition with {AnnotationScale=double.NaN}));
    }
    [Theory][InlineData("A4",297,210,1)][InlineData("A3-100",420,297,.01)][InlineData("Letter",279.4,215.9,1)]
    public void TemplatesDefinePhysicalPaperAndViewportScale(string key,double w,double h,double scale)
    {var doc=CadEngineeringTemplates.Create(key);var paper=Assert.Single(doc.Layouts.Values);Assert.Equal(w,paper.PaperWidth);Assert.Equal(h,paper.PaperHeight);Assert.Equal(scale,Assert.Single(paper.Viewports).Scale);}
}
