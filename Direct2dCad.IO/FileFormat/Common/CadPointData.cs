using MessagePack;

namespace Direct2dCad.IO.FileFormat.Common;

[MessagePackObject]
public readonly record struct CadPointData(
    [property: Key(0)] double X,
    [property: Key(1)] double Y);
