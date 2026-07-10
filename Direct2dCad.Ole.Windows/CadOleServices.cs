namespace Direct2dCad.Ole.Windows;

public static class CadOleServices
{
    public static CadOleClipboardData? TryCreateFromClipboard(int maxPreviewPixelSide = 1024)
    {
        EnsureStaThread();
        using var initializer = OleInitializationScope.Enter();
        using var oleObject = DrawableOleObjectFactory.CreateFromClipboard();
        return oleObject is null
            ? null
            : CreateClipboardData(oleObject, maxPreviewPixelSide);
    }

    public static CadOleEditSession BeginEdit(
        byte[] oleBytes,
        IntPtr parentHwnd,
        string objectName = "",
        string containerName = "Direct2dCad",
        int maxPreviewPixelSide = 2048,
        Action<CadOleClipboardData, bool>? previewUpdated = null)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();

        var session = new CadOleEditSession(oleBytes, objectName, maxPreviewPixelSide, previewUpdated);
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

    public static CadOleClipboardData CreatePreview(byte[] oleBytes, int maxPreviewPixelSide = 2048)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();

        using var initializer = OleInitializationScope.Enter();
        using var oleObject = DrawableOleObjectFactory.CreateFromBytes(oleBytes);
        return CreateClipboardData(oleObject, maxPreviewPixelSide, upscaleToTarget: true);
    }

    public static CadOleDrawData Draw(byte[] oleBytes, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();

        using var initializer = OleInitializationScope.Enter();
        using var oleObject = DrawableOleObjectFactory.CreateFromBytes(oleBytes);
        return Draw(oleObject, pixelWidth, pixelHeight);
    }

    public static CadOleRenderSession CreateRenderSession(byte[] oleBytes)
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        EnsureStaThread();
        return new CadOleRenderSession(oleBytes);
    }

    internal static CadOleDrawData Draw(DrawableOleObject oleObject, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(oleObject);

        pixelWidth = Math.Clamp(pixelWidth, 1, 8192);
        pixelHeight = Math.Clamp(pixelHeight, 1, 8192);

        var pixels = oleObject.Draw(pixelWidth, pixelHeight);
        return new CadOleDrawData(
            pixelWidth,
            pixelHeight,
            checked(pixelWidth * 4),
            pixels);
    }

    internal static CadOleClipboardData CreateClipboardData(
        DrawableOleObject oleObject,
        int maxPreviewPixelSide,
        bool upscaleToTarget = false)
    {
        ArgumentNullException.ThrowIfNull(oleObject);

        var (previewWidth, previewHeight) = oleObject.ResolvePreviewPixelSize(maxPreviewPixelSide, upscaleToTarget);
        var pixels = oleObject.Draw(previewWidth, previewHeight);
        return new CadOleClipboardData(
            previewWidth,
            previewHeight,
            checked(previewWidth * 4),
            pixels,
            oleObject.GetBackingStorageBytes(),
            "application/x-ole-storage",
            string.IsNullOrWhiteSpace(oleObject.Name) ? "OLE Object" : oleObject.Name);
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
        private readonly int _maxPreviewPixelSide;
        private readonly Action<CadOleClipboardData, bool>? _previewUpdated;
        private bool _disposed;

        internal CadOleEditSession(
            byte[] oleBytes,
            string objectName,
            int maxPreviewPixelSide,
            Action<CadOleClipboardData, bool>? previewUpdated)
        {
            _initializer = OleInitializationScope.Enter();
            _maxPreviewPixelSide = maxPreviewPixelSide;
            _previewUpdated = previewUpdated;
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

        public CadOleClipboardData CreatePreview(int maxPreviewPixelSide)
        {
            ThrowIfDisposed();
            return CreateClipboardData(_oleObject, maxPreviewPixelSide, upscaleToTarget: true);
        }

        public CadOleDrawData Draw(int pixelWidth, int pixelHeight)
        {
            ThrowIfDisposed();
            return CadOleServices.Draw(_oleObject, pixelWidth, pixelHeight);
        }

        private void OnHostViewChanged(object? sender, EventArgs e) => PublishUpdatedPreview(isPersisted: false);

        private void OnHostClosed(object? sender, EventArgs e) => PublishUpdatedPreview(isPersisted: true);

        private void OnSaved(object? sender, EventArgs e) => PublishUpdatedPreview(isPersisted: true);

        private void PublishUpdatedPreview(bool isPersisted)
        {
            if (_disposed || _previewUpdated is null)
                return;

            _previewUpdated(
                CreateClipboardData(_oleObject, _maxPreviewPixelSide, upscaleToTarget: true),
                isPersisted);
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

        public CadOleDrawData Draw(int pixelWidth, int pixelHeight)
        {
            ThrowIfDisposed();
            return CadOleServices.Draw(_oleObject, pixelWidth, pixelHeight);
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
