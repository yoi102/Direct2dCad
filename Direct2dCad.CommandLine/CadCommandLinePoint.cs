using System.Globalization;

namespace Direct2dCad.CommandLine;

public readonly record struct CadCommandLinePoint(double X, double Y);

public static class CadCommandLinePointParser
{
    public static bool LooksLikePoint(string value) =>
        value.Contains(',') || value.StartsWith('@');

    public static bool TryParse(
        string value,
        CadCommandLinePoint? relativeBase,
        out CadCommandLinePoint point,
        out string? error)
    {
        var text = value.Trim();
        var isRelative = text.StartsWith('@');
        if (isRelative)
            text = text[1..].Trim();

        if (isRelative && relativeBase is null)
        {
            point = default;
            error = "A relative point requires a previous drawing point.";
            return false;
        }

        if (text.Contains('<'))
            return TryParsePolar(text, relativeBase, isRelative, out point, out error);

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseNumber(parts[0], out var x) || !TryParseNumber(parts[1], out var y))
        {
            point = default;
            error = "Point format must be X,Y, @dX,dY, or @distance<angle.";
            return false;
        }

        var origin = isRelative ? relativeBase!.Value : default;
        point = new CadCommandLinePoint(origin.X + x, origin.Y + y);
        error = null;
        return true;
    }

    private static bool TryParsePolar(
        string text,
        CadCommandLinePoint? relativeBase,
        bool isRelative,
        out CadCommandLinePoint point,
        out string? error)
    {
        var parts = text.Split('<', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !TryParseNumber(parts[0], out var distance) ||
            !TryParseNumber(parts[1], out var angleDegrees))
        {
            point = default;
            error = "Polar point format must be @distance<angle.";
            return false;
        }

        var origin = isRelative ? relativeBase!.Value : default;
        var angleRadians = angleDegrees * Math.PI / 180.0;
        point = new CadCommandLinePoint(
            origin.X + distance * Math.Cos(angleRadians),
            origin.Y + distance * Math.Sin(angleRadians));
        error = null;
        return true;
    }

    private static bool TryParseNumber(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        double.IsFinite(result);
}
