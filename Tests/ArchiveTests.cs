using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using StickyNoteWPF.Sync;
using Xunit;

namespace StickyNoteWPF.Tests;

public class ArchiveTests
{
    [Fact]
    public void ArchivedDataSurvivesStorageAndSyncWithoutChangingTaskPreferences()
    {
        var completed = new TaskItem { Text = "finished", IsDone = true };
        completed.SubItems.Add(new TaskItem { Text = "finished child", IsDone = true });
        var note = new StickyNoteModel { IsArchived = true, IsOpen = true, Text = "keep this", Opacity = 0.35 };
        var list = new TaskListModel { IsArchived = true, IsOpen = true, ShowCompleted = false, Items = { completed } };
        var stored = StoreDataCodec.Parse(StoreDataCodec.Serialize(new StoreData { Notes = [note], TaskLists = [list] }));
        Assert.True(stored.Notes[0].IsArchived);
        Assert.True(stored.TaskLists[0].IsArchived);
        Assert.False(stored.Notes[0].IsOpen);
        Assert.False(stored.TaskLists[0].IsOpen);
        Assert.Equal("keep this", stored.Notes[0].Text);
        Assert.Equal(0.35, stored.Notes[0].Opacity);
        Assert.False(stored.TaskLists[0].ShowCompleted);
        var sync = SnapshotTools.ForSync(stored);
        Assert.Null(SnapshotTools.Validate(sync));
        Assert.True(sync.Notes[0].IsArchived);
        Assert.True(sync.TaskLists[0].IsArchived);
        Assert.Equal(completed.Id, sync.TaskLists[0].Items[0].Id);
        Assert.True(sync.TaskLists[0].Items[0].SubItems[0].IsDone);
        var archivedHash = SnapshotTools.Hash(sync);
        sync.TaskLists[0].IsArchived = false;
        Assert.NotEqual(archivedHash, SnapshotTools.Hash(sync));
    }

    [Fact]
    public void OldDataDefaultsToActiveAndArchiveChangesNotifyOnlyOnce()
    {
        var data = StoreDataCodec.Parse("""{"Notes":[{"Text":"legacy"}],"TaskLists":[{"Items":[]}]}""");
        Assert.False(data.Notes[0].IsArchived);
        Assert.False(data.TaskLists[0].IsArchived);
        var changes = new List<string?>();
        data.Notes[0].PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        data.TaskLists[0].PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        data.Notes[0].IsArchived = true;
        data.Notes[0].IsArchived = true;
        data.TaskLists[0].IsArchived = true;
        data.TaskLists[0].IsArchived = true;
        Assert.Equal(new[] { "IsArchived", "IsArchived" }, changes);
    }
}
