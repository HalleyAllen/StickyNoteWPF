namespace StickyNoteWPF.Services;

/// <summary>Writes immutable snapshots serially; intermediate pending snapshots are coalesced.</summary>
public sealed class SaveQueue<T> where T : class
{
    private readonly object _gate = new();
    private readonly Func<T, bool> _write;
    private T? _pending;
    private bool _running;
    private Task _worker = Task.CompletedTask;
    private bool _lastSaveSucceeded = true;

    public SaveQueue(Func<T, bool> write) => _write = write;
    public event Action? SaveFailed;
    public bool LastSaveSucceeded { get { lock (_gate) return _lastSaveSucceeded; } }

    public void Enqueue(T snapshot)
    {
        lock (_gate)
        {
            _pending = snapshot;
            if (_running) return;
            _running = true;
            _worker = Task.Run(Process);
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _worker;
    }

    private void Process()
    {
        while (true)
        {
            T snapshot;
            lock (_gate)
            {
                if (_pending == null)
                {
                    _running = false;
                    return;
                }
                snapshot = _pending;
                _pending = null;
            }

            bool succeeded;
            try { succeeded = _write(snapshot); }
            catch { succeeded = false; }
            lock (_gate) _lastSaveSucceeded = succeeded;
            if (!succeeded)
            {
                // An error reporter must not leave the queue permanently marked as running.
                try { SaveFailed?.Invoke(); }
                catch { }
            }
        }
    }
}
