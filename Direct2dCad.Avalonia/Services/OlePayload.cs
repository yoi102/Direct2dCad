using System.Text;
namespace Direct2dCad.Avalonia.Services;

/// <summary>Preserves the WPF client's D2CAD-OLE1 storage envelope byte for byte.</summary>
internal sealed record OlePayload(byte[] Storage, int Aspect, string Name)
{
    private static ReadOnlySpan<byte> Magic => "D2CAD-OLE1"u8;
    public byte[] Encode()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Magic.Length); writer.Write(Magic); writer.Write(Name); writer.Write(Aspect); writer.Write(Storage.Length); writer.Write(Storage); writer.Flush(); return stream.ToArray();
    }
    public static OlePayload Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("OLE payload exceeds its budget.");
        using var stream = new MemoryStream(bytes.ToArray()); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadInt32() != Magic.Length || !reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Invalid OLE payload signature.");
        var nameLength = reader.Read7BitEncodedInt(); if (nameLength is < 0 or > 4096) throw new InvalidDataException("OLE object name exceeds its budget.");
        var nameBytes = reader.ReadBytes(nameLength); if (nameBytes.Length != nameLength) throw new EndOfStreamException();
        var name = Encoding.UTF8.GetString(nameBytes); var aspect = reader.ReadInt32();
        if (aspect is not (1 or 2 or 4 or 8)) throw new InvalidDataException("Invalid OLE drawing aspect.");
        var length = reader.ReadInt32(); if (length <= 0 || length > stream.Length - stream.Position) throw new InvalidDataException("OLE storage is truncated.");
        return new(reader.ReadBytes(length), aspect, name);
    }
}
