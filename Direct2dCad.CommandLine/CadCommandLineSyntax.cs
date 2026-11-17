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
        var text = commandLine ?? string.Empty;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            // Inside quotes, "" is a literal quote (QuoteArgument emits it), so any value round-trips.
            if (character == '"' && quoted && index + 1 < text.Length && text[index + 1] == '"')
            {
                current.Append('"');
                index++;
            }
            else if (character == '"') { quoted = !quoted; started = true; }
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

    public static string QuoteArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 || value.Any(static c => char.IsWhiteSpace(c) || c == '"')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    public static string NormalizeCommandName(string value) =>
        value.Trim().TrimStart('_', '.').ToUpperInvariant();
}
