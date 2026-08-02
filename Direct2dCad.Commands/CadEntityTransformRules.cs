using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

/// <summary>
/// Public capability checks shared by command clients such as the Agent tools.
/// Keeping these rules next to the command implementation prevents schemas and
/// interactive callers from claiming transformations that the database cannot apply.
/// </summary>
public static class CadEntityTransformRules
{
    public static void EnsureRotationSupported(CadEntity entity, double angleRadians)
    {
        ArgumentNullException.ThrowIfNull(entity);
        CadEntityTransform.ValidateRotation(entity, angleRadians);
    }

    public static void EnsureScaleSupported(CadEntity entity, double factor)
    {
        ArgumentNullException.ThrowIfNull(entity);
        CadEntityTransform.ValidateUniformScale(entity, factor);
    }

    public static void EnsureMirrorSupported(CadEntity entity, double axisAngleRadians)
    {
        ArgumentNullException.ThrowIfNull(entity);
        CadEntityTransform.ValidateMirror(entity, axisAngleRadians);
    }

    public static string GetRotationConstraint(CadEntity entity) => entity switch
    {
        CadEllipse or CadRectangle => "Only integer multiples of 90 degrees are supported.",
        CadEllipseArc or CadOleObject => "Rotation is not supported by transform_entities.",
        _ => "Any finite angle in degrees is supported."
    };

    public static string GetScaleConstraint(CadEntity entity) => entity is CadEllipseArc
        ? "Uniform scaling is not supported by transform_entities."
        : "The factor must be greater than zero. Block references may use negative scale only through exact geometry editing.";

    public static string GetMirrorConstraint(CadEntity entity) => entity switch
    {
        CadEllipse or CadRectangle => "The mirror axis must be a multiple of 45 degrees.",
        CadOleObject => "Only horizontal or vertical mirror axes are supported.",
        CadEllipseArc => "Mirroring is not supported by transform_entities.",
        _ => "Any finite mirror-axis angle is supported."
    };
}
