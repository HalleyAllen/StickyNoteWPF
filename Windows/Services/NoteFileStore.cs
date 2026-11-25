using System.IO;

namespace StickyNoteWPF.Services;

/// <summary>Loads and atomically saves one store, preserving all recovery files on failure.</summary>
public sealed class NoteFileStore(string savePath, Action<string>? log = null)
{
    public string SavePath { get; } = savePath;
    public bool LoadSucceeded { get; private set; }

    public StoreData Load()
    {
        LoadSucceeded = false;
        try
        {
            if (File.Exists(SavePath))
            {
                var data = StoreDataCodec.Parse(File.ReadAllText(SavePath));
                LoadSucceeded = true;
                return data;
            }
            if (File.Exists(SavePath + ".bak"))
                return RecoverBackup();

            // Only a store with neither a primary nor a backup is genuinely new.
            LoadSucceeded = true;
            return new();
        }
        catch (Exception ex)
        {
            log?.Invoke($"NoteStore.Load: {ex}");
            // Do not replace an unreadable/corrupt primary until its original bytes are safe.
            if (File.Exists(SavePath))
            {
                try
                {
                    var archive = AtomicFile.PreserveCorruptedFile(SavePath);
                    log?.Invoke($"NoteStore.Load: 原文件已保留到 {archive}");
                }
                catch (Exception archiveError)
                {
                    log?.Invoke($"NoteStore.PreserveCorruptedFile: {archiveError}");
                    return new();
                }
                if (File.Exists(SavePath + ".bak"))
                {
                    try { return RecoverBackup(); }
                    catch (Exception recoveryError) { log?.Invoke($"NoteStore.RecoverBackup: {recoveryError}"); }
                }
            }
            return new();
        }
    }

    private StoreData RecoverBackup()
    {
        var data = StoreDataCodec.Parse(File.ReadAllText(SavePath + ".bak"));
        // Restore immediately, without rotating a bad primary into the good backup.
        AtomicFile.WriteAllText(SavePath, StoreDataCodec.Serialize(data), backupExisting: false);
        LoadSucceeded = true;
        log?.Invoke("NoteStore.Load: 已从最近有效备份恢复");
        return data;
    }

    public bool SaveJson(string json)
    {
        if (!LoadSucceeded)
        {
            log?.Invoke("NoteStore.Save: 加载未成功，拒绝覆盖原文件，请先恢复备份");
            return false;
        }
        try
        {
            AtomicFile.WriteAllText(SavePath, json);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"NoteStore.Save: {ex}");
            return false;
        }
    }
}
