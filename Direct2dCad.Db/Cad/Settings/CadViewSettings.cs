using Direct2dCad.Db.Cad;

namespace Direct2dCad.Db.Cad.Settings;

public sealed class CadViewSettings
{
    public CadColor BackgroundColor { get; set; } = CadColor.Black;
    public CadGridSettings Grid { get; set; } = new();
    public CadOriginSettings Origin { get; set; } = new();
}
