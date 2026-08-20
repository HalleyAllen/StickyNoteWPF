using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class App : System.Windows.Application
{
    // 单实例互斥锁：保证同一时间只运行一个程序
    private static readonly string MutexName = "StickyNoteWPF_SingleInstance";
    private static Mutex? _singleMutex;
    // 用于激活已运行实例的自定义窗口消息（internal 供 MainWindow 读取）
    internal static readonly int ActivateMessage = NativeMethods.RegisterWindowMessage("StickyNoteWPF_Activate");

    private List<StickyNoteModel> _notes = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _openWindows = new();
    private MainWindow? _manager;
    private TrayIconService? _tray;
    public AppSettings Settings { get; private set; } = new();

    public IReadOnlyList<StickyNoteModel> Notes => _notes;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例检测：若已有实例在运行，则激活它并退出当前实例
        _singleMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 向已运行实例广播“激活”消息
            NativeMethods.PostMessage(
                (IntPtr)NativeMethods.HWND_BROADCAST,
                ActivateMessage,
                IntPtr.Zero,
                IntPtr.Zero);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Settings = AppSettings.Load();
        _notes = NoteStore.Load();
        _tray = new TrayIconService(this);

        // 启动时打开之前存在的便利贴（每个便签使用自身持久化的样式）
        foreach (var note in _notes)
            OpenNote(note);

        ApplyTopmost(Settings.GlobalTopmost);

        // 管理器窗口关闭时不退出应用（托盘常驻）
        ShowManager();
        if (_manager != null)
            _manager.Closing += (_, args) =>
            {
                args.Cancel = true;
                _manager.Hide();
            };
    }

    public void CreateNote()
    {
        var note = new StickyNoteModel();
        note.Color = Settings.DefaultColor;
        note.TextColor = Settings.NoteTextColor;
        note.Opacity = Settings.WindowOpacity;
        note.FontSize = Settings.DefaultFontSize;
        // 新便利贴稍微错位，避免完全重叠
        note.Left = 200 + (_notes.Count % 5) * 30;
        note.Top = 150 + (_notes.Count % 5) * 30;
        _notes.Add(note);
        SaveAll();
        OpenNote(note);
        _manager?.RefreshList();
        _tray?.ShowBalloon("已新建便利贴", "在桌面上编辑你的内容吧。");
    }

    public void ApplyTopmost(bool topmost)
    {
        foreach (var win in _openWindows.Values)
            win.Topmost = topmost;
    }

    // 主设置只作为新建便签的默认值，已创建的便签保留各自样式（见 CreateNote / StickyNoteWindow）

    public void OpenSettings()
    {
        var w = new SettingsWindow(Settings);
        if (_manager != null)
            w.Owner = _manager;
        w.ShowDialog();
    }

    public void OpenNoteSettings(StickyNoteWindow noteWindow)
    {
        var w = new StickyNoteSettingsWindow(noteWindow);
        w.Owner = noteWindow;

        // 计算位置：默认放在便利贴右侧外部，避免遮挡；超出屏幕则放左侧
        const double gap = 12;
        var screen = System.Windows.SystemParameters.WorkArea;
        double left = noteWindow.Left + noteWindow.Width + gap;
        double top = noteWindow.Top;

        if (left + w.Width > screen.Right)
        {
            // 右侧放不下，改放便利贴左侧
            left = noteWindow.Left - w.Width - gap;
            if (left < screen.Left)
                left = screen.Left + gap; // 实在放不下就贴屏幕左边
        }

        // 垂直方向限制在屏幕工作区内
        if (top < screen.Top) top = screen.Top + gap;
        if (top + w.Height > screen.Bottom)
            top = System.Math.Max(screen.Top + gap, screen.Bottom - w.Height - gap);

        w.Left = left;
        w.Top = top;

        w.ShowDialog();
    }

    public void OpenNote(StickyNoteModel note)
    {
        if (_openWindows.TryGetValue(note.Id, out var existing))
        {
            existing.Activate();
            return;
        }

        var win = new StickyNoteWindow(note);
        // 便签窗口关闭（点 ✕）仅隐藏，不删除数据
        win.ClosedByUser += (_, _) => HideNote(note);
        win.Closed += (_, _) => _openWindows.Remove(note.Id);
        _openWindows[note.Id] = win;
        win.Show();
        win.ApplyForceShowAll();
    }

    // 全局“全部显示”开关切换后，实时刷新所有已打开的便签
    public void ApplyForceShowAll()
    {
        foreach (var win in _openWindows.Values)
            win.ApplyForceShowAll();
    }

    // 外观/标题等修改后，刷新已打开的便签窗口显示；refreshManager=true 时同步刷新管理界面列表
    public void RefreshNote(StickyNoteModel note, bool refreshManager = false)
    {
        if (_openWindows.TryGetValue(note.Id, out var win) && win.IsLoaded)
            win.RefreshFromModel();
        SaveAll();
        if (refreshManager) _manager?.RefreshList();
    }

    // 关闭便签窗口：只隐藏，保留数据（下次打开/启动时可恢复）
    private void HideNote(StickyNoteModel note)
    {
        if (_openWindows.TryGetValue(note.Id, out var win))
        {
            _openWindows.Remove(note.Id);
            if (win.IsLoaded)
                win.Close();
        }
        SaveAll();
    }

    // 真正删除便签：关闭窗口并从数据/磁盘移除
    public void DeleteNote(StickyNoteModel note)
    {
        if (_openWindows.TryGetValue(note.Id, out var win))
        {
            _openWindows.Remove(note.Id);
            if (win.IsLoaded)
                win.Close();
        }
        _notes.Remove(note);
        SaveAll();
        _manager?.RefreshList();
    }

    public void ShowManager()
    {
        if (_manager == null)
        {
            _manager = new MainWindow();
        }
        if (_manager.IsVisible)
            _manager.Activate();
        else
            _manager.Show();
        _manager.RefreshList();
    }

    public void SaveAll()
    {
        NoteStore.Save(_notes);
    }

    public void ExitApp()
    {
        foreach (var win in _openWindows.Values.ToList())
            win.Close();
        SaveAll();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    // 收到其他实例发来的激活消息时，显示并前置管理器窗口
    internal void ActivateFromOtherInstance()
    {
        ShowManager();
    }
}

// Win32 互操作：跨实例广播消息
internal static class NativeMethods
{
    public const int HWND_BROADCAST = 0xFFFF;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern int RegisterWindowMessage(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
}
