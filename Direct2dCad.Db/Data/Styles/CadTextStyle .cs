namespace Direct2dCad.Db.Data.Styles;

public sealed class CadTextStyle : CadStyle
{
    public override CadStyleKind Kind => CadStyleKind.Text;

    public string FontFamily { get; private set; }
    public double TextHeight { get; private set; }
    public double WidthFactor { get; private set; }
    public double ObliqueAngle { get; private set; }
    public bool IsBold { get; private set; }
    public bool IsItalic { get; private set; }

    internal CadTextStyle(
        StyleId id,
        string name,
        string fontFamily,
        double textHeight,
        double widthFactor = 1.0,
        double obliqueAngle = 0.0,
        bool isBold = false,
        bool isItalic = false)
        : base(id, name)
    {
        FontFamily = GuardFontFamily(fontFamily);
        TextHeight = GuardPositive(textHeight, nameof(textHeight));
        WidthFactor = GuardPositive(widthFactor, nameof(widthFactor));
        ObliqueAngle = obliqueAngle;
        IsBold = isBold;
        IsItalic = isItalic;
    }

    public void SetFontFamily(string fontFamily) => FontFamily = GuardFontFamily(fontFamily);
    public void SetTextHeight(double textHeight) => TextHeight = GuardPositive(textHeight, nameof(textHeight));
    public void SetWidthFactor(double widthFactor) => WidthFactor = GuardPositive(widthFactor, nameof(widthFactor));
    public void SetObliqueAngle(double obliqueAngle) => ObliqueAngle = obliqueAngle;
    public void SetBold(bool value) => IsBold = value;
    public void SetItalic(bool value) => IsItalic = value;

    private static string GuardFontFamily(string fontFamily)
    {
        return string.IsNullOrWhiteSpace(fontFamily)
            ? throw new ArgumentException("Font family cannot be empty.", nameof(fontFamily))
            : fontFamily.Trim();
    }

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }
}
