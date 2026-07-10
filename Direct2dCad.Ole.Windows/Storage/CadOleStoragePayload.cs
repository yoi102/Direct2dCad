using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Direct2dCad.Ole.Windows;

internal sealed class CadOleStoragePayload(byte[] storageBytes, DVASPECT drawAspect, string name)
{
    private static readonly byte[] StorageMagic = Encoding.ASCII.GetBytes("D2CAD-OLE1");

    public byte[] StorageBytes { get; } = storageBytes;

    public DVASPECT DrawAspect { get; } = drawAspect;

    public string Name { get; } = name;

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(StorageMagic.Length);
        writer.Write(StorageMagic);
        writer.Write(Name ?? string.Empty);
        writer.Write((int)DrawAspect);
        writer.Write(StorageBytes.Length);
        writer.Write(StorageBytes);
        writer.Flush();

        return stream.ToArray();
    }

    public static CadOleStoragePayload FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magicLength = reader.ReadInt32();
        var magic = reader.ReadBytes(magicLength);
        if (!magic.SequenceEqual(StorageMagic))
            throw new InvalidDataException("Invalid OLE storage payload.");

        var name = reader.ReadString();
        var drawAspect = (DVASPECT)reader.ReadInt32();
        var storageLength = reader.ReadInt32();
        if (storageLength <= 0)
            throw new InvalidDataException("OLE storage payload is empty.");

        var storageBytes = reader.ReadBytes(storageLength);
        if (storageBytes.Length != storageLength)
            throw new EndOfStreamException("OLE storage payload is truncated.");

        return new CadOleStoragePayload(storageBytes, drawAspect, name);
    }
}
