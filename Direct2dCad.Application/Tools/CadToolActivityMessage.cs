namespace Direct2dCad.Application.Tools;

public sealed record CadToolActivityMessage(
    string CallId, string ToolName, string? DocumentName, string Outcome, string Summary);
