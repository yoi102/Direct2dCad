using System.Text;
using System.Text.Json;

namespace Direct2dCad.Mcp.Stdio;

/// <summary>
/// Read-only view of the stdio input that keeps the HTTP listener's rule: a JSON-RPC message with
/// duplicate property names is refused instead of letting the SDK silently keep one of the values.
/// </summary>
/// <remarks>
/// A refused request is rewritten as an unparsable line that still starts with its id, so the SDK's
/// stream transport answers it with a -32700 error for that id; a refused notification is dropped.
/// Any other line passes through unchanged. Lines are split on CR as well as LF, as the SDK's
/// reader does, so a bare CR cannot hide a second message inside a line this filter accepted.
/// </remarks>
internal sealed class DuplicatePropertyRejectingStream(Stream inner, Action<string>? log = null) : Stream
{
    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions Lenient = new() { AllowDuplicateProperties = true };

    private readonly byte[] _chunk = new byte[16 * 1024];
    private readonly MemoryStream _line = new();
    private byte[] _ready = [];
    private int _readyOffset;
    private bool _endOfInput;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty)
            return 0;
        while (true)
        {
            if (_readyOffset < _ready.Length)
            {
                var count = Math.Min(destination.Length, _ready.Length - _readyOffset);
                _ready.AsMemory(_readyOffset, count).CopyTo(destination);
                _readyOffset += count;
                return count;
            }
            if (_endOfInput)
                return 0;

            var read = await inner.ReadAsync(_chunk, cancellationToken).ConfigureAwait(false);
            using var lines = new MemoryStream();
            if (read == 0)
            {
                _endOfInput = true;
                FlushLine(lines);
            }
            else
            {
                var start = 0;
                for (var index = 0; index < read; index++)
                {
                    if (_chunk[index] is not ((byte)'\n' or (byte)'\r'))
                        continue;
                    _line.Write(_chunk, start, index - start);
                    FlushLine(lines);
                    start = index + 1;
                }
                _line.Write(_chunk, start, read - start);
            }
            _ready = lines.ToArray();
            _readyOffset = 0;
        }
    }

    private void FlushLine(MemoryStream output)
    {
        if (_line.Length == 0)
            return;
        if (Filter(_line.ToArray()) is { } accepted)
        {
            output.Write(accepted);
            output.WriteByte((byte)'\n');
        }
        _line.SetLength(0);
    }

    /// <summary>Returns the line to forward, a replacement, or null to drop it.</summary>
    private byte[]? Filter(byte[] line)
    {
        try
        {
            using (JsonDocument.Parse(line, Strict))
                return line;
        }
        catch (JsonException)
        {
        }

        string? id;
        try
        {
            using var document = JsonDocument.Parse(line, Lenient);
            id = document.RootElement.ValueKind == JsonValueKind.Object &&
                 document.RootElement.TryGetProperty("id", out var value) &&
                 value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                ? value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return line; // Not a duplicate-name problem; the SDK reports malformed JSON itself.
        }

        if (id is null)
        {
            log?.Invoke("Dropped a JSON-RPC notification with duplicate property names.");
            return null;
        }
        log?.Invoke($"Rejected JSON-RPC request {id}: duplicate property names.");
        return Encoding.UTF8.GetBytes($$"""{"id":{{id}},"error":"duplicate property names":}""");
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            _line.Dispose();
        }
        base.Dispose(disposing);
    }
}
