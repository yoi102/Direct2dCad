using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Direct2dCad.IO.FileFormat.Sections;
using MessagePack;

namespace Direct2dCad.IO.Versioning;

internal static class CadDocumentSectionMigrations
{
    internal static CadDocumentSection ReadVersion1(
        byte[] payload,
        MessagePackSerializerOptions options)
    {
        try
        {
            // A short-lived build wrote Guid payloads while the section version
            // was still 1. Keep those files readable as well as the original long format.
            return MessagePackSerializer.Deserialize<CadDocumentSection>(payload, options)
                   ?? throw new InvalidDataException("Document section version 1 payload is null.");
        }
        catch (MessagePackSerializationException)
        {
            var legacy = MessagePackSerializer.Deserialize<CadDocumentSectionV1>(payload, options)
                         ?? throw new InvalidDataException("Document section version 1 payload is null.");
            return new CadDocumentSection
            {
                Id = ConvertLegacyId(legacy.Id),
                Name = legacy.Name
            };
        }
    }

    private static Guid ConvertLegacyId(long id)
    {
        var source = Encoding.UTF8.GetBytes(
            $"Direct2dCad.DocumentId.v1:{id.ToString(CultureInfo.InvariantCulture)}");
        var hash = SHA256.HashData(source);

        // Mark the deterministic result as an RFC 4122 name-based UUID.
        hash[7] = (byte)((hash[7] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }
}
