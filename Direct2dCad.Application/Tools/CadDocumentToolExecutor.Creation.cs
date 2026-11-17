using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private string AddLine(JsonElement arguments)
    {
        var command = CreateLineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddLineCommand CreateLineCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "x1"), RequiredDouble(arguments, "y1")),
            new CadPointD(RequiredDouble(arguments, "x2"), RequiredDouble(arguments, "y2")),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Line"));

    private string AddCircle(JsonElement arguments)
    {
        var command = CreateCircleCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddCircleCommand CreateCircleCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius"),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Circle"));

    private string AddRectangle(JsonElement arguments)
    {
        var command = CreateRectangleCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddRectangleCommand CreateRectangleCommand(JsonElement arguments)
    {
        var minX = RequiredDouble(arguments, "min_x");
        var minY = RequiredDouble(arguments, "min_y");
        var maxX = RequiredDouble(arguments, "max_x");
        var maxY = RequiredDouble(arguments, "max_y");
        if (maxX <= minX || maxY <= minY)
            throw new ArgumentException("Rectangle max values must be greater than min values.");
        var sharedRadius = OptionalNonNegative(arguments, "corner_radius", 0);
        var radiusX = OptionalNonNegative(arguments, "corner_radius_x", sharedRadius);
        var radiusY = OptionalNonNegative(arguments, "corner_radius_y", sharedRadius);

        return new AddRectangleCommand(
            CadRectD.FromLTRB(minX, minY, maxX, maxY),
            radiusX,
            radiusY,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Rectangle"));
    }

    private string AddText(JsonElement arguments)
    {
        var command = CreateTextCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddTextCommand CreateTextCommand(JsonElement arguments)
    {
        var text = RequiredString(arguments, "text");
        var rotation = OptionalDouble(arguments, "rotation_degrees", 0) * Math.PI / 180.0;
        return new AddTextCommand(
            text,
            new CadPointD(RequiredDouble(arguments, "x"), RequiredDouble(arguments, "y")),
            RequiredPositive(arguments, "height"),
            rotation,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Text"));
    }

    private string AddShapeText(JsonElement arguments)
    {
        var command = CreateShapeTextCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddShapeTextCommand CreateShapeTextCommand(JsonElement arguments) => new(
        RequiredString(arguments, "text"),
        new CadPointD(RequiredDouble(arguments, "x"), RequiredDouble(arguments, "y")),
        RequiredPositive(arguments, "height"),
        OptionalDouble(arguments, "rotation_degrees", 0) * Math.PI / 180.0,
        OptionalPositive(arguments, "width_factor", CadStrokeFont.DefaultWidthFactor),
        OptionalNonNegative(arguments, "character_spacing_factor", CadStrokeFont.DefaultCharacterSpacingFactor),
        OptionalDouble(arguments, "oblique_angle_degrees", 0) * Math.PI / 180.0,
        ResolveLayer(arguments),
        name: ResolveName(arguments, "ShapeText"));

    private string AddPolyline(JsonElement arguments)
    {
        var command = CreatePolylineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddPolylineCommand CreatePolylineCommand(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("points", out var pointElements) || pointElements.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("points must be an array.");
        var points = pointElements.EnumerateArray()
            .Select(point => new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y")))
            .ToArray();
        var closed = OptionalBool(arguments, "closed");
        return new AddPolylineCommand(
            points,
            closed,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Polyline"));
    }

    private string AddArc(JsonElement arguments)
    {
        var command = CreateArcCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddArcCommand CreateArcCommand(JsonElement arguments)
    {
        var sweepDegrees = RequiredDouble(arguments, "sweep_angle_degrees");
        if (Math.Abs(sweepDegrees) <= 1e-9 || Math.Abs(sweepDegrees) > 360.0)
            throw new ArgumentOutOfRangeException("sweep_angle_degrees", "Sweep must be non-zero and no greater than 360 degrees.");
        return new AddArcCommand(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius"),
            RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0,
            sweepDegrees * Math.PI / 180.0,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Arc"));
    }

    private string AddEllipse(JsonElement arguments)
    {
        var command = CreateEllipseCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddEllipseCommand CreateEllipseCommand(JsonElement arguments) => new(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius_x"),
            RequiredPositive(arguments, "radius_y"),
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Ellipse"));

    private string AddEllipseArc(JsonElement arguments)
    {
        var command = CreateEllipseArcCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddEllipseArcCommand CreateEllipseArcCommand(JsonElement arguments)
    {
        var sweepDegrees = RequiredDouble(arguments, "sweep_angle_degrees");
        if (Math.Abs(sweepDegrees) <= 1e-9 || Math.Abs(sweepDegrees) > 360.0)
            throw new ArgumentOutOfRangeException("sweep_angle_degrees", "Sweep must be non-zero and no greater than 360 degrees.");
        return new AddEllipseArcCommand(
            new CadPointD(RequiredDouble(arguments, "center_x"), RequiredDouble(arguments, "center_y")),
            RequiredPositive(arguments, "radius_x"),
            RequiredPositive(arguments, "radius_y"),
            RequiredDouble(arguments, "start_angle_degrees") * Math.PI / 180.0,
            sweepDegrees * Math.PI / 180.0,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "EllipseArc"));
    }

    private string AddPolygon(JsonElement arguments)
    {
        var command = CreatePolygonCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddPolygonCommand CreatePolygonCommand(JsonElement arguments)
    {
        var points = RequiredPoints(arguments, "points", minimumCount: 3);
        return new AddPolygonCommand(
            points,
            ResolveLayer(arguments),
            name: ResolveName(arguments, "Polygon"));
    }

    private string AddSpline(JsonElement arguments)
    {
        var command = CreateSplineCommand(arguments);
        return ExecuteCreate(command, () => command.CreatedEntityId);
    }

    private AddSplineCommand CreateSplineCommand(JsonElement arguments)
    {
        return new AddSplineCommand(
            CadSplineTools.Parse(arguments),

            ResolveLayer(arguments),
            name: ResolveName(arguments, "Spline"));
    }
}
