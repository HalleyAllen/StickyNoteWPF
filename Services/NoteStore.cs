using System.IO;
using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public static class NoteStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "notes.json");

    // 上次加载是否成功：失败时禁止“空数据覆盖”，并自动备份损坏文件
    private static bool _loadSucceeded;

    public static string SavePath => FilePath;
    public static bool LoadSucceeded => _loadSucceeded;

    public static StoreData Load()
    {
        var data = new StoreData();
        try
        {
            if (!File.Exists(FilePath))
            {
                Logger.Log("NoteStore.Load: 文件不存在，返回空数据");
                _loadSucceeded = true; // 没有旧数据，允许保存
                return data;
            }

            var json = File.ReadAllText(FilePath);
            Logger.Log($"NoteStore.Load: 读取文件成功，长度={json.Length}");

            data = StoreDataCodec.Parse(json);
            Logger.Log($"NoteStore.Load: 新格式，便利贴数={data.Notes.Count}，任务清单数={data.TaskLists.Count}");
            _loadSucceeded = true;
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.Load", ex);
            _loadSucceeded = false;
            if (BackupCorruptedFile() && File.Exists(FilePath + ".bak"))
            {
                try
                {
                    var backup = File.ReadAllText(FilePath + ".bak");
                    data = StoreDataCodec.Parse(backup);
                    // Restore immediately without rotating the corrupt primary into the good backup.
                    AtomicFile.WriteAllText(FilePath, StoreDataCodec.Serialize(data), backupExisting: false);
                    _loadSucceeded = true;
                    Logger.Log("NoteStore.Load: 已从最近有效备份恢复");
                }
                catch (Exception recoveryError)
                {
                    Logger.LogException("NoteStore.RecoverBackup", recoveryError);
                    data = new();
                }
            }
        }
        return data;
    }

    // 加载失败时把原文件备份一份，防止后续保存把损坏文件覆盖后无法恢复
    private static bool BackupCorruptedFile()
    {
        try
        {
            var bakPath = AtomicFile.PreserveCorruptedFile(FilePath);
            Logger.Log($"NoteStore.Load: 检测到文件损坏，已备份原文件到 {bakPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.BackupCorruptedFile", ex);
            return false;
        }
    }

    public static string Serialize(StoreData data) => StoreDataCodec.Serialize(data);

    public static bool SaveJson(string json)
    {
        try
        {
            // 加载失败且未能恢复时禁止覆盖原文件，包括之后新建的数据。
            if (!_loadSucceeded)
            {
                Logger.Log("NoteStore.Save: 加载未成功，拒绝覆盖原文件，请先恢复备份");
                return false;
            }

            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            AtomicFile.WriteAllText(FilePath, json);
            Logger.Log($"NoteStore.Save: 写入成功，长度={json.Length}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.Save", ex);
            return false;
        }
    }
}
