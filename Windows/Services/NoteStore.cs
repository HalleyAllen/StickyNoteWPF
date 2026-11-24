using System.IO;
using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public class StoreData : StickyNoteWPF.Sync.SyncData { }

public static class NoteStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "notes.json");

    // 上次加载是否成功：失败时禁止“空数据覆盖”，并自动备份损坏文件
    private static bool _loadSucceeded;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string SavePath => FilePath;

    // 下载覆盖前先保留原始数据；写盘失败时不更换内存中的内容。
    public static void ReplaceFromSync(StoreData data)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        if (File.Exists(FilePath))
            File.Copy(FilePath, Path.Combine(dir, $"notes.before-sync.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json"));
        WriteFile(FilePath, data);
        _loadSucceeded = true;
    }

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

            data = ReadFile(FilePath);
            Logger.Log($"NoteStore.Load: 新格式，便利贴数={data.Notes.Count}，任务清单数={data.TaskLists.Count}");
            _loadSucceeded = true;
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.Load", ex);
            _loadSucceeded = false;
            BackupCorruptedFile();
        }
        return data;
    }

    // 加载失败时把原文件备份一份，防止后续保存把损坏文件覆盖后无法恢复
    private static void BackupCorruptedFile()
    {
        try
        {
            var bakPath = FilePath + ".bak";
            File.Copy(FilePath, bakPath, overwrite: true);
            Logger.Log($"NoteStore.Load: 检测到文件损坏，已备份原文件到 {bakPath}");
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.BackupCorruptedFile", ex);
        }
    }

    public static void Save(StoreData data)
    {
        try
        {
            int notes = data.Notes?.Count ?? 0;
            int lists = data.TaskLists?.Count ?? 0;

            // 防御：加载失败且内存为空时跳过保存，保留原文件，避免空数据清空用户数据
            if (!_loadSucceeded && notes == 0 && lists == 0)
            {
                Logger.Log("NoteStore.Save: 加载失败且内存为空，跳过保存，保留原文件");
                return;
            }

            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            WriteFile(FilePath, data);
            Logger.Log($"NoteStore.Save: 写入成功，便利贴数={notes}，任务清单数={lists}");
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.Save", ex);
        }
    }

    // 流式读写避免同时保留完整 UTF-16 JSON 字符串与 JSON DOM；保留临时文件原子替换。
    private static StoreData ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        int first;
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 128, true))
        {
            do { first = reader.Read(); } while (first >= 0 && char.IsWhiteSpace((char)first));
        }
        stream.Position = 0;
        if (first == '[') return new StoreData { Notes = JsonSerializer.Deserialize<List<StickyNoteModel>>(stream) ?? new() };
        var data = JsonSerializer.Deserialize<StoreData>(stream) ?? new();
        data.Notes ??= new();
        data.TaskLists ??= new();
        return data;
    }

    private static void WriteFile(string path, StoreData data)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, data, Json);
        File.Move(temp, path, true);
    }
}
