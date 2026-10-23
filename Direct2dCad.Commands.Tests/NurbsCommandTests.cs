using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class NurbsCommandTests
{
    [Fact]
    public void TransformsUndoAndCrossDocumentClipboardPreserveRationalKnotsAndWeights()
    {
        var doc=CadDocument.Create("NURBS");
        var curve=new CadNurbsCurve(2,[new(1,0),new(1,1),new(0,1)],[2,2,2,7,7,7],[1,Math.Sqrt(.5),1]);
        var spline=doc.AddSpline(new CadSplineGeometry(curve));
        ICadCommand[] commands=[new MoveEntitiesCommand([spline.Id],new(7,-3)),new RotateEntitiesCommand([spline.Id],default,.7),
            new ScaleEntitiesCommand([spline.Id],default,2),new MirrorEntitiesCommand([spline.Id],default,.4)];
        foreach(var command in commands)
        {
            command.Execute(doc);Assert.Equal(curve.Knots,spline.Knots);Assert.Equal(curve.Weights,spline.Weights);Assert.Empty(spline.FitPoints);
            command.Undo(doc);for(var i=0;i<=20;i++)Assert.InRange(curve.EvaluateNormalized(i/20.0).DistanceTo(spline.Nurbs.EvaluateNormalized(i/20.0)),0,1e-10);
        }
        var snapshot=CadClipboardSnapshotFactory.Create(doc,[spline.Id])!;
        var target=CadDocument.Create("Paste");var paste=new PasteEntitiesCommand(snapshot,new(5,8));paste.Execute(target);
        var copy=Assert.IsType<CadSpline>(target.GetEntity(Assert.Single(paste.CreatedEntityIds)));
        Assert.Equal(curve.Knots,copy.Knots);Assert.Equal(curve.Weights,copy.Weights);
        for(var i=0;i<=20;i++) Assert.InRange((curve.EvaluateNormalized(i/20.0)+new CadVectorD(5,8)).DistanceTo(copy.Nurbs.EvaluateNormalized(i/20.0)),0,1e-10);
    }
}
