using System.Text.Json;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Tools;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadEntityQueryPagingTests
{
    public static IEnumerable<object[]> SortCases() =>
        from field in new[] { "id", "name", "type", "layer", "z_index", "length", "width", "height", "bounds_area" }
        from isDescending in new[] { false, true }
        select new object[] { field, isDescending };

    [Theory]
    [MemberData(nameof(SortCases))]
    public void BoundedPaging_MatchesFullSortingWithTiesNullsAndLargeOffsets(string field, bool descending)
    {
        var document = CadDocument.Create("Paging");
        var layer = document.CreateLayer("Other", CadColor.Red, new CadLineWeight(1));
        for (var i = 0; i < 80; i++)
        {
            var point = new CadPointD(i % 8, i / 8);
            CadEntity entity = (i % 3) switch
            {
                0 => document.AddLine(point, new(point.X + i % 5 + 1, point.Y + i % 3), layerId: i % 2 == 0 ? layer : null),
                1 => document.AddCircle(point, i % 7 + 1, layerId: i % 2 == 0 ? layer : null),
                _ => document.AddText("Text", point, i % 4 + 1, layerId: i % 2 == 0 ? layer : null)
            };
            entity.Rename(i % 2 == 0 ? "Same" : "same");
            entity.SetZIndex(i % 4);
        }
        var owner = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        document.MoveEntityToBlock(document.Entities.Values.Last().Id, owner);
        document.Entities.Values.First().Erase();
        var source = document.GetEntitiesInBlock(BlockId.ModelSpace).Where(entity => !entity.IsErased).ToArray();
        object? Key(CadEntity entity) => field switch
        {
            "id" => (double)entity.Id.Value,
            "name" => entity.Name,
            "type" => entity is CadLine ? "Line" : entity is CadCircle ? "Circle" : "Text",
            "layer" => document.GetLayer(entity.LayerId).Name,
            "z_index" => (double)entity.ZIndex,
            "length" => entity is Curve curve ? curve.Length : null,
            "width" => entity.Bounds.Width,
            "height" => entity.Bounds.Height,
            "bounds_area" => entity.Bounds.Width * entity.Bounds.Height,
            _ => throw new InvalidOperationException()
        };
        var comparer = Comparer<object?>.Create((left, right) => left is null ? (right is null ? 0 : 1) :
            right is null ? -1 : left is string text ? StringComparer.OrdinalIgnoreCase.Compare(text, (string)right) :
            ((double)left).CompareTo((double)right));
        var ordered = (descending ? source.OrderByDescending(Key, comparer) : source.OrderBy(Key, comparer))
            .ThenBy(entity => entity.Id.Value).ToArray();
        foreach (var offset in new[] { 0, 3, 70, 90, int.MaxValue })
        {
            var result = JsonSerializer.SerializeToElement(CadEntityQuery.CreatePage(document,
                BlockId.ModelSpace, new HashSet<EntityId>(), new(CadEntityQuery.CurrentSpaceScope, null, null, false,
                    Offset: offset, Limit: 7, SortBy: field, SortDescending: descending)));
            Assert.Equal(ordered.Skip(offset).Take(7).Select(entity => entity.Id.Value),
                result.GetProperty("entities").EnumerateArray().Select(entity => entity.GetProperty("id").GetInt64()));
            Assert.Equal(source.Length, result.GetProperty("scope_entity_count").GetInt32());
            Assert.Equal(source.Length, result.GetProperty("total_matches").GetInt32());
            Assert.Equal((long)offset + 7 < source.Length, result.GetProperty("has_more").GetBoolean());
        }
    }
}
