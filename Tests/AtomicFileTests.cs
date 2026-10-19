using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StickyNote-tests-" + Guid.NewGuid());

    [Fact]
    public void ReplacementRetainsPreviousCompleteDocument()
    {
        string path = Path.Combine(_directory, "notes.json");
        AtomicFile.WriteAllText(path, "{\"text\":\"旧内容\"}");
        AtomicFile.WriteAllText(path, "{\"text\":\"新内容\"}");
        Assert.Equal("{\"text\":\"新内容\"}", File.ReadAllText(path));
        Assert.Equal("{\"text\":\"旧内容\"}", File.ReadAllText(path + ".bak"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void FailedReplacementCleansTemporaryFiles()
    {
        Directory.CreateDirectory(_directory);
        string directoryAtDestination = Path.Combine(_directory, "notes.json");
        Directory.CreateDirectory(directoryAtDestination);
        Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(directoryAtDestination, "new"));
        Assert.True(Directory.Exists(directoryAtDestination));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CorruptionArchivesNeverOverwriteLastGoodBackupOrEachOther()
    {
        string path = Path.Combine(_directory, "notes.json");
        AtomicFile.WriteAllText(path, "valid");
        AtomicFile.WriteAllText(path, "corrupt");
        string first = AtomicFile.PreserveCorruptedFile(path);
        string second = AtomicFile.PreserveCorruptedFile(path);
        Assert.NotEqual(first, second);
        Assert.Equal("corrupt", File.ReadAllText(first));
        Assert.Equal("corrupt", File.ReadAllText(second));
        Assert.Equal("valid", File.ReadAllText(path + ".bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
