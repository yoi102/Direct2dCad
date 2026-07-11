namespace Direct2dCad.Ole.Windows;

public static class CadOleServices
{
    public static CadOleClipboardData? TryCreateFromClipboard()
    {
        EnsureStaThread();
        using var initializer = OleInitializationScope.Enter();
        using var oleObject = DrawableOleObjectFactory.CreateFromClipboard();
        return oleObject is null
            ? null
            : CreateClipboardData(oleObject);
    }

    public static CadOleEditSession BeginEdit(
        byte[] oleBytes,
        IntPtr parentHwnd,
        string objectName = "",
        string containerName = "Direct2dCad",
        Action<CadOleClipboardData?, bool>? objectUpdated = null)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();

        var session = new CadOleEditSession(oleBytes, objectName, objectUpdated);
        try
        {
            session.Open(parentHwnd, containerName);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public static CadOleRenderSession CreateRenderSession(byte[] oleBytes)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();
        return new CadOleRenderSession(oleBytes);
    }

    internal static CadOleDrawData DrawRegion(
        DrawableOleObject oleObject,
        int fullPixelWidth,
        int fullPixelHeight,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight)
    {
        ArgumentNullException.ThrowIfNull(oleObject);

        fullPixelWidth = Math.Clamp(fullPixelWidth, 1, 1_048_576);
        fullPixelHeight = Math.Clamp(fullPixelHeight, 1, 1_048_576);
        regionWidth = Math.Clamp(regionWidth, 1, 2048);
        regionHeight = Math.Clamp(regionHeight, 1, 2048);
        regionX = Math.Clamp(regionX, 0, fullPixelWidth - regionWidth);
        regionY = Math.Clamp(regionY, 0, fullPixelHeight - regionHeight);

        var pixels = oleObject.Draw(
            fullPixelWidth,
            fullPixelHeight,
            regionX,
            regionY,
            regionWidth,
            regionHeight);
        return new CadOleDrawData(
            regionWidth,
            regionHeight,
            checked(regionWidth * 4),
            pixels);
    }

    internal static CadOleClipboardData CreateClipboardData(DrawableOleObject oleObject)
    {
        ArgumentNullException.ThrowIfNull(oleObject);

        return new CadOleClipboardData(
            oleObject.GetBackingStorageBytes(),
            "application/x-ole-storage",
            string.IsNullOrWhiteSpace(oleObject.Name) ? "OLE Object" : oleObject.Name,
            oleObject.ResolveNaturalAspectRatio());
    }

    private static void EnsureStaThread()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("OLE clipboard operations must run on an STA thread.");
    }

    public sealed class CadOleEditSession : IDisposable
    {
        private readonly OleInitializationScope _initializer;
        private readonly DrawableOleObject _oleObject;
        private readonly Action<CadOleClipboardData?, bool>? _objectUpdated;
        private bool _disposed;

        internal CadOleEditSession(
            byte[] oleBytes,
            string objectName,
            Action<CadOleClipboardData?, bool>? objectUpdated)
        {
            _initializer = OleInitializationScope.Enter();
            _objectUpdated = objectUpdated;
            _oleObject = DrawableOleObjectFactory.CreateFromBytes(oleBytes);
            _oleObject.HostViewChanged += OnHostViewChanged;
            _oleObject.HostClosed += OnHostClosed;
            _oleObject.Saved += OnSaved;

            if (!string.IsNullOrWhiteSpace(objectName))
                _oleObject.Name = objectName;
        }

        internal void Open(IntPtr parentHwnd, string containerName)
        {
            ThrowIfDisposed();
            _oleObject.OpenEditor(parentHwnd, containerName);
        }

        public CadOleDrawData DrawRegion(
            int fullPixelWidth,
            int fullPixelHeight,
            int regionX,
            int regionY,
            int regionWidth,
            int regionHeight)
        {
            ThrowIfDisposed();
            return CadOleServices.DrawRegion(
                _oleObject,
                fullPixelWidth,
                fullPixelHeight,
                regionX,
                regionY,
                regionWidth,
                regionHeight);
        }

        private void OnHostViewChanged(object? sender, EventArgs e)
        {
            if (!_disposed)
                _objectUpdated?.Invoke(null, false);
        }

        private void OnHostClosed(object? sender, EventArgs e) => PublishPersistedUpdate();

        private void OnSaved(object? sender, EventArgs e) => PublishPersistedUpdate();

        private void PublishPersistedUpdate()
        {
            if (_disposed || _objectUpdated is null)
                return;

            _objectUpdated(CreateClipboardData(_oleObject), true);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _oleObject.HostViewChanged -= OnHostViewChanged;
            _oleObject.HostClosed -= OnHostClosed;
            _oleObject.Saved -= OnSaved;
            _oleObject.Dispose();
            _initializer.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CadOleEditSession));
        }
    }

    public sealed class CadOleRenderSession : IDisposable
    {
        private readonly OleInitializationScope _initializer;
        private readonly DrawableOleObject _oleObject;
        private bool _disposed;

        internal CadOleRenderSession(byte[] oleBytes)
        {
            _initializer = OleInitializationScope.Enter();
            _oleObject = DrawableOleObjectFactory.CreateFromBytes(oleBytes);
        }

        public CadOleDrawData DrawRegion(
            int fullPixelWidth,
            int fullPixelHeight,
            int regionX,
            int regionY,
            int regionWidth,
            int regionHeight)
        {
            ThrowIfDisposed();
            return CadOleServices.DrawRegion(
                _oleObject,
                fullPixelWidth,
                fullPixelHeight,
                regionX,
                regionY,
                regionWidth,
                regionHeight);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _oleObject.Dispose();
            _initializer.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CadOleRenderSession));
        }
    }
}
