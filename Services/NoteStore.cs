using System.IO;
using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public static class NoteStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "notes.json");

    public static List<StickyNoteModel> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<StickyNoteModel>();

            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<StickyNoteModel>>(json);
            return list ?? new List<StickyNoteModel>();
        }
        catch
        {
            return new List<StickyNoteWPF.Models.StickyNoteModel>();
        }
    }

    public static void Save(IEnumerable<StickyNoteModel> notes)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(notes,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 持久化失败时不阻塞 UI
        }
    }
}
