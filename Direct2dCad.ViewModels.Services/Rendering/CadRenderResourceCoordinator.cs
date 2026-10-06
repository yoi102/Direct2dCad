using Direct2dCad.ChangeTracking;
using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadRenderResourceCoordinator
{
    public bool IsAttached { get; private set; }

    public bool IsApplyingTextMeasurementChanges { get; private set; }

    public void Attach(
        CadEditor editor,
        ICadRenderSession renderHost,
        CadTransientScene transientScene,
        CadHandleScene handleScene,
        EventHandler<CadDocumentChangeSet> documentChangedHandler)
    {
        if (IsAttached)
            return;

        renderHost.SetScene(editor.Document, editor.Viewport);
        renderHost.SetTransientScene(transientScene);
        renderHost.SetHandleScene(handleScene);
        editor.DocumentChanged += documentChangedHandler;
        // SetScene already reset the device resources and scheduled the initial
        // background geometry preparation. Rebuilding here would immediately
        // move the whole document back onto the UI thread.
        editor.RegisterGeometryResourceManager(
            renderHost.GeometryResourceManager,
            rebuildExistingResources: false);
        IsAttached = true;
    }

    public void Detach(
        CadEditor editor,
        ICadRenderSession renderHost,
        EventHandler<CadDocumentChangeSet> documentChangedHandler)
    {
        if (!IsAttached)
            return;

        editor.DocumentChanged -= documentChangedHandler;
        editor.UnregisterGeometryResourceManager(renderHost.GeometryResourceManager);
        IsAttached = false;
    }

    public void UpdateTextMeasurements(CadEditor editor, ICadRenderSession renderHost)
    {
        if (!IsAttached || IsApplyingTextMeasurementChanges)
            return;

        var measurements = renderHost.MeasurePendingTextBounds(editor.Document);
        if (measurements.Count == 0)
            return;

        try
        {
            IsApplyingTextMeasurementChanges = true;
            editor.ApplyDerivedTextBounds(measurements);
        }
        finally
        {
            IsApplyingTextMeasurementChanges = false;
        }
    }
}
