using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject(AllowPrivate = true)]
internal sealed class CadDocumentSectionV1
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = "Untitled";
}
