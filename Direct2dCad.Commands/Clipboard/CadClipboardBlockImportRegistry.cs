using System.Runtime.CompilerServices;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Commands.Clipboard;

internal static class CadClipboardBlockImportRegistry
{
    private static readonly ConditionalWeakTable<CadDocument, ImportedBlockMap> DocumentMaps = new();

    public static bool TryResolve(
        CadDocument document,
        Guid sourceDocumentToken,
        BlockId sourceBlockId,
        out BlockId targetBlockId)
    {
        targetBlockId = default;
        if (sourceDocumentToken == Guid.Empty ||
            !DocumentMaps.TryGetValue(document, out var importedBlocks))
        {
            return false;
        }

        var key = new ImportedBlockKey(sourceDocumentToken, sourceBlockId);
        lock (importedBlocks.SyncRoot)
        {
            if (!importedBlocks.BlockIds.TryGetValue(key, out targetBlockId))
                return false;

            if (document.TryGetBlock(targetBlockId, out var targetBlock) && targetBlock is not null)
                return true;

            importedBlocks.BlockIds.Remove(key);
            targetBlockId = default;
            return false;
        }
    }

    public static void Register(
        CadDocument document,
        Guid sourceDocumentToken,
        BlockId sourceBlockId,
        BlockId targetBlockId)
    {
        if (sourceDocumentToken == Guid.Empty)
            return;

        var importedBlocks = DocumentMaps.GetValue(document, static _ => new ImportedBlockMap());
        lock (importedBlocks.SyncRoot)
            importedBlocks.BlockIds[new ImportedBlockKey(sourceDocumentToken, sourceBlockId)] = targetBlockId;
    }

    private sealed class ImportedBlockMap
    {
        public object SyncRoot { get; } = new();
        public Dictionary<ImportedBlockKey, BlockId> BlockIds { get; } = [];
    }

    private readonly record struct ImportedBlockKey(Guid SourceDocumentToken, BlockId SourceBlockId);
}
