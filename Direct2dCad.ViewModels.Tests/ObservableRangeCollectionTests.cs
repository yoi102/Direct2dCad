using System.Collections.Specialized;
using System.ComponentModel;
using Direct2dCad.ViewModels.Collections;

namespace Direct2dCad.ViewModels.Tests;

public sealed class ObservableRangeCollectionTests
{
    [Fact]
    public void ReplaceItemsBatchesNotificationsAndPreservesUnchangedItems()
    {
        var unchanged = new object();
        var items = new ObservableRangeCollection<object>([new(), unchanged, new()]);
        var changes = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();
        items.CollectionChanged += (_, e) => changes.Add(e.Action);
        ((INotifyPropertyChanged)items).PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        var replacement = new object();
        items.ReplaceItems(new Dictionary<int, object> { [0] = replacement, [2] = replacement });
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        Assert.Equal(["Item[]"], properties);
        Assert.Same(unchanged, items[1]);
        Assert.Same(replacement, items[0]);
        Assert.Same(replacement, items[2]);
    }

    [Fact]
    public void ReplaceItemsValidatesBeforeAnyMutationAndSingleChangeUsesReplace()
    {
        var items = new ObservableRangeCollection<int>([1, 2, 3]);
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => changes.Add(e.Action);
        Assert.Throws<ArgumentOutOfRangeException>(() => items.ReplaceItems(
            new Dictionary<int, int> { [0] = 10, [3] = 20 }));
        Assert.Equal([1, 2, 3], items);
        Assert.Empty(changes);
        items.ReplaceItems(new Dictionary<int, int>());
        Assert.Empty(changes);
        items.ReplaceItems(new Dictionary<int, int> { [1] = 20 });
        Assert.Equal([NotifyCollectionChangedAction.Replace], changes);
        Assert.Equal([1, 20, 3], items);
    }
}
