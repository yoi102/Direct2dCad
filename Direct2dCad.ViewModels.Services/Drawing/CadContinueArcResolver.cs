using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadContinueArcResolver(CadDocument document)
{
    public CadContinueArcBase Resolve()
    {
        foreach (var entity in document.Entities.Values.Reverse())
        {
            if (entity.IsErased)
                continue;

            switch (entity)
            {
                case CadArc arc:
                    var arcStart = arc.EndPoint;
                    var radiusVector = arcStart - arc.Center;
                    var arcTangent = arc.SweepAngleRadians > 0
                        ? radiusVector.Perpendicular().Normalize()
                        : (-radiusVector.Perpendicular()).Normalize();
                    return arcTangent != CadVectorD.Zero
                        ? new CadContinueArcBase(true, arcStart, arcTangent)
                        : default;

                case CadLine line:
                    var lineStart = line.End;
                    var lineTangent = (line.End - line.Start).Normalize();
                    return lineTangent != CadVectorD.Zero
                        ? new CadContinueArcBase(true, lineStart, lineTangent)
                        : default;
            }
        }

        return default;
    }
}
