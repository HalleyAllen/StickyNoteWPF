using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

/// <summary>Tracks persistent changes at every task depth, releasing removed items.</summary>
public sealed class TaskTreeObserver : IDisposable
{
    private readonly ObservableCollection<TaskItem> _root;
    private readonly Action _changed;
    private readonly HashSet<TaskItem> _items = new();
    private readonly HashSet<ObservableCollection<TaskItem>> _collections = new();
    private bool _disposed;

    public TaskTreeObserver(ObservableCollection<TaskItem> root, Action changed)
    {
        _root = root;
        _changed = changed;
        Attach(root);
    }

    private void Attach(ObservableCollection<TaskItem> collection)
    {
        if (!_collections.Add(collection)) return;
        collection.CollectionChanged += CollectionChanged;
        foreach (var item in collection)
        {
            if (!_items.Add(item)) continue;
            item.PropertyChanged += ItemChanged;
            Attach(item.SubItems);
        }
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(TaskItem.Text)
            or nameof(TaskItem.IsDone) or nameof(TaskItem.Order))
            _changed();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Detach();
        Attach(_root);
        _changed();
    }

    private void Detach()
    {
        foreach (var item in _items) item.PropertyChanged -= ItemChanged;
        foreach (var collection in _collections) collection.CollectionChanged -= CollectionChanged;
        _items.Clear();
        _collections.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }
}
