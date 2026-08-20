using System.IO;
using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public class StoreData
{
    public List<StickyNoteModel> Notes { get; set; } = new();
    public List<TaskListModel> TaskLists { get; set; } = new();
}

public static class NoteStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "notes.json");

    // 上次加载是否成功：失败时禁止“空数据覆盖”，并自动备份损坏文件
    private static bool _loadSucceeded;

    public static string SavePath => FilePath;

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

            // 先探测根节点类型：数组=旧格式（直接是便利贴数组），对象=新格式（StoreData）
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var legacy = JsonSerializer.Deserialize<List<StickyNoteModel>>(json);
                    data.Notes = legacy ?? new List<StickyNoteModel>();
                    Logger.Log($"NoteStore.Load: 旧格式，便利贴数={data.Notes.Count}");
                    _loadSucceeded = true;
                    return data;
                }
            }

            var parsed = JsonSerializer.Deserialize<StoreData>(json);
            if (parsed is not null)
            {
                data.Notes = parsed.Notes ?? new List<StickyNoteModel>();
                data.TaskLists = parsed.TaskLists ?? new List<TaskListModel>();
            }
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

            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            Logger.Log($"NoteStore.Save: 写入成功，便利贴数={notes}，任务清单数={lists}，长度={json.Length}");
        }
        catch (Exception ex)
        {
            Logger.LogException("NoteStore.Save", ex);
        }
    }
}
