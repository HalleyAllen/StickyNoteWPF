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
}
