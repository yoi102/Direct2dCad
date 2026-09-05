using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadOleObject : CadEntity
{
    private CadRectD _bounds;
    private byte[] _oleBytes;
    private IReadOnlyList<byte>? _oleBytesView;

    public override CadRectD Bounds => _bounds;

    public string ContentType { get; private set; }

    public string SourceName { get; private set; }

    public double Opacity { get; private set; }

    public IReadOnlyList<byte> OleBytes => _oleBytesView ??= Array.AsReadOnly(_oleBytes);

    // SetOleData replaces owned storage, so existing read-only snapshots remain valid.
    public ReadOnlyMemory<byte> OleMemory => _oleBytes;

    internal CadOleObject(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadRectD bounds,
        ReadOnlySpan<byte> oleBytes,
        string contentType = "application/x-ole-storage",
        string sourceName = "",
        string name = "",
        double opacity = 1.0)
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
        _oleBytes = GuardBytes(oleBytes, 1, nameof(oleBytes));
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
        Opacity = GuardOpacity(opacity);
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
    }

    public void SetOleData(
        byte[] oleBytes,
        string contentType = "application/x-ole-storage",
        string sourceName = "")
    {
        ArgumentNullException.ThrowIfNull(oleBytes);
        _oleBytes = GuardBytes(oleBytes, 1, nameof(oleBytes));
        _oleBytesView = null;
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
    }

    public byte[] CopyOleBytes()
    {
        return (byte[])_oleBytes.Clone();
    }

    public void SetOpacity(double opacity)
    {
        Opacity = GuardOpacity(opacity);
    }

    private static double GuardOpacity(double opacity)
    {
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity));

        return Math.Clamp(opacity, 0.0, 1.0);
    }

    private static CadRectD GuardBounds(CadRectD bounds)
    {
        return bounds.IsEmpty ||
               bounds.Width <= 0 ||
               bounds.Height <= 0 ||
               double.IsNaN(bounds.Width) ||
               double.IsNaN(bounds.Height) ||
               double.IsInfinity(bounds.Width) ||
               double.IsInfinity(bounds.Height)
            ? throw new ArgumentOutOfRangeException(nameof(bounds))
            : bounds;
    }

    private static byte[] GuardBytes(ReadOnlySpan<byte> bytes, int minimumLength, string paramName)
    {
        if (bytes.Length < minimumLength)
            throw new ArgumentException("Data is shorter than expected.", paramName);

        return bytes.ToArray();
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "application/x-ole-storage"
            : contentType.Trim();
    }
}
