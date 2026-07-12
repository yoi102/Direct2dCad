namespace Direct2dCad.CommandLine;

public interface ICadCommandLineHandler
{
    CadCommandLineDescriptor Descriptor { get; }

    CadCommandLineResult Execute(CadCommandLineRequest request);
}

public sealed record CadCommandLineRequest(
    ICadCommandLineContext Context,
    IReadOnlyList<string> Arguments);
