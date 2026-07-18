namespace Direct2dCad.Rendering.Handles;

public sealed record CadHandleSceneBuildOptions(
    bool IncludeSelectionOutline = true,
    bool IncludeGripHandles = true,
    bool IncludeLockedEntityGripHandles = false)
{
    public const int DefaultMaximumIndividualGripEntityCount = 512;

    public CadHandleStyle SelectionOutlineStyle { get; init; } = CadHandleStyle.SelectionOutline;
    public CadHandleStyle GripStyle { get; init; } = CadHandleStyle.Grip;
    public double RotationHandleOffset { get; init; } = 1.0;
    public int MaximumIndividualGripEntityCount { get; init; } = DefaultMaximumIndividualGripEntityCount;
    public bool IncludeAggregateMoveGripForLargeSelection { get; init; } = true;

    public static CadHandleSceneBuildOptions Default { get; } = new();
}
