namespace Direct2dCad.CommandLine;

public interface ICadCommandLineService
{
    IReadOnlyList<CadCommandLineDescriptor> Commands { get; }

    CadCommandLineResult Execute(string commandLine, ICadCommandLineContext? context);
}

public sealed record CadCommandLineDescriptor(
    string Name,
    string Aliases,
    string Syntax,
    string Description);

public sealed record CadCommandLineResult(
    bool Success,
    string Message,
    bool ClearOutput = false);
