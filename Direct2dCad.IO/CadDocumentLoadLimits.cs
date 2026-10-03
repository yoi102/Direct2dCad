using Direct2dCad.IO.FileFormat.Container;
using MessagePack;
using MessagePack.Formatters;

namespace Direct2dCad.IO;

public sealed record CadDocumentLoadLimits
{
    public long MaximumFileBytes { get; init; } = 512L * 1024 * 1024;
    public int MaximumSectionBytes { get; init; } = 256 * 1024 * 1024;
    public long MaximumDecodedBytes { get; init; } = 512L * 1024 * 1024;
    public long MaximumEmbeddedBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumCollectionItems { get; init; } = 1_000_000;
    public int MaximumEntities { get; init; } = 1_000_000;
    public long MaximumValues { get; init; } = 16_000_000;
    public int MaximumDepth { get; init; } = 64;
    public int MaximumBlockDepth { get; init; } = 32;
    public long MaximumImagePixels { get; init; } = 64L*1024*1024;
    public int MaximumStringBytes { get; init; } = 16 * 1024 * 1024;

    internal void ValidateTable(IReadOnlyList<CadSectionEntry> entries, long fileLength)
    {
        if (fileLength > MaximumFileBytes || entries.Any(e => e.PayloadLength > MaximumSectionBytes))
            throw new InvalidDataException("The drawing exceeds the configured file/section size budget.");
    }

    internal MessagePackSerializerOptions Secure(MessagePackSerializerOptions options) => options.WithSecurity(
        MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(MaximumDepth)
            .WithMaximumDecompressedSize(MaximumSectionBytes));

    internal sealed class DecodeBudget(CadDocumentLoadLimits limits, CancellationToken token)
    {
        private long _decoded, _embedded, _values;
        public void Validate(byte[] payload, MessagePackSerializerOptions options)
        {
            var stamp = MessagePackSerializer.Deserialize<Stamp>(payload,
                limits.Secure(options).WithResolver(new StampResolver(this)), token);
            _decoded = checked(_decoded + stamp.Bytes);
            if (_decoded > limits.MaximumDecodedBytes)
                throw new InvalidDataException("The drawing exceeds the total decompression budget.");
        }
        private void Scan(ref MessagePackReader reader, int depth)
        {
            if (++_values > limits.MaximumValues || depth > limits.MaximumDepth)
                throw new InvalidDataException("The drawing exceeds its collection or nesting budget.");
            if ((_values & 1023) == 0) token.ThrowIfCancellationRequested();
            switch (reader.NextMessagePackType)
            {
                case MessagePackType.Array:
                    var count = reader.ReadArrayHeader();
                    CheckCount(count);
                    for (var i = 0; i < count; i++) Scan(ref reader, depth + 1);
                    break;
                case MessagePackType.Map:
                    var pairs = reader.ReadMapHeader();
                    CheckCount(pairs);
                    for (var i = 0; i < pairs; i++) { Scan(ref reader, depth + 1); Scan(ref reader, depth + 1); }
                    break;
                case MessagePackType.Binary:
                    _embedded = checked(_embedded + (reader.ReadBytes()?.Length ?? 0));
                    if (_embedded > limits.MaximumEmbeddedBytes) throw new InvalidDataException("Embedded content exceeds the drawing budget.");
                    break;
                case MessagePackType.String:
                    if ((reader.ReadStringSequence()?.Length ?? 0) > limits.MaximumStringBytes)
                        throw new InvalidDataException("A drawing string exceeds the size budget.");
                    break;
                default: reader.Skip(); break;
            }
        }
        private void CheckCount(int count)
        {
            if (count > limits.MaximumCollectionItems)
                throw new InvalidDataException("A drawing collection exceeds the item budget.");
        }
        internal readonly record struct Stamp(long Bytes);
        private sealed class StampResolver(DecodeBudget budget) : IFormatterResolver
        {
            public IMessagePackFormatter<T>? GetFormatter<T>() => typeof(T) == typeof(Stamp)
                ? (IMessagePackFormatter<T>)(object)new StampFormatter(budget) : null;
        }
        [ExcludeFormatterFromSourceGeneratedResolver]
        internal sealed class StampFormatter(DecodeBudget budget) : IMessagePackFormatter<Stamp>
        {
            public void Serialize(ref MessagePackWriter writer, Stamp value, MessagePackSerializerOptions options) => throw new NotSupportedException();
            public Stamp Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                var start = reader.Consumed;
                budget.Scan(ref reader, 0);
                if (!reader.End) throw new InvalidDataException("Trailing data in a drawing section.");
                return new(reader.Consumed - start);
            }
        }
    }
}
