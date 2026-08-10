using System.Text;

namespace Direct2dCad.ViewModels.Services.Platform;

public static class AiTextFileReader
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    public const int MaximumCharacters = 120_000;

    public static AiFileImportData Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("The selected file does not exist.", filePath);
        if (fileInfo.Length > MaximumFileBytes)
            throw new InvalidDataException("Text files larger than 4 MB cannot be attached.");

        try
        {
            using var reader = new StreamReader(
                filePath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (text.Length > MaximumCharacters)
            {
                text = string.Concat(
                    text.AsSpan(0, MaximumCharacters),
                    "\n[Attached file truncated to fit the AI context window]");
            }

            return new AiFileImportData(
                fileInfo.Name,
                ResolveContentType(fileInfo.Extension),
                TextContent: text);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Only image and UTF text files can be attached to the AI conversation.",
                exception);
        }
    }

    private static string ResolveContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".c" or ".h" => "text/x-c",
            ".cpp" or ".cxx" or ".cc" or ".hpp" => "text/x-c++",
            ".cs" => "text/x-csharp",
            ".css" => "text/css",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".md" or ".markdown" => "text/markdown",
            ".py" => "text/x-python",
            ".sql" => "application/sql",
            ".svg" => "image/svg+xml",
            ".ts" => "text/typescript",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "text/plain"
        };
}
