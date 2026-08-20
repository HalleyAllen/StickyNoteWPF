using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace StickyNoteWPF.Services;

public class AppSettings
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNoteWPF", "settings.json");

    public bool StartupWithWindows { get; set; }
    public bool GlobalTopmost { get; set; } = true;

    // 全局快捷键：隐藏/显示所有窗口（除设置窗口外），如 "Ctrl+Alt+H"；空字符串表示未设置
    public string ToggleWindowsHotKey { get; set; } = string.Empty;
    public bool ForceShowAll { get; set; } = false;   // 全局强制所有便签不透明且始终显示（停用隐藏/透明效果）
    public string DefaultColor { get; set; } = "#FFF7A900";
    public double DefaultFontSize { get; set; } = 14;   // 初始默认字体大小（仅影响新建便签）
    public double WindowOpacity { get; set; } = 1.0;
    public string NoteTextColor { get; set; } = "#FF222222";   // 便签编辑区文字
    public string TitleTextColor { get; set; } = "#FF333333";  // 标题栏文字
    public string ButtonColor { get; set; } = "#FF333333";     // 标题栏按钮(🎨/✕)

    // 最近使用颜色（MRU，最多 8 个，第一位为最近选中）；初始为预置色板
    public List<string> RecentColors { get; set; } = new(AppearanceHelper.DefaultColors);

    // 设置窗口尺寸记忆
    public double SettingsWindowWidth { get; set; } = 380;
    public double SettingsWindowHeight { get; set; } = 470;
    public double NoteSettingsWidth { get; set; } = 360;
    public double NoteSettingsHeight { get; set; } = 470;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void SetStartupWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
            {
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue("StickyNoteWPF", exe);
            }
            else
            {
                if (key.GetValue("StickyNoteWPF") != null)
                    key.DeleteValue("StickyNoteWPF");
            }
        }
        catch { }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("StickyNoteWPF") != null;
        }
        catch { return false; }
    }
}
