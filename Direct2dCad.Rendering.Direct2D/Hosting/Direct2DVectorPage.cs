using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

/// <summary>Records CAD geometry and text into a Direct2D command list for vector printing.</summary>
public sealed class Direct2DVectorPage : IDisposable
{
    private readonly ImageSourceDirect2DResource _resources = new();
    private readonly Direct2DSceneRender _renderer = new();
    private ID2D1CommandList? _commands;
    public nint DevicePointer => _resources.Device!.NativePointer;
    public nint CommandListPointer => _commands!.NativePointer;
    public Direct2DVectorPage(CadDocument document, CadViewport viewport, CadRenderOptions options, CadOleRenderCallback? oleDrawCallback = null, CadScreenRect? outputClip = null)
    {
        var background = document.ViewSettings.BackgroundColor;
        try
        {
            document.ViewSettings.BackgroundColor = CadColor.White;
            var context = _resources.Context!;
            _renderer.ResetDeviceResources(_resources.Factory, _resources.DwriteFactory, _resources.Device, context, document, false);
            _renderer.OleDrawCallback = oleDrawCallback;
            _commands = context.CreateCommandList(); context.Target = _commands;
            context.BeginDraw();
            try
            {
                context.Transform = Matrix3x2.Identity;
                using var paper = context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
                context.FillRectangle(new Vortice.RawRectF(0, 0, (float)viewport.ViewWidth, (float)viewport.ViewHeight), paper);
                var clip = outputClip ?? new CadScreenRect(0, 0, (int)Math.Ceiling(viewport.ViewWidth), (int)Math.Ceiling(viewport.ViewHeight));
                context.PushAxisAlignedClip(new Vortice.RawRectF(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height), AntialiasMode.Aliased);
                try { _renderer.BeginFrame(viewportZoom: viewport.Zoom); _renderer.Render(document, viewport, options); }
                finally { _renderer.CompleteFrame(); context.PopAxisAlignedClip(); }
            }
            finally { context.EndDraw().CheckError(); context.Target = null; }
            _commands.Close().CheckError();
        }
        catch { Dispose(); throw; }
        finally { document.ViewSettings.BackgroundColor = background; }
    }
    public void Dispose() { _commands?.Dispose(); _commands = null; _renderer.Dispose(); _resources.Dispose(); }
}
