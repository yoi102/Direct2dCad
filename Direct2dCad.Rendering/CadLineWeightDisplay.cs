namespace Direct2dCad.Rendering;

public static class CadLineWeightDisplay
{
    public const double DipsPerInch = 96.0;
    public const double MillimetersPerInch = 25.4;
    public const double DipsPerMillimeter = DipsPerInch / MillimetersPerInch;

    public static double ToDips(double millimeters)
    {
        if (!double.IsFinite(millimeters) || millimeters < 0)
            throw new ArgumentOutOfRangeException(nameof(millimeters));

        return millimeters * DipsPerMillimeter;
    }

    public static float ToDipsSingle(float millimeters)
    {
        if (!float.IsFinite(millimeters) || millimeters < 0)
            throw new ArgumentOutOfRangeException(nameof(millimeters));

        return millimeters * (float)DipsPerMillimeter;
    }
}
