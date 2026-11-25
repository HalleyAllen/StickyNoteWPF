using System.Collections.ObjectModel;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class TaskTreeObserverTests
{
    [Fact]
    public void ObservesTextAtEveryDepthIncludingNewChildren()
    {
        var root = new TaskItem();
        var child = new TaskItem();
        root.SubItems.Add(child);
        int saves = 0;
        using var observer = new TaskTreeObserver(new() { root }, () => saves++);
        root.Text = "root edit";
        child.Text = "child edit";
        var grandchild = new TaskItem();
        child.SubItems.Add(grandchild);
        grandchild.Text = "nested edit";
        Assert.Equal(4, saves);
    }

    [Fact]
    public void RemovedAndClearedTasksNoLongerTriggerSaves()
    {
        var child = new TaskItem();
        var root = new TaskItem { SubItems = new() { child } };
        var items = new ObservableCollection<TaskItem> { root };
        int saves = 0;
        using var observer = new TaskTreeObserver(items, () => saves++);
        root.SubItems.Remove(child);
        child.Text = "removed";
        items.Clear();
        root.Text = "cleared";
        Assert.Equal(2, saves);
    }

    [Fact]
    public void ExpansionDoesNotSaveAndDisposalUnsubscribes()
    {
        var task = new TaskItem();
        var items = new ObservableCollection<TaskItem> { task };
        int saves = 0;
        var observer = new TaskTreeObserver(items, () => saves++);
        task.IsExpanded = false;
        task.RefreshHasSubItems();
        Assert.Equal(0, saves);
        observer.Dispose();
        observer.Dispose();
        task.Text = "closed";
        items.Add(new());
        Assert.Equal(0, saves);
    }
}
