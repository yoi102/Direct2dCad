using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Cad;

public static class CadBlockTransform
{
    public static CadMatrixD Create(CadBlockDefinition definition, CadBlockReference reference)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(reference);

        return Create(
            definition,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY);
    }

    public static CadMatrixD Create(
        CadBlockDefinition definition,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return CadMatrixD.CreateTranslation(-definition.BasePoint.X, -definition.BasePoint.Y) *
               CadMatrixD.CreateScale(scaleX, scaleY) *
               CadMatrixD.CreateRotation(rotationRadians) *
               CadMatrixD.CreateTranslation(position.X, position.Y);
    }

    public static CadPointD TransformPoint(
        CadBlockDefinition definition,
        CadBlockReference reference,
        CadPointD point) => Create(definition, reference).TransformPoint(point);

    public static CadRectD TransformBounds(
        CadBlockDefinition definition,
        CadBlockReference reference,
        CadRectD bounds) => bounds.Transform(Create(definition, reference));

    public static CadRectD TransformBounds(
        CadBlockDefinition definition,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        CadRectD bounds) => bounds.Transform(Create(definition, position, rotationRadians, scaleX, scaleY));
}
