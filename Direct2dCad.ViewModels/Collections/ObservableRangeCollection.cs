using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Direct2dCad.ViewModels.Collections;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChangedEventArgs = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChangedEventArgs = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs CollectionResetEventArgs =
        new(NotifyCollectionChangedAction.Reset);

    public ObservableRangeCollection()
    {
    }

    public ObservableRangeCollection(IEnumerable<T> collection)
        : base(collection)
    {
    }

    public void AddRange(IEnumerable<T> collection)
    {
        var items = Materialize(collection);
        if (items.Count == 0)
            return;

        CheckReentrancy();
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    public void AddRangeAndTrimStart(IEnumerable<T> collection, int maximumCount)
    {
        if (maximumCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        var items = Materialize(collection);
        if (items.Count == 0 && Items.Count <= maximumCount)
            return;

        CheckReentrancy();
        foreach (var item in items)
            Items.Add(item);
        while (Items.Count > maximumCount)
            Items.RemoveAt(0);
        RaiseReset();
    }

    public void RemoveRange(int index, int count)
    {
        if (index < 0 || index > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0 || count > Count - index)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return;

        CheckReentrancy();
        for (var itemIndex = 0; itemIndex < count; itemIndex++)
            Items.RemoveAt(index);
        RaiseReset();
    }

    public void ReplaceRange(IEnumerable<T> collection)
    {
        var items = Materialize(collection);
        if (Items.Count == 0 && items.Count == 0)
            return;

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    public void ReplaceItems(IReadOnlyDictionary<int, T> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0)
            return;
        CheckReentrancy();
        foreach (var index in replacements.Keys)
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(replacements));

        if (replacements.Count == 1)
        {
            foreach (var (index, value) in replacements)
                SetItem(index, value);
            return;
        }

        foreach (var (index, value) in replacements)
            Items[index] = value;
        // WPF collection views do not support multi-item Replace notifications.
        OnPropertyChanged(IndexerChangedEventArgs);
        OnCollectionChanged(CollectionResetEventArgs);
    }

    private void RaiseReset()
    {
        OnPropertyChanged(CountChangedEventArgs);
        OnPropertyChanged(IndexerChangedEventArgs);
        OnCollectionChanged(CollectionResetEventArgs);
    }

    private IReadOnlyList<T> Materialize(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (ReferenceEquals(collection, this))
            return collection.ToArray();
        return collection as IReadOnlyList<T> ?? collection.ToArray();
    }
}
