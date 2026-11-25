using System.IO;

namespace StickyNoteWPF.Services;

public static class NoteStore
{
    private static readonly NoteFileStore Store = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "notes.json"), Logger.Log);

    public static string SavePath => Store.SavePath;
    public static bool LoadSucceeded => Store.LoadSucceeded;
    public static StoreData Load() => Store.Load();
    public static string Serialize(StoreData data) => StoreDataCodec.Serialize(data);
    public static bool SaveJson(string json) => Store.SaveJson(json);
    public static void ReplaceFromSync(StoreData data)
    {
        var json = Serialize(data);
        var directory = Path.GetDirectoryName(SavePath)!;
        Directory.CreateDirectory(directory);
        if (File.Exists(SavePath)) File.Copy(SavePath, Path.Combine(directory, $"notes.before-sync.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json"));
        AtomicFile.WriteAllText(SavePath, json);
        Store.Load();
    }
    private static StoreData ReadFile(string path) => StoreDataCodec.Parse(File.ReadAllText(path));
    private static void WriteFile(string path, StoreData data) => AtomicFile.WriteAllText(path, Serialize(data));
}
