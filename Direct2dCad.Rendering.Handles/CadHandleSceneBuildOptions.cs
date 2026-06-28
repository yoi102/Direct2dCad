namespace Direct2dCad.Rendering.Handles;

public sealed record CadHandleSceneBuildOptions(
    bool IncludeSelectionOutline = true,
    bool IncludeGripHandles = true,
    bool IncludeLockedEntityGripHandles = false)
{
    public static CadHandleSceneBuildOptions Default { get; } = new();
}
