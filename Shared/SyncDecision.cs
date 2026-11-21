namespace StickyNoteWPF.Sync;

public enum SyncDirection { None, Upload, Download, Conflict }

public static class SyncDecision
{
    public static SyncDirection Choose(long? baselineRevision, string baselineHash,
        string localHash, string remoteHash, long remoteRevision, bool localEmpty)
    {
        if (localHash == remoteHash) return SyncDirection.None;
        if (baselineRevision is null || string.IsNullOrEmpty(baselineHash))
        {
            if (remoteRevision == 0) return SyncDirection.Upload;
            return localEmpty ? SyncDirection.Download : SyncDirection.Conflict;
        }
        // 远端版本回退说明服务器可能从备份恢复，必须由用户确认。
        if (remoteRevision < baselineRevision) return SyncDirection.Conflict;
        if (localHash == baselineHash) return SyncDirection.Download;
        if (remoteHash == baselineHash) return SyncDirection.Upload;
        return SyncDirection.Conflict;
    }
}
