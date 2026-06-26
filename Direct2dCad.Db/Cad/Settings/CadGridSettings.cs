using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.Db.Cad.Settings;

public enum CadGridType
{
    None,
    Dots,
    Lines,
    Cross
}
public sealed class CadGridSettings
{
    public CadGridType Type { get; set; } = CadGridType.Lines;
    public double SpacingX { get; set; } = 10.0;
    public double SpacingY { get; set; } = 10.0;
    public int Subdivision { get; set; } = 5;
}
