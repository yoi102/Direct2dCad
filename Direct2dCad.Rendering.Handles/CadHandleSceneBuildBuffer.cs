using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Rendering.Handles;

/// <summary>
/// Reusable storage for building large handle scenes without allocating new backing arrays.
/// </summary>
public sealed class CadHandleSceneBuildBuffer
{
    internal List<CadHandleItem> Items { get; } = [];
    internal List<CadEntity> SelectedEntities { get; } = [];
}
