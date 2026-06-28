using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

internal static class CadEntityTransform
{
    internal static void Translate(CadEntity entity, CadVectorD delta)
    {
        switch (entity)
        {
            case CadLine line:
                line.SetGeometry(line.Start + delta, line.End + delta);
                break;
            case CadCircle circle:
                circle.SetCenter(circle.Center + delta);
                break;
            case CadRectangle rectangle:
                rectangle.SetBounds(rectangle.Bounds.Translate(delta));
                break;
            case CadArc arc:
                arc.SetCenter(arc.Center + delta);
                break;
            case CadPolyline polyline:
                polyline.ReplacePoints(polyline.Points.Select(x => x + delta));
                break;
            case CadText text:
                text.SetPosition(text.Position + delta);
                break;
            case CadBlockReference blockReference:
                blockReference.SetPosition(blockReference.Position + delta);
                break;
            default:
                throw new NotSupportedException($"Entity type is not movable: {entity.GetType().Name}");
        }
    }
}
