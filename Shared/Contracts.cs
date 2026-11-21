using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Sync;

public class SyncData
{
    public List<StickyNoteModel> Notes { get; set; } = new();
    public List<TaskListModel> TaskLists { get; set; } = new();
}

public record Credentials(string Username, string Password);
public record LoginResult(string UserId, string Username, string Token, DateTimeOffset ExpiresAt);
public record RemoteSnapshot(long Revision, SyncData Data);
public record UploadSnapshot(long ExpectedRevision, SyncData Data);
public record ApiError(string Message);

public static class SnapshotTools
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        MaxDepth = 96
    };

    // 设备位置、窗口开关、本地图片路径不参与跨设备同步，也不触发内容冲突。
    public static SyncData ForSync(SyncData source)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<SyncData>(
            System.Text.Json.JsonSerializer.Serialize(source, Json), Json)!;
        foreach (var note in copy.Notes)
        {
            note.Left = 200; note.Top = 200; note.Width = 220; note.Height = 220;
            note.IsOpen = false;
            note.BackgroundImagePath = string.Empty;
        }
        foreach (var list in copy.TaskLists)
        {
            list.Left = 200; list.Top = 150;
            list.IsOpen = false; list.ForceShow = false;
            list.BackgroundImagePath = null;
        }
        return copy;
    }

    public static string Hash(SyncData data)
    {
        var canonical = ForSync(data);
        // 不同设备时区不同，同一个创建时间仍应得到相同的内容摘要。
        foreach (var note in canonical.Notes) note.CreatedAt = note.CreatedAt.ToUniversalTime();
        return Convert.ToHexString(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(canonical, Json)));
    }

    public static bool IsEmpty(SyncData data) => data.Notes.Count == 0 && data.TaskLists.Count == 0;

    public static string? Validate(SyncData? data)
    {
        if (data?.Notes is null || data.TaskLists is null) return "数据格式不正确。";
        if (data.Notes.Count + data.TaskLists.Count > 2000) return "便签和清单总数不能超过 2000。";
        var ids = new HashSet<Guid>();
        bool ValidId(Guid id) => id != Guid.Empty && ids.Add(id);
        bool Text(string? value, int max) => value is not null && value.Length <= max;
        bool Color(string? value) => value is { Length: 7 or 9 } && value[0] == '#'
            && value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false;
        bool Style(string? color, string? textColor, double opacity, double fontSize) =>
            Color(color) && Color(textColor) && double.IsFinite(opacity) && opacity is >= 0 and <= 1
            && double.IsFinite(fontSize) && fontSize is >= 1 and <= 300;
        int tasks = 0;
        bool Tasks(IEnumerable<TaskItem>? items, int depth)
        {
            if (items is null || depth > 16) return false;
            foreach (var item in items)
                if (item is null || ++tasks > 10000 || !ValidId(item.Id) || !Text(item.Text, 100000)
                    || !Tasks(item.SubItems, depth + 1)) return false;
            return true;
        }
        foreach (var n in data.Notes)
            if (n is null || !ValidId(n.Id) || !Text(n.Title, 1000) || !Text(n.Text, 500000)
                || !Style(n.Color, n.TextColor, n.Opacity, n.FontSize)
                || !string.IsNullOrEmpty(n.BackgroundImagePath)) return "便利贴数据无效。";
        foreach (var l in data.TaskLists)
            if (l is null || !ValidId(l.Id) || !Text(l.Title, 1000) || !Text(l.FontFamily, 200)
                || !Style(l.Color, l.TextColor, l.Opacity, l.FontSize)
                || !string.IsNullOrEmpty(l.BackgroundImagePath) || !Tasks(l.Items, 0)) return "任务清单数据无效或层级过深。";
        return null;
    }
}
