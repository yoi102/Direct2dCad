using SharpGen.Runtime;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

internal static class Direct2DDeviceFailureClassifier
{
    internal static bool IsRecoverable(Result result)
    {
        return result == Vortice.Direct2D1.ResultCode.RecreateTarget ||
               result == Vortice.DXGI.ResultCode.DeviceRemoved ||
               result == Vortice.DXGI.ResultCode.DeviceReset ||
               result == Vortice.DXGI.ResultCode.DeviceHung ||
               result == Vortice.DXGI.ResultCode.DriverInternalError ||
               result == Vortice.DXGI.ResultCode.AccessLost;
    }
}
