using System.Numerics;

namespace Direct2dCad.Rendering.Direct2D.Resources;

/// <summary>
/// Direct2D strokes <c>DrawLine</c> on a much slower path when the world transform mirrors, and the CAD view
/// always does (Y points up in the drawing, down on screen). Measured on this renderer's target: about
/// 1.57 µs per line mirrored against 0.23-0.25 µs in device space. Lines drawn straight to a surface are
/// therefore stroked in device space. Recordings made under the identity transform never mirror, so command
/// lists that are replayed under another transform keep their world-space strokes.
/// </summary>
internal static class Direct2DLineSubmission
{
    /// <summary>
    /// True for a uniform scale combined with a rotation and one reflection, the shape of every CAD view
    /// transform. Non-uniform scales are rejected so stroke widths never change from the world-space result.
    /// </summary>
    public static bool TryGetMirroredSimilarityScale(in Matrix3x2 transform, out float scale)
    {
        var determinant = transform.M11 * transform.M22 - transform.M12 * transform.M21;
        if (!(determinant < 0) || !float.IsFinite(determinant))
        {
            scale = 0;
            return false;
        }

        scale = MathF.Sqrt(-determinant);
        var tolerance = scale * 1e-4f;
        return MathF.Abs(transform.M11 + transform.M22) <= tolerance &&
               MathF.Abs(transform.M12 - transform.M21) <= tolerance;
    }
}
