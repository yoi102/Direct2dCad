using MessagePack;

namespace Direct2dCad.IO.FileFormat.Common;

[MessagePackObject]
public readonly record struct CadGradientStopData(
    [property: Key(0)] double Offset,
    [property: Key(1)] CadColorData Color);
