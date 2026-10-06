namespace Direct2dCad.CommandLine;

public static class CadCommandLineSyntax
{
    public static string[] Tokenize(string commandLine)
    {
        if (!TryTokenize(commandLine, out var tokens, out var error))
            throw new FormatException(error);
        return tokens;
    }

    public static bool TryTokenize(string? commandLine, out string[] tokens, out string? error)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in commandLine ?? string.Empty)
        {
            if (character == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started) { values.Add(current.ToString()); current.Clear(); started = false; }
            }
            else { current.Append(character); started = true; }
        }
        if (quoted) { tokens = []; error = "Missing closing quote."; return false; }
        if (started) values.Add(current.ToString());
        tokens = values.ToArray();
        error = null;
        return true;
    }

    public static string QuoteArgument(string value) => value.Any(char.IsWhiteSpace) || value.Length == 0
        ? $"\"{value}\"" : value;

    public static string NormalizeCommandName(string value) =>
        value.Trim().TrimStart('_', '.').ToUpperInvariant();
}
