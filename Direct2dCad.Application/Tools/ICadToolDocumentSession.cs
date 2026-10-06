using Direct2dCad.Db;
using Direct2dCad.Editor;

namespace Direct2dCad.Application.Tools;

/// <summary>The document editing capabilities required by CAD tools, independent of a UI or renderer.</summary>
public interface ICadToolDocumentSession
{
    CadEditor CadEditor { get; }
    bool IsDisposed { get; }
    LayerId DrawingLayerId { get; set; }
    string ToolMode { get; }
    double CurrentPointerWorldX { get; }
    double CurrentPointerWorldY { get; }
    LayoutId? ActiveLayoutId { get; }
    LayoutViewportId? ActiveLayoutViewportId { get; }
    BlockId? EditingBlockId { get; }
    string EditingBlockName { get; }
    bool IsEditingBlock { get; }
    bool IsModelSpaceActive { get; }
    bool IsLayoutViewportActive { get; }
    bool IsPaperSpaceActive { get; }
    void FitToWindow();
    void RequestRender();
    /// <summary>Updates tool-result selection without adding an editor-history entry; explicit selection uses an editor command.</summary>
    void SelectEntities(IEnumerable<EntityId> entityIds);
    void EditBlockDefinition(BlockId blockId);
    void ExitBlockEditing();
}
