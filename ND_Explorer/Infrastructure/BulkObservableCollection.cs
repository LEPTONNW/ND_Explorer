using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ND_Explorer.Infrastructure;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void Reset(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        NotifyReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        var addedAny = false;
        foreach (var item in items)
        {
            Items.Add(item);
            addedAny = true;
        }

        if (addedAny)
        {
            NotifyReset();
        }
    }

    public void UpsertRange(IEnumerable<T> items, Func<T, T, bool> matches)
    {
        var changed = false;
        foreach (var item in items)
        {
            var existingIndex = -1;
            for (var index = 0; index < Items.Count; index++)
            {
                if (matches(Items[index], item))
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Items[existingIndex] = item;
            }
            else
            {
                Items.Add(item);
            }

            changed = true;
        }

        if (changed)
        {
            NotifyReset();
        }
    }

    private void NotifyReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }
}
