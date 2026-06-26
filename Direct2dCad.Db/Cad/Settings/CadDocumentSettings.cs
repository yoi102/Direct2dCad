using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.Db.Cad.Settings;

public enum CadUnit
{
    Unitless,
    Millimeter,
    Centimeter,
    Meter,
    Inch,
    Foot,
    Mil
}

public sealed class CadDocumentSettings
{
    public CadUnit Unit { get; private set; }
    public int LengthPrecision { get; private set; }
    public int AnglePrecision { get; private set; }
    public LayerDrawingPriority LayerDrawingPriority { get; private set; } = new();
    public LayerDrawingPriority LaterDrawingPriority => LayerDrawingPriority;

    public static CadDocumentSettings Default()
    {
        return new CadDocumentSettings
        {
            Unit = CadUnit.Millimeter,
            LengthPrecision = 3,
            AnglePrecision = 2
        };
    }

    public void SetUnit(CadUnit unit)
    {
        Unit = unit;
    }

    public void SetLengthPrecision(int precision)
    {
        if (precision < 0 || precision > 12)
            throw new ArgumentOutOfRangeException(nameof(precision));

        LengthPrecision = precision;
    }

    public void SetAnglePrecision(int precision)
    {
        if (precision < 0 || precision > 12)
            throw new ArgumentOutOfRangeException(nameof(precision));

        AnglePrecision = precision;
    }
}
