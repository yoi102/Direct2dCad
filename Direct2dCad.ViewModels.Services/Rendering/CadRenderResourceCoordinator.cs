using Direct2dCad.ChangeTracking;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Rendering;

internal sealed class CadRenderResourceCoordinator
{
    public bool IsAttached { get; private set; }

    public bool IsApplyingTextMeasurementChanges { get; private set; }

    public void Attach(
        CadEditor editor,
        Direct2DImageRenderHost renderHost,
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
        editor.RegisterGeometryResourceManager(renderHost.GeometryResourceManager);
        IsAttached = true;
    }

    public void Detach(
        CadEditor editor,
        Direct2DImageRenderHost renderHost,
        EventHandler<CadDocumentChangeSet> documentChangedHandler)
    {
        if (!IsAttached)
            return;

        editor.DocumentChanged -= documentChangedHandler;
        editor.UnregisterGeometryResourceManager(renderHost.GeometryResourceManager);
        IsAttached = false;
    }

    public void UpdateTextMeasurements(CadEditor editor, Direct2DImageRenderHost renderHost)
    {
        if (!IsAttached || IsApplyingTextMeasurementChanges)
            return;

        var changes = renderHost.UpdateTextMeasurements(editor.Document);
        if (!changes.DocumentChanged)
            return;

        try
        {
            IsApplyingTextMeasurementChanges = true;
            editor.PublishDocumentChanges(changes);
        }
        finally
        {
            IsApplyingTextMeasurementChanges = false;
        }
    }
}
