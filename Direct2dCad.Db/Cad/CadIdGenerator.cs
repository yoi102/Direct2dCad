namespace Direct2dCad.Db.Cad;

/// <summary>
/// CAD 对象 ID 生成器。
/// 默认保留：
/// LayerId.Default = 1，BlockId.ModelSpace = 1，BlockId.PaperSpace = 2，StyleId.DefaultGraphic = 1，LineTypeId.Continuous = 1。
/// </summary>
public sealed class CadIdGenerator
{
    private long _nextDocumentId;
    private long _nextEntityId;
    private long _nextLayerId;
    private long _nextBlockId;
    private long _nextStyleId;
    private long _nextHatchPatternId;

    public CadIdGenerator(
        long nextDocumentId = 1,
        long nextEntityId = 1,
        long nextLayerId = 2,
        long nextBlockId = 3,
        long nextStyleId = 2,
        long nextHatchPatternId = 1)
    {
        _nextDocumentId = GuardNext(nextDocumentId, nameof(nextDocumentId));
        _nextEntityId = GuardNext(nextEntityId, nameof(nextEntityId));
        _nextLayerId = Math.Max(2, GuardNext(nextLayerId, nameof(nextLayerId)));
        _nextBlockId = Math.Max(3, GuardNext(nextBlockId, nameof(nextBlockId)));
        _nextStyleId = Math.Max(2, GuardNext(nextStyleId, nameof(nextStyleId)));
        _nextHatchPatternId = GuardNext(nextHatchPatternId, nameof(nextHatchPatternId));
    }

    public DocumentId NewDocumentId() => new(_nextDocumentId++);
    public EntityId NewEntityId() => new(_nextEntityId++);
    public LayerId NewLayerId() => new(_nextLayerId++);
    public BlockId NewBlockId() => new(_nextBlockId++);
    public StyleId NewStyleId() => new(_nextStyleId++);
    public HatchPatternId NewHatchPatternId() => new(_nextHatchPatternId++);

    internal void RegisterExisting(DocumentId id) => _nextDocumentId = Math.Max(_nextDocumentId, id.Value + 1);
    internal void RegisterExisting(EntityId id) => _nextEntityId = Math.Max(_nextEntityId, id.Value + 1);
    internal void RegisterExisting(LayerId id) => _nextLayerId = Math.Max(_nextLayerId, id.Value + 1);
    internal void RegisterExisting(BlockId id) => _nextBlockId = Math.Max(_nextBlockId, id.Value + 1);
    internal void RegisterExisting(StyleId id) => _nextStyleId = Math.Max(_nextStyleId, id.Value + 1);
    internal void RegisterExisting(HatchPatternId id) => _nextHatchPatternId = Math.Max(_nextHatchPatternId, id.Value + 1);

    private static long GuardNext(long value, string paramName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Next id must be greater than 0.");

        return value;
    }
}
