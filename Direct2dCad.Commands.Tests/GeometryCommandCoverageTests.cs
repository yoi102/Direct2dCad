using Direct2dCad.Commands;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands.Tests;

public sealed class GeometryCommandCoverageTests
{
    [Fact]
    public void ScalarGeometryCommands_ExecuteAndUndoRestorePreviousGeometry()
    {
        var document = CadDocument.Create("Test");

        var circle = document.AddCircle(new CadPointD(1, 2), 3);
        var circleCommand = new SetCircleGeometryCommand(circle.Id, new CadPointD(8, 9), 7);
        circleCommand.Execute(document);
        Assert.Equal(new CadPointD(8, 9), circle.Center);
        Assert.Equal(7, circle.Radius);
        circleCommand.Undo(document);
        Assert.Equal(new CadPointD(1, 2), circle.Center);
        Assert.Equal(3, circle.Radius);

        var ellipse = document.AddEllipse(new CadPointD(2, 3), 4, 2);
        var ellipseCommand = new SetEllipseGeometryCommand(ellipse.Id, new CadPointD(9, 10), 8, 5);
        ellipseCommand.Execute(document);
        Assert.Equal(new CadPointD(9, 10), ellipse.Center);
        Assert.Equal(8, ellipse.RadiusX);
        Assert.Equal(5, ellipse.RadiusY);
        ellipseCommand.Undo(document);
        Assert.Equal(new CadPointD(2, 3), ellipse.Center);
        Assert.Equal(4, ellipse.RadiusX);
        Assert.Equal(2, ellipse.RadiusY);

        var arc = document.AddArc(new CadPointD(3, 4), 5, 0.25, 1.5);
        var arcCommand = new SetArcGeometryCommand(arc.Id, new CadPointD(11, 12), 9, 0.75, -2);
        arcCommand.Execute(document);
        Assert.Equal(new CadPointD(11, 12), arc.Center);
        Assert.Equal(9, arc.Radius);
        Assert.Equal(0.75, arc.StartAngleRadians);
        Assert.Equal(-2, arc.SweepAngleRadians);
        arcCommand.Undo(document);
        Assert.Equal(new CadPointD(3, 4), arc.Center);
        Assert.Equal(5, arc.Radius);
        Assert.Equal(0.25, arc.StartAngleRadians);
        Assert.Equal(1.5, arc.SweepAngleRadians);
    }

    [Fact]
    public void RectangleGeometryCommand_UndoRestoresBoundsAndCornerRadii()
    {
        var document = CadDocument.Create("Test");
        var originalBounds = CadRectD.FromLTRB(1, 2, 11, 8);
        var rectangle = document.AddRectangle(originalBounds, 2, 1);
        var command = new SetRectangleGeometryCommand(
            rectangle.Id,
            CadRectD.FromLTRB(20, 30, 40, 50));

        command.Execute(document);
        Assert.Equal(CadRectD.FromLTRB(20, 30, 40, 50), rectangle.Bounds);

        command.Undo(document);
        Assert.Equal(originalBounds, rectangle.Bounds);
        Assert.Equal(2, rectangle.CornerRadiusX);
        Assert.Equal(1, rectangle.CornerRadiusY);
    }

    [Fact]
    public void PointCollectionGeometryCommands_ExecuteAndUndoRestorePointsAndClosedState()
    {
        var document = CadDocument.Create("Test");
        var originalPoints = new[]
        {
            new CadPointD(0, 0),
            new CadPointD(4, 0),
            new CadPointD(4, 4)
        };
        var updatedPoints = new[]
        {
            new CadPointD(10, 10),
            new CadPointD(20, 10),
            new CadPointD(20, 20),
            new CadPointD(10, 20)
        };

        var polyline = document.AddPolyline(originalPoints, isClosed: false);
        var polylineCommand = new SetPolylineGeometryCommand(polyline.Id, updatedPoints, closed: true);
        var polylineExecute = polylineCommand.Execute(document);
        Assert.Equal(CadEntityChangeKind.Geometry, Assert.Single(polylineExecute.EntityChanges).Kind);
        Assert.Equal(updatedPoints, polyline.Points);
        Assert.True(polyline.Closed);
        polylineCommand.Undo(document);
        Assert.Equal(originalPoints, polyline.Points);
        Assert.False(polyline.Closed);
        polylineCommand.Execute(document);
        Assert.Equal(updatedPoints, polyline.Points);
        Assert.True(polyline.Closed);

        var spline = document.AddSpline(originalPoints, closed: false);
        var splineCommand = new SetSplineGeometryCommand(spline.Id, updatedPoints, closed: true);
        var splineExecute = splineCommand.Execute(document);
        Assert.Equal(CadEntityChangeKind.Geometry, Assert.Single(splineExecute.EntityChanges).Kind);
        Assert.Equal(updatedPoints, spline.FitPoints);
        Assert.True(spline.Closed);
        splineCommand.Undo(document);
        Assert.Equal(originalPoints, spline.FitPoints);
        Assert.False(spline.Closed);
        splineCommand.Execute(document);
        Assert.Equal(updatedPoints, spline.FitPoints);
        Assert.True(spline.Closed);
    }

    [Fact]
    public void GeometryCommands_RejectWrongEntityTypeWithoutMutation()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(CadPointD.Origin, new CadPointD(5, 0));
        var originalStart = line.Start;
        var originalEnd = line.End;

        var command = new SetCircleGeometryCommand(line.Id, new CadPointD(10, 10), 4);

        Assert.Throws<InvalidOperationException>(() => command.Execute(document));
        Assert.Equal(originalStart, line.Start);
        Assert.Equal(originalEnd, line.End);
    }

    [Fact]
    public void PointCollectionGeometryCommands_RejectInvalidPointCounts()
    {
        var entityId = new EntityId(42);
        var onePoint = new[] { CadPointD.Origin };
        var twoPoints = new[] { CadPointD.Origin, new CadPointD(1, 0) };

        Assert.Throws<ArgumentException>(() => new SetPolylineGeometryCommand(entityId, onePoint, closed: false));
        Assert.Throws<ArgumentException>(() => new SetPolylineGeometryCommand(entityId, twoPoints, closed: true));
        Assert.Throws<ArgumentException>(() => new SetSplineGeometryCommand(entityId, onePoint, closed: false));
        Assert.Throws<ArgumentException>(() => new SetSplineGeometryCommand(entityId, twoPoints, closed: true));
    }
}
