using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickyNoteWPF.Services;

public sealed class SyncPreferences
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyNoteWPF", "sync.json");
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProtectedServerKey { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public long? Revision { get; set; }
    public string BaselineHash { get; set; } = "";
    public DateTimeOffset? LastSyncAt { get; set; }

    [JsonIgnore]
    public string ServerKey { get => Unprotect(ProtectedServerKey); set => ProtectedServerKey = Protect(value); }
    [JsonIgnore]
    public string Token { get => Unprotect(ProtectedToken); set => ProtectedToken = Protect(value); }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SyncPreferences Load()
    {
        try { return File.Exists(PathName) ? JsonSerializer.Deserialize<SyncPreferences>(File.ReadAllText(PathName), Json) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, PathName, true);
    }

    private static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return ""; }
    }
}
