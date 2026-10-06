using Direct2dCad.CommandLine;
using Direct2dCad.Editor;
using Direct2dCad.Lang;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class CommandLineToolboxViewModel
{
    private async Task<CadCommandLineResult> DispatchLineAsync(string commandLine,
        CadDocumentViewModel? document, CadEditor? editor, CancellationToken token)
    {
        var tool = await _toolCommandLineService.TryExecuteAsync(commandLine, token);
        token.ThrowIfCancellationRequested();
        if (tool is not null) return new(tool.Success, tool.Message);
        // A delayed dispatcher may decline an input after its tab was closed or replaced.
        if (document is not null && (document.IsDisposed || !ReferenceEquals(document.CadEditor, editor)))
        {
            AddMessage(CadCommandLineEntryKind.Warning, CadUiText.Get("TerminalDocumentUnavailable"));
            return new(false, CadUiText.Get("TerminalDocumentUnavailable"));
        }
        var result = _commandLineService.Execute(commandLine, document);
        if (commandLine.Equals("HELP", StringComparison.OrdinalIgnoreCase) || commandLine == "?")
        {
            var help = await _toolCommandLineService.TryExecuteAsync("CADHELP", token);
            token.ThrowIfCancellationRequested();
            if (help is not null) result = result with { Message = result.Message + Environment.NewLine + help.Message };
        }
        return result;
    }

    private async Task<CadCommandLineResult> ExecuteScriptAsync(string commandLine, CancellationToken token)
    {
        if (!CadCommandLineSyntax.TryTokenize(commandLine, out var arguments, out var error) ||
            arguments.Length != 2 || !Path.IsPathFullyQualified(arguments[1]))
            return new(false, error ?? "Usage: SCRIPT \"absolute-path.scr\"");
        const int maximumBytes = 1024 * 1024;
        var path = arguments[1];
        var expectedDocument = _documentViewModel;
        if (new FileInfo(path).Length > maximumBytes) return new(false, "Scripts must be at most 1 MiB.");
        var lines = await File.ReadAllLinesAsync(path, token);
        if (lines.Length > 10000) return new(false, "Scripts must contain at most 10,000 lines.");
        var completed = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (!ReferenceEquals(expectedDocument, _documentViewModel))
                return Stopped(index, "The active document changed outside the script.");
            if (CommandName(line) == "SCRIPT") return Stopped(index, "Nested SCRIPT commands are not supported.");
            AddEntry(CadCommandLineEntryKind.Input, $"[{Path.GetFileName(path)}:{index + 1}] > {line}");
            CadCommandLineResult result;
            try { result = await DispatchLineAsync(line, expectedDocument, expectedDocument?.CadEditor, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return Stopped(index, exception.Message); }
            token.ThrowIfCancellationRequested();
            if (!result.Success) return Stopped(index, result.Message);
            if (result.ClearOutput) ClearOutput();
            else if (!string.IsNullOrWhiteSpace(result.Message)) AddMessage(CadCommandLineEntryKind.Output, result.Message);
            completed++;
            if (ChangesActiveDocument(line)) expectedDocument = _documentViewModel;
            // Keep rendering and Escape responsive for runs of synchronous drawing commands.
            await Task.Yield();
        }
        return new(true, $"Script completed: {completed} commands. Each command retains its normal undo behavior.");

        CadCommandLineResult Stopped(int index, string message) => new(false,
            $"Script stopped at line {index + 1} after {completed} commands: {message}{Environment.NewLine}Earlier completed commands remain applied; use UNDO where supported.");
    }

    private static bool ChangesActiveDocument(string commandLine)
    {
        var text = commandLine.TrimStart();
        var name = CommandName(text);
        if (name == "TOOL") name = CommandName(text[(text.IndexOfAny([' ', '\t']) + 1)..]);
        return name is "NEW" or "OPEN" or "CREATE_DOCUMENT" or "OPEN_DOCUMENT" or "OPEN_DXF" or "ACTIVATE_DOCUMENT" or "CLOSE_DOCUMENT";
    }
}
