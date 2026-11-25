using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class NoteFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StickyNote-store-" + Guid.NewGuid());
    private string SavePath => Path.Combine(_directory, "notes.json");
    private const string GoodBackup = "{\"Notes\":[{\"Text\":\"保留的便签\"}],\"TaskLists\":[{\"Title\":\"保留的清单\",\"Items\":null}]}";

    [Fact]
    public void MissingPrimaryRestoresBackupBeforeAllowingSaves()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SavePath + ".bak", GoodBackup);
        var store = new NoteFileStore(SavePath);
        var data = store.Load();
        Assert.True(store.LoadSucceeded);
        Assert.Equal("保留的便签", Assert.Single(data.Notes).Text);
        Assert.Empty(Assert.Single(data.TaskLists).Items);
        Assert.Equal(GoodBackup, File.ReadAllText(SavePath + ".bak"));
        Assert.Equal("保留的便签", Assert.Single(StoreDataCodec.Parse(File.ReadAllText(SavePath)).Notes).Text);
        Assert.True(store.SaveJson(StoreDataCodec.Serialize(data)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidBackupBlocksSavesAndPreservesOriginalFiles(bool primaryExists)
    {
        Directory.CreateDirectory(_directory);
        if (primaryExists) File.WriteAllText(SavePath, "broken primary");
        File.WriteAllText(SavePath + ".bak", "broken backup");
        var store = new NoteFileStore(SavePath);
        Assert.Empty(store.Load().Notes);
        Assert.False(store.LoadSucceeded);
        Assert.False(store.SaveJson("{\"Notes\":[]}"));
        Assert.Equal("broken backup", File.ReadAllText(SavePath + ".bak"));
        Assert.Equal(primaryExists, File.Exists(SavePath));
        if (primaryExists)
        {
            Assert.Equal("broken primary", File.ReadAllText(SavePath));
            Assert.Equal("broken primary", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*.json"))));
        }
    }

    [Fact]
    public void CorruptPrimaryIsArchivedBeforeRecoveryAndGoodBackupIsUnchanged()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SavePath, "corrupt primary");
        File.WriteAllText(SavePath + ".bak", GoodBackup);
        var store = new NoteFileStore(SavePath);
        var data = store.Load();
        Assert.True(store.LoadSucceeded);
        Assert.Equal("保留的便签", Assert.Single(data.Notes).Text);
        Assert.Equal(GoodBackup, File.ReadAllText(SavePath + ".bak"));
        Assert.Equal("corrupt primary", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*.json"))));
    }

    [Fact]
    public void NewStoreAndLegacyBackupLoadSuccessfully()
    {
        var store = new NoteFileStore(SavePath);
        Assert.Empty(store.Load().Notes);
        Assert.True(store.LoadSucceeded);
        Assert.True(store.SaveJson("[{\"Text\":\"旧版内容\"}]"));
        File.Move(SavePath, SavePath + ".bak");
        Assert.Equal("旧版内容", Assert.Single(store.Load().Notes).Text);
        Assert.True(store.LoadSucceeded);
        Assert.True(File.Exists(SavePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
