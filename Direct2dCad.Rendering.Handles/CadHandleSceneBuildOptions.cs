namespace Direct2dCad.Rendering.Handles;

public sealed record CadHandleSceneBuildOptions(
    bool IncludeSelectionOutline = true,
    bool IncludeGripHandles = true,
    bool IncludeLockedEntityGripHandles = false)
{
    public CadHandleStyle SelectionOutlineStyle { get; init; } = CadHandleStyle.SelectionOutline;
    public CadHandleStyle GripStyle { get; init; } = CadHandleStyle.Grip;

    public static CadHandleSceneBuildOptions Default { get; } = new();
}
