using System.Globalization;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.ViewModels.Tools;

internal static partial class CadEntityQuery
{
    private sealed class QueryCounts(bool includeGroups = false)
    {
        private readonly Dictionary<string, int> _types = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<LayerId, int> _layers = [];
        private readonly Dictionary<BlockId, int> _owners = [];
        public int Count { get; private set; }

        public void Add(CadEntity entity)
        {
            Count++;
            var type = EntityType(entity);
            _types[type] = _types.GetValueOrDefault(type) + 1;
            if (!includeGroups)
                return;
            _layers[entity.LayerId] = _layers.GetValueOrDefault(entity.LayerId) + 1;
            _owners[entity.OwnerBlockId] = _owners.GetValueOrDefault(entity.OwnerBlockId) + 1;
        }

        public object[] TypeCounts() => _types.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (object)new { type = pair.Key, count = pair.Value }).ToArray();

        public object Summary(CadDocument document) => new
        {
            entity_count = Count,
            type_counts = TypeCounts(),
            layer_counts = _layers.Select(pair => new
                { layer_id = pair.Key.Value, layer = LayerName(document, pair.Key), count = pair.Value })
                .OrderByDescending(item => item.count).ThenBy(item => item.layer, StringComparer.OrdinalIgnoreCase).ToArray(),
            owner_space_counts = _owners.Select(pair =>
            {
                document.TryGetBlock(pair.Key, out var block);
                return new
                {
                    owner_block_id = pair.Key.Value,
                    owner = block?.Name ?? pair.Key.Value.ToString(CultureInfo.InvariantCulture),
                    kind = block?.Kind.ToString() ?? "Unknown",
                    count = pair.Value
                };
            }).OrderByDescending(item => item.count).ThenBy(item => item.owner, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
