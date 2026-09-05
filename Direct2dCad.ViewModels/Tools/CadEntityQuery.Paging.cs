using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.ViewModels.Tools;

internal static partial class CadEntityQuery
{
    private static object[] SelectPage(CadDocument document, IEnumerable<CadEntity> entities,
        QueryCounts counts, CadEntityQueryOptions options)
    {
        var limit = options.Offset >= document.Entities.Count ? 0 :
            (int)Math.Min((long)options.Offset + options.Limit, document.Entities.Count);
        var order = new PageKeyComparer(options.SortDescending);
        var worstFirst = Comparer<PageKey>.Create((left, right) => order.Compare(right, left));
        var candidates = new PriorityQueue<CadEntity, PageKey>(worstFirst);
        var keySelector = CreateSortKeySelector(document, options);
        foreach (var entity in entities)
        {
            counts.Add(entity);
            if (limit == 0)
                continue;
            var key = new PageKey(keySelector(entity), entity.Id.Value);
            if (candidates.Count < limit)
                candidates.Enqueue(entity, key);
            else if (candidates.TryPeek(out _, out var worst) && order.Compare(key, worst) < 0)
                candidates.DequeueEnqueue(entity, key);
        }
        var page = candidates.UnorderedItems.ToArray();
        Array.Sort(page, (left, right) => order.Compare(left.Priority, right.Priority));
        return page.Skip(options.Offset).Take(options.Limit)
            .Select(item => EntityDto(document, item.Element)).ToArray();
    }

    private readonly record struct PageKey(object? Value, long Id);

    private sealed class PageKeyComparer(bool descending) : IComparer<PageKey>
    {
        public int Compare(PageKey left, PageKey right)
        {
            var primary = descending
                ? CadQueryValueComparer.Instance.Compare(right.Value, left.Value)
                : CadQueryValueComparer.Instance.Compare(left.Value, right.Value);
            return primary != 0 ? primary : left.Id.CompareTo(right.Id);
        }
    }
}
