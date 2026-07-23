using System.Windows;
using System.Windows.Interop;
using Direct2dCad.Rendering;

namespace Direct2dCad.wpf.Controls;

public sealed class D3D11ImageSource : D3DImage, IDisposable, ID3D11ImageSource
{
    private IntPtr _surface9Ptr;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _hasBackBuffer;
    private bool _disposed;

    public int SurfaceWidth => _surfaceWidth;
    public int SurfaceHeight => _surfaceHeight;

    public D3D11ImageSource()
    {
        IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;
    }

    public void SetSurface(nint surface9Ptr)
    {
        ThrowIfDisposed();

        // 指针相同，但 back buffer 不存在时，也需要重新绑定
        if (_surface9Ptr == surface9Ptr && _hasBackBuffer)
            return;

        _surface9Ptr = surface9Ptr;
        ApplyBackBuffer();
    }

    public void SetSurface(nint surface9Ptr, int width, int height)
    {
        SetSize(width, height);
        SetSurface(surface9Ptr);
    }

    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
            return;

        _surfaceWidth = width;
        _surfaceHeight = height;
    }

    private void ApplyBackBuffer()
    {
        if (!IsFrontBufferAvailable)
        {
            _hasBackBuffer = false;
            return;
        }

        Lock();
        try
        {
            if (_surface9Ptr == IntPtr.Zero)
            {
                SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _hasBackBuffer = false;
            }
            else
            {
                SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface9Ptr);
                _hasBackBuffer = true;
            }
        }
        finally
        {
            Unlock();
        }
    }

    private void OnIsFrontBufferAvailableChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (IsFrontBufferAvailable)
        {
            // WPF front buffer 恢复后，需要重新绑定 back buffer
            ApplyBackBuffer();
            Invalidate();
        }
        else
        {
            // front buffer 不可用时，停止 Invalidate / Render
            _hasBackBuffer = false;
        }
    }

    public void Invalidate()
    {
        if (!_hasBackBuffer)
            return;

        if (!IsFrontBufferAvailable)
            return;

        if (_surfaceWidth <= 0 || _surfaceHeight <= 0)
            return;

        Lock();
        try
        {
            AddDirtyRect(new Int32Rect(0, 0, _surfaceWidth, _surfaceHeight));
        }
        finally
        {
            Unlock();
        }
    }

    public void Invalidate(IntRect dirtyRect)
    {
        Invalidate(new[] { dirtyRect });
    }

    public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presentAction);

        if (!_hasBackBuffer || !IsFrontBufferAvailable)
        {
            presentAction();
            return;
        }

        Lock();
        try
        {
            presentAction();
            if (dirtyRects is { Count: > 0 })
            {
                foreach (var dirtyRect in dirtyRects)
                {
                    var clampedRect = ClampDirtyRect(dirtyRect);
                    if (clampedRect.Width > 0 && clampedRect.Height > 0)
                        AddDirtyRect(clampedRect);
                }
            }
            else if (_surfaceWidth > 0 && _surfaceHeight > 0)
            {
                AddDirtyRect(new Int32Rect(0, 0, _surfaceWidth, _surfaceHeight));
            }
        }
        finally
        {
            Unlock();
        }
    }

    public void Invalidate(Int32Rect dirtyRect)
    {
        if (!_hasBackBuffer)
            return;

        if (!IsFrontBufferAvailable)
            return;

        if (_surfaceWidth <= 0 || _surfaceHeight <= 0)
            return;

        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        dirtyRect = ClampDirtyRect(dirtyRect);
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        Lock();
        try
        {
            AddDirtyRect(dirtyRect);
        }
        finally
        {
            Unlock();
        }
    }

    public void Invalidate(IReadOnlyList<IntRect> dirtyRects)
    {
        if (!_hasBackBuffer)
            return;

        if (!IsFrontBufferAvailable)
            return;

        if (_surfaceWidth <= 0 || _surfaceHeight <= 0)
            return;

        if (dirtyRects.Count == 0)
            return;

        var clampedRects = new List<Int32Rect>(dirtyRects.Count);
        foreach (var dirtyRect in dirtyRects)
        {
            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
                continue;

            var clampedRect = ClampDirtyRect(dirtyRect);
            if (clampedRect.Width > 0 && clampedRect.Height > 0)
                clampedRects.Add(clampedRect);
        }

        if (clampedRects.Count == 0)
            return;

        Lock();
        try
        {
            foreach (var clampedRect in clampedRects)
                AddDirtyRect(clampedRect);
        }
        finally
        {
            Unlock();
        }
    }

    private Int32Rect ClampDirtyRect(Int32Rect dirtyRect)
    {
        var x = Math.Clamp(dirtyRect.X, 0, _surfaceWidth);
        var y = Math.Clamp(dirtyRect.Y, 0, _surfaceHeight);
        var right = (int)Math.Clamp(
            (long)dirtyRect.X + dirtyRect.Width,
            0L,
            _surfaceWidth);
        var bottom = (int)Math.Clamp(
            (long)dirtyRect.Y + dirtyRect.Height,
            0L,
            _surfaceHeight);
        return new Int32Rect(x, y, right - x, bottom - y);
    }

    private Int32Rect ClampDirtyRect(IntRect dirtyRect)
    {
        return ClampDirtyRect(new Int32Rect(
            dirtyRect.X,
            dirtyRect.Y,
            dirtyRect.Width,
            dirtyRect.Height));
    }

    public void Detach()
    {
        if (_disposed)
            return;

        Lock();
        try
        {
            SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
        }
        finally
        {
            Unlock();
        }

        _surface9Ptr = IntPtr.Zero;
        _surfaceWidth = 0;
        _surfaceHeight = 0;
        _hasBackBuffer = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged;

        Detach();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(D3D11ImageSource));
    }
}
