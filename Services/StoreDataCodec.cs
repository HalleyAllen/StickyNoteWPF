using System.Collections.ObjectModel;
using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public static class StoreDataCodec
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static StoreData Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        StoreData data;
        if (root.ValueKind == JsonValueKind.Array)
            data = new() { Notes = JsonSerializer.Deserialize<List<StickyNoteModel>>(json)! };
        else if (root.ValueKind == JsonValueKind.Object &&
            (root.TryGetProperty(nameof(StoreData.Notes), out _) || root.TryGetProperty(nameof(StoreData.TaskLists), out _)))
            data = JsonSerializer.Deserialize<StoreData>(json)!;
        else
            throw new JsonException("无法识别便签数据格式");

        Normalize(data);
        return data;
    }

    public static string Serialize(StoreData data) => JsonSerializer.Serialize(data, Options);

    private static void Normalize(StoreData data)
    {
        data.Notes ??= new();
        data.TaskLists ??= new();
        data.Notes.RemoveAll(note => note is null);
        data.TaskLists.RemoveAll(list => list is null);
        var ids = new HashSet<Guid>();
        foreach (var note in data.Notes)
        {
            note.Id = UniqueId(note.Id, ids);
            note.Title ??= string.Empty;
            note.Text ??= string.Empty;
            note.Color = Nonempty(note.Color, "#FFF7A900");
            note.TextColor = Nonempty(note.TextColor, "#FF222222");
            note.BackgroundImagePath ??= string.Empty;
            note.BackgroundImageMode = Nonempty(note.BackgroundImageMode, "Stretch");
            note.FontSize = Positive(note.FontSize, 14, 512);
            note.Opacity = Opacity(note.Opacity);
            note.Left = Coordinate(note.Left, 200);
            note.Top = Coordinate(note.Top, 200);
            note.Width = Dimension(note.Width, 220, 160);
            note.Height = Dimension(note.Height, 220, 120);
        }
        foreach (var list in data.TaskLists)
        {
            list.Id = UniqueId(list.Id, ids);
            list.Title ??= string.Empty;
            list.Color = Nonempty(list.Color, "#FFF7A900");
            list.TextColor = Nonempty(list.TextColor, "#FF222222");
            list.BackgroundImageMode = Nonempty(list.BackgroundImageMode, "Fill");
            list.FontFamily = Nonempty(list.FontFamily, "Microsoft YaHei");
            list.FontSize = Positive(list.FontSize, 14, 512);
            list.Opacity = Opacity(list.Opacity);
            list.Left = Coordinate(list.Left, 200);
            list.Top = Coordinate(list.Top, 150);
            list.Width = Dimension(list.Width, 320, 160);
            list.Height = Dimension(list.Height, 360, 120);
            list.Items ??= new();
            NormalizeTasks(list.Items, ids);
        }
    }

    private static void NormalizeTasks(ObservableCollection<TaskItem> items, HashSet<Guid> ids)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i] is null) items.RemoveAt(i);
        foreach (var item in items)
        {
            item.Id = UniqueId(item.Id, ids);
            item.Text ??= string.Empty;
            item.SubItems ??= new();
            NormalizeTasks(item.SubItems, ids);
        }
    }

    private static Guid UniqueId(Guid id, HashSet<Guid> ids)
    {
        if (id != Guid.Empty && ids.Add(id)) return id;
        do { id = Guid.NewGuid(); } while (!ids.Add(id));
        return id;
    }

    private static string Nonempty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static double Positive(double value, double fallback, double maximum)
        => double.IsFinite(value) && value > 0 ? Math.Min(value, maximum) : fallback;
    private static double Opacity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    private static double Coordinate(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    private static double Dimension(double value, double fallback, double minimum)
        => double.IsFinite(value) && value >= minimum ? Math.Min(value, 32768) : fallback;
}
