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

    public static StoreData Load()
    {
        var data = new StoreData();
        try
        {
            if (!File.Exists(FilePath))
                return data;

            var json = File.ReadAllText(FilePath);

            // 兼容旧格式：直接是便利贴数组
            var legacy = JsonSerializer.Deserialize<List<StickyNoteModel>>(json);
            if (legacy is not null)
            {
                data.Notes = legacy;
                return data;
            }

            var parsed = JsonSerializer.Deserialize<StoreData>(json);
            if (parsed is not null)
            {
                data.Notes = parsed.Notes ?? new List<StickyNoteModel>();
                data.TaskLists = parsed.TaskLists ?? new List<TaskListModel>();
            }
        }
        catch
        {
            // 解析失败时返回空数据
        }
        return data;
    }

    public static void Save(StoreData data)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 持久化失败时不阻塞 UI
        }
    }
}
