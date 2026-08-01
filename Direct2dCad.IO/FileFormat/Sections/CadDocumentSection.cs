using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadDocumentSection
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public string Name { get; set; } = "Untitled";
}
