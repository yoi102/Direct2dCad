using MessagePack;

namespace Direct2dCad.IO.FileFormat.Common;

[MessagePackObject]
public readonly record struct CadColorData(
    [property: Key(0)] byte A,
    [property: Key(1)] byte R,
    [property: Key(2)] byte G,
    [property: Key(3)] byte B);
