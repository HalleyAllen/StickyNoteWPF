using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class ModelNotificationTests
{
    [Fact]
    public void NoteChangesNotifyOnlyTheChangedPropertyAndIgnoreEqualValues()
    {
        var note = new StickyNoteModel();
        var changes = new List<string?>();
        note.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        note.Title = "title";
        note.Title = "title";
        note.Text = "body";
        note.Color = "#FFFFFF";
        Assert.Equal(new[] { "Title", "Text", "Color" }, changes);
    }

    [Fact]
    public void TaskChangesUpdateProgressWithoutInvalidatingEveryBinding()
    {
        var list = new TaskListModel { Items = new() { new TaskItem() } };
        var changes = new List<string?>();
        list.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        using var observer = new TaskTreeObserver(list.Items, list.NotifyTaskChanges);
        list.Title = "title";
        list.Title = "title";
        list.Items[0].IsDone = true;
        Assert.Equal("已完成 1/1", list.ProgressText);
        Assert.Equal(new[] { "Title", "ProgressText" }, changes);
    }
}
