using System.Configuration;
using System.Data;
using System.Windows;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class App : System.Windows.Application
{
    private List<StickyNoteModel> _notes = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _openWindows = new();
    private MainWindow? _manager;
    private TrayIconService? _tray;
    public AppSettings Settings { get; private set; } = new();

    public IReadOnlyList<StickyNoteModel> Notes => _notes;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
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
    }

    // 管理界面修改标题等后，刷新已打开的便签窗口显示
    public void RefreshNote(StickyNoteModel note)
    {
        if (_openWindows.TryGetValue(note.Id, out var win) && win.IsLoaded)
            win.ApplyTitle();
        SaveAll();
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
}
