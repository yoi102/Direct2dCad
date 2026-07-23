using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadHandleSceneUpdateTracker
{
    private CadDocument? _document;
    private CadHandleSceneBuildOptions? _options;
    private long _selectionVersion;
    private bool _isCurrent;

    public bool IsCurrent(
        CadDocument document,
        long selectionVersion,
        CadHandleSceneBuildOptions options)
    {
        return _isCurrent &&
               ReferenceEquals(_document, document) &&
               _selectionVersion == selectionVersion &&
               _options == options;
    }

    public void MarkCurrent(
        CadDocument document,
        long selectionVersion,
        CadHandleSceneBuildOptions options)
    {
        _document = document;
        _selectionVersion = selectionVersion;
        _options = options;
        _isCurrent = true;
    }

    public void Invalidate() => _isCurrent = false;

    public void Reset()
    {
        _document = null;
        _options = null;
        _selectionVersion = 0;
        _isCurrent = false;
    }
}
