namespace Direct2dCad.ViewModels;

public partial class MainViewModel
{
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _dockLayoutService.AnchorableStateChanged -= OnAnchorableStateChanged;
        CurrentEditorTabViewModel = null;
        OpenOperation.Dispose();

        List<Exception>? errors = null;
        foreach (var document in _dockLayoutService.Documents.OfType<EditorTabViewModel>().ToArray())
        {
            try { document.Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }

        if (_ownsRecoveryStore)
            _recoveryStore.Dispose();

        if (errors is not null)
            throw new AggregateException("One or more documents could not be completely disposed.", errors);
    }
}
