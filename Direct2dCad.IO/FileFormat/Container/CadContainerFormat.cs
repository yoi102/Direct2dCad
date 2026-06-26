using System.Text;

namespace Direct2dCad.IO.FileFormat.Container;

internal static class CadContainerFormat
{
    internal const int CurrentContainerVersion = 1;
    internal const int FileHeaderLength = 25;
    internal const int SectionEntryLength = 19;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("D2CAD");

    internal static void WriteHeader(BinaryWriter writer, CadFileHeader header)
    {
        writer.Write(Magic);
        writer.Write(header.ContainerVersion);
        writer.Write(header.SectionCount);
        writer.Write(header.SectionTableOffset);
        writer.Write(header.SectionTableLength);
    }

    internal static CadFileHeader ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Invalid .d2cad file header.");

        return new CadFileHeader(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt64(),
            reader.ReadInt32());
    }

    internal static void WriteSectionEntry(BinaryWriter writer, CadSectionEntry entry)
    {
        writer.Write((ushort)entry.Kind);
        writer.Write(entry.Version);
        writer.Write((byte)entry.Compression);
        writer.Write(entry.PayloadOffset);
        writer.Write(entry.PayloadLength);
    }

    internal static CadSectionEntry ReadSectionEntry(BinaryReader reader)
    {
        return new CadSectionEntry(
            (CadSectionKind)reader.ReadUInt16(),
            reader.ReadInt32(),
            (CadCompressionKind)reader.ReadByte(),
            reader.ReadInt64(),
            reader.ReadInt32());
    }
}
