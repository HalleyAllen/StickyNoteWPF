using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class SaveQueueTests
{
    [Fact]
    public async Task SlowWritesAreSerializedAndLatestPendingSnapshotWins()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writes = new List<string>();
        var queue = new SaveQueue<string>(text =>
        {
            if (text == "first")
            {
                started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            writes.Add(text);
            return true;
        });
        queue.Enqueue("first");
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            queue.Enqueue("intermediate");
            queue.Enqueue("latest");
        }
        finally { release.Set(); }
        await queue.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "first", "latest" }, writes);
        Assert.True(queue.LastSaveSucceeded);
    }

    [Fact]
    public async Task FailureIsReportedAndSubsequentWritesCanRecover()
    {
        var queue = new SaveQueue<string>(text => text == "ok" ? true : throw new IOException());
        int failures = 0;
        queue.SaveFailed += () => failures++;
        queue.Enqueue("fail");
        await queue.FlushAsync();
        Assert.False(queue.LastSaveSucceeded);
        Assert.Equal(1, failures);
        queue.Enqueue("ok");
        await queue.FlushAsync();
        Assert.True(queue.LastSaveSucceeded);
    }
    [Fact]
    public async Task FailedSnapshotIsRetainedForExplicitRetry()
    {
        bool available = false;
        var attempts = new List<string>();
        var queue = new SaveQueue<string>(text => { attempts.Add(text); return available; });
        queue.Enqueue("unsaved content");
        await queue.FlushAsync();
        Assert.False(queue.LastSaveSucceeded);
        Assert.Single(attempts); // A disk failure must not start an automatic retry loop.
        available = true;
        queue.RetryFailed();
        await queue.FlushAsync();
        Assert.True(queue.LastSaveSucceeded);
        Assert.Equal(new[] { "unsaved content", "unsaved content" }, attempts);
        queue.RetryFailed();
        await queue.FlushAsync();
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task NewerSnapshotSupersedesRetainedFailureEvenWhenReporterThrows()
    {
        var attempts = new List<string>();
        var queue = new SaveQueue<string>(text => { attempts.Add(text); return text == "new"; });
        queue.SaveFailed += () => throw new InvalidOperationException("reporter failed");
        queue.Enqueue("old");
        await queue.FlushAsync();
        queue.Enqueue("new");
        await queue.FlushAsync();
        queue.RetryFailed();
        await queue.FlushAsync();
        Assert.True(queue.LastSaveSucceeded);
        Assert.Equal(new[] { "old", "new" }, attempts);
    }

}
