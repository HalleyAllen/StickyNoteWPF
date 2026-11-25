using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class RollingLogWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StickyNote-log-tests-" + Guid.NewGuid());

    [Fact]
    public void RotationKeepsNewestMessagesWithBoundedFiles()
    {
        string path = Path.Combine(_directory, "logs", "app.log");
        var writer = new RollingLogWriter(path, maxBytes: 128, backups: 2);
        for (int i = 0; i < 30; i++) writer.Append($"message-{i:D2}: " + new string('a', 35) + "\n");
        Assert.Equal(3, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
        Assert.Contains("message-29", File.ReadAllText(path));
        Assert.All(Directory.GetFiles(Path.GetDirectoryName(path)!), file => Assert.True(new FileInfo(file).Length <= 128));
    }

    [Fact]
    public void ConcurrentMessagesAreNotLostOrInterleaved()
    {
        string path = Path.Combine(_directory, "app.log");
        var writer = new RollingLogWriter(path);
        Parallel.For(0, 100, i => writer.Append($"message-{i:D3}\n"));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(100, lines.Length);
        Assert.Equal(100, lines.Distinct().Count());
        Assert.All(lines, line => Assert.Matches("^message-[0-9]{3}$", line));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
