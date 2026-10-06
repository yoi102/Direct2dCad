using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class EllipticalCurveEditGuardTests
{
    [Fact]
    public void EllipticalSourcesAndPlansAreRejectedBeforeDocumentMutation()
    {
        var document = CadDocument.Create("ellipse guard");
        var ellipse = document.AddEllipse(default, 10, 4);
        var arc = document.AddEllipseArc(default, 10, 4, .2, 1.8);
        Assert.Throws<NotSupportedException>(() => CadCurveEditing.Offset(ellipse, 2, new(20, 0)));
        Assert.Throws<NotSupportedException>(() => CadCurveEditing.Break(arc, new(10, 0)));
        var line = document.AddLine(new(0, 0), new(10, 0));
        var elliptical = new CadCurveShape([CadPlanarPrimitive.EllipseArc(default, 10, 4, .6, 0, Math.PI)]);
        var command = new EditCurvesCommand("invalid ellipse plan", CadCurveEditPlan.Replace(line.Id, elliptical));
        Assert.Throws<NotSupportedException>(() => command.Execute(document));
        Assert.Equal(3, document.Entities.Count);
        Assert.False(line.IsErased); Assert.False(ellipse.IsErased); Assert.False(arc.IsErased);
        Assert.Equal(new CadPointD(10, 0), line.End);
    }
}
