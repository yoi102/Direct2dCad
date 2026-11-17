namespace Direct2dCad.CommandLine.Tests;

public sealed class CadCommandLineSyntaxTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   \t ", new string[0])]
    [InlineData("LINE", new[] { "LINE" })]
    [InlineData("  LAYER   SET  A ", new[] { "LAYER", "SET", "A" })]
    [InlineData("TEXT \"hello world\" 1,2", new[] { "TEXT", "hello world", "1,2" })]
    [InlineData("A \"\" B", new[] { "A", "", "B" })]
    [InlineData("pre\"fix suf\"fix", new[] { "prefix suffix" })]
    [InlineData("\"say \"\"hi\"\"\"", new[] { "say \"hi\"" })]
    public void TokenizeSplitsOnUnquotedWhitespace(string input, string[] expected)
    {
        Assert.True(CadCommandLineSyntax.TryTokenize(input, out var tokens, out var error));
        Assert.Null(error);
        Assert.Equal(expected, tokens);
    }

    [Fact]
    public void NullInputTokenizesToNothing()
    {
        Assert.True(CadCommandLineSyntax.TryTokenize(null, out var tokens, out _));
        Assert.Empty(tokens);
    }

    [Theory]
    [InlineData("TEXT \"unterminated")]
    [InlineData("\"")]
    public void UnterminatedQuoteIsReported(string input)
    {
        Assert.False(CadCommandLineSyntax.TryTokenize(input, out var tokens, out var error));
        Assert.Empty(tokens);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Throws<FormatException>(() => CadCommandLineSyntax.Tokenize(input));
    }

    [Theory]
    [InlineData("Layer1", "Layer1")]
    [InlineData("", "\"\"")]
    [InlineData("My Layer", "\"My Layer\"")]
    [InlineData("A\"B", "\"A\"\"B\"")]
    public void QuoteArgumentQuotesOnlyWhenNeeded(string value, string expected)
        => Assert.Equal(expected, CadCommandLineSyntax.QuoteArgument(value));

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("tab\tand space")]
    [InlineData("quote\"inside")]
    [InlineData("\"leading and trailing\"")]
    [InlineData("\"\"")]
    public void QuotedArgumentRoundTripsThroughTokenizer(string value)
    {
        var commandLine = "LAYER SET " + CadCommandLineSyntax.QuoteArgument(value);
        Assert.True(CadCommandLineSyntax.TryTokenize(commandLine, out var tokens, out _));
        Assert.Equal(new[] { "LAYER", "SET", value }, tokens);
    }

    [Theory]
    [InlineData("line", "LINE")]
    [InlineData("  _line ", "LINE")]
    [InlineData("._Circle", "CIRCLE")]
    public void NormalizeCommandNameStripsAutoCadPrefixes(string value, string expected)
        => Assert.Equal(expected, CadCommandLineSyntax.NormalizeCommandName(value));
}
