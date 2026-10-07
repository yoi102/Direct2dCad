using System.Numerics;
using System.Runtime.CompilerServices;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Resources;

/// <summary>Keeps world transforms in double precision until local geometry is submitted.</summary>
internal static class Direct2DCoordinateSystem
{
    private sealed class State { public CadMatrixD World; public Matrix3x2 Native; }
    private static readonly ConditionalWeakTable<ID2D1DeviceContext, State> States = new();
    internal static bool NeedsOrigin(CadPointD point) => Math.Abs(point.X) > 8192 || Math.Abs(point.Y) > 8192;
    internal static bool RequiresPrecision(CadViewport viewport) => NeedsOrigin(viewport.VisibleWorldBounds.Center);
    internal static CadMatrixD ViewportTransform(CadViewport viewport) =>
        CadMatrixD.CreateScale(viewport.Zoom, -viewport.Zoom) * CadMatrixD.CreateTranslation(viewport.Offset.X, viewport.Offset.Y);
    internal static CadMatrixD FromNative(Matrix3x2 value) => new(value.M11, value.M12, value.M21, value.M22, value.M31, value.M32);
    internal static Matrix3x2 ToNative(CadMatrixD value) => new((float)value.M11, (float)value.M12,
        (float)value.M21, (float)value.M22, (float)value.OffsetX, (float)value.OffsetY);
    internal static CadMatrixD Current(ID2D1DeviceContext context) =>
        States.TryGetValue(context, out var state) && state.Native == context.Transform ? state.World : FromNative(context.Transform);
    private static void Set(ID2D1DeviceContext context, CadMatrixD value)
    {
        var state = States.GetOrCreateValue(context);
        state.World = value; state.Native = ToNative(value); context.Transform = state.Native;
    }
    public static Scope PushAbsolute(ID2D1DeviceContext context, CadMatrixD value)
    {
        var previous = Current(context); Set(context, value); return new(context, previous);
    }
    public static Scope Push(ID2D1DeviceContext context, CadMatrixD local) => local.IsIdentity ? default : PushAbsolute(context, local * Current(context));
    public static Scope PushOrigin(ID2D1DeviceContext context, CadPointD origin) => Push(context, CadMatrixD.CreateTranslation(origin.X, origin.Y));
    internal static CadViewport LocalViewport(CadViewport viewport, CadPointD origin)
    {
        if (origin == default) return viewport;
        var local = new CadViewport();
        local.SetSize(viewport.ViewWidth, viewport.ViewHeight);
        local.SetView(viewport.Zoom, new(viewport.Offset.X + origin.X * viewport.Zoom, viewport.Offset.Y - origin.Y * viewport.Zoom));
        return local;
    }
    public readonly struct Scope(ID2D1DeviceContext? context, CadMatrixD previous) : IDisposable
    {
        public void Dispose() { if (context is not null) Set(context, previous); }
    }
}
