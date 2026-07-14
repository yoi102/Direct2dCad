using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadBlockReferencesSection
{
    [Key(0)] public List<CadBlockReferenceData> BlockReferences { get; set; } = [];
}
