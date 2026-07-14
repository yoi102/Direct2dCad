using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Cad;

public sealed record CadDetachedBlockDefinition(
    CadBlockDefinition Definition,
    IReadOnlyList<CadEntity> Entities);
