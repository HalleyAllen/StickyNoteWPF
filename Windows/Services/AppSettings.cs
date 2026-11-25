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
    public bool DarkMode { get; set; }

    // 全局快捷键：仅隐藏/显示便签与清单，如 "F1" 或 "Ctrl+Alt+H"；空字符串表示未设置
    public string ToggleWindowsHotKey { get; set; } = string.Empty;
    public bool ForceShowAll { get; set; } = false;   // 全局强制所有便签不透明且始终显示（停用隐藏/透明效果）

    // ===== 新建便利贴的默认外观（仅影响之后新建的便利贴）=====
    public const string NoteDefaultColorHex = "#FF4B5058";
    public const string NoteDefaultTextColorHex = "#FFFFFFFF";
    public const double NoteDefaultOpacity = 0.85;
    public int DefaultAppearanceVersion { get; set; }
    public string DefaultColor { get; set; } = NoteDefaultColorHex;
    public double DefaultFontSize { get; set; } = 14;   // 初始默认字体大小（仅影响新建便利贴）
    public double WindowOpacity { get; set; } = NoteDefaultOpacity;
    public string NoteTextColor { get; set; } = NoteDefaultTextColorHex;
    public string TitleTextColor { get; set; } = NoteDefaultTextColorHex;
    public string ButtonColor { get; set; } = NoteDefaultTextColorHex;

    // ===== 新建任务清单的默认外观 =====
    // 与便利贴默认完全独立、互不影响。字段可空仅为兼容旧版 settings.json：
    // 旧版未单独设置时存 null（表示“跟随便利贴默认”），Load() 时会一次性固化为当时的
    // 便利贴默认值，之后便利贴默认的修改不会再影响新建任务清单。
    public const string TaskListDefaultColorHex = NoteDefaultColorHex;
    public const string TaskListDefaultTextColorHex = NoteDefaultTextColorHex;
    public const double TaskListDefaultOpacity = NoteDefaultOpacity;
    public const double TaskListDefaultFontSize = 14;

    public string? TaskListColor { get; set; }
    public double? TaskListFontSize { get; set; }
    public double? TaskListOpacity { get; set; }
    public string? TaskListTextColor { get; set; }
    // 新建任务清单默认是否显示已勾选完成的任务（仅影响之后新建的清单；
    // 旧版 settings.json 无此字段时由属性初始化器保持 true，无需额外迁移）
    public bool TaskListShowCompleted { get; set; } = true;

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
                if (s != null) return EnsureTaskListDefaults(s);
            }
        }
        catch { }
        return EnsureTaskListDefaults(new AppSettings());
    }

    // 一次性迁移旧数据：TaskList* 为 null（旧版“跟随便利贴默认”）时，固化为当时的便利贴
    // 默认值。此后任务清单默认完全独立，便利贴默认的修改不再影响新建任务清单。
    private static AppSettings EnsureTaskListDefaults(AppSettings s)
    {
        s.TaskListColor ??= s.DefaultColor;
        s.TaskListTextColor ??= s.NoteTextColor;
        s.TaskListOpacity ??= s.WindowOpacity;
        s.TaskListFontSize ??= s.DefaultFontSize;
        // 仅升级仍使用旧出厂配色的新建默认值；已创建的模型与自定义默认值保持原样。
        if (s.DefaultAppearanceVersion < 1)
        {
            if (IsOldDefault(s.DefaultColor, s.NoteTextColor, s.WindowOpacity))
            {
                s.DefaultColor = NoteDefaultColorHex;
                s.NoteTextColor = NoteDefaultTextColorHex;
                if (s.TitleTextColor is "#FF333333" or "#FF222222") s.TitleTextColor = NoteDefaultTextColorHex;
                if (s.ButtonColor is "#FF333333" or "#FF222222") s.ButtonColor = NoteDefaultTextColorHex;
                s.WindowOpacity = NoteDefaultOpacity;
            }
            if (IsOldDefault(s.TaskListColor, s.TaskListTextColor, s.TaskListOpacity.Value))
            {
                s.TaskListColor = TaskListDefaultColorHex;
                s.TaskListTextColor = TaskListDefaultTextColorHex;
                s.TaskListOpacity = TaskListDefaultOpacity;
            }
            s.DefaultAppearanceVersion = 1;
        }
        return s;
    }

    private static bool IsOldDefault(string color, string text, double opacity)
        => string.Equals(color, "#FFF7A900", StringComparison.OrdinalIgnoreCase)
           && string.Equals(text, "#FF222222", StringComparison.OrdinalIgnoreCase) && opacity == 1;

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
                // 单文件发布下 Assembly.Location 为空字符串，需用 ProcessPath 获取 exe 路径
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
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
