namespace Direct2dCad.CommandLine;

public static class CadCommandLineSyntax
{
    public static string[] Tokenize(string commandLine) =>
        (commandLine ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string NormalizeCommandName(string value) =>
        value.Trim().TrimStart('_', '.').ToUpperInvariant();
}
