using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
    // 用于通知已运行实例退出的自定义窗口消息（新实例接管时发送）
    internal static readonly int ShutdownMessage = NativeMethods.RegisterWindowMessage("StickyNoteWPF_Shutdown");

    private List<StickyNoteModel> _notes = new();
    private List<TaskListModel> _taskLists = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _openWindows = new();
    private readonly Dictionary<Guid, TaskListWindow> _openTaskLists = new();
    private MainWindow? _manager;
    private TrayIconService? _tray;
    private bool _isExiting;
    private HotKeyService? _hotKey;
    public AppSettings Settings { get; private set; } = new();

    public IReadOnlyList<StickyNoteModel> Notes => _notes;
    public IReadOnlyList<TaskListModel> TaskLists => _taskLists;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Log($"=== App.OnStartup 开始，进程PID={System.Diagnostics.Process.GetCurrentProcess().Id} ===");
        // 单实例检测：若已有实例在运行，则请求旧实例退出并等待其释放文件，由新实例接管
        _singleMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Logger.Log("OnStartup: 检测到已有实例，开始请求旧实例退出并接管");
            // 1) 通知旧实例存盘退出
            NativeMethods.PostMessage(
                (IntPtr)NativeMethods.HWND_BROADCAST,
                ShutdownMessage,
                IntPtr.Zero,
                IntPtr.Zero);

            // 2) 找到其他 StickyNote 进程，请求其关闭主窗口（正常退出存盘），等待退出
            var selfId = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("StickyNote"))
            {
                if (p.Id == selfId) continue;
                try { p.CloseMainWindow(); } catch { }
            }
            // 等待旧实例退出（最多约 2.5 秒）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2500)
            {
                var stillAlive = System.Diagnostics.Process.GetProcessesByName("StickyNote")
                    .Any(p => p.Id != selfId);
                if (!stillAlive) break;
                System.Threading.Thread.Sleep(150);
            }
            // 3) 仍有残留则强制结束，避免两个实例写同一文件
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("StickyNote"))
            {
                if (p.Id == selfId) continue;
                try { p.Kill(); } catch { }
            }

            // 释放本实例占用的互斥锁引用，重新获取以确保独占
            _singleMutex.Close();
            System.Threading.Thread.Sleep(200);
            _singleMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                _singleMutex.Close();
                _singleMutex = null;
            }
        }

        base.OnStartup(e);

        Settings = AppSettings.Load();
        var data = NoteStore.Load();
        _notes = data.Notes;
        _taskLists = data.TaskLists;
        _tray = new TrayIconService(this);

        // 启动时只打开上次处于打开状态的便利贴与任务清单（其余保持关闭，可在管理界面打开）
        foreach (var note in _notes)
            if (note.IsOpen)
                OpenNote(note);
        foreach (var list in _taskLists)
            if (list.IsOpen)
                OpenTaskList(list);

        ApplyTopmost(Settings.GlobalTopmost);

        // 注册全局快捷键（隐藏/显示所有窗口），未设置则跳过
        RegisterToggleHotKey(Settings.ToggleWindowsHotKey);

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
        _manager?.RefreshLists();
        _tray?.ShowBalloon("已新建便利贴", "在桌面上编辑你的内容吧。");
    }

    public void ApplyTopmost(bool topmost)
    {
        foreach (var win in _openWindows.Values)
            win.Topmost = topmost;
        foreach (var win in _openTaskLists.Values)
            win.Topmost = topmost;
    }

    // ====== 全局快捷键：隐藏/显示所有窗口（设置窗口除外）======

    /// <summary>注册「隐藏/显示全部窗口」全局快捷键；组合为空表示不注册，注册失败返回 false。</summary>
    public bool RegisterToggleHotKey(string combination)
    {
        _hotKey?.Dispose();
        _hotKey = null;
        if (string.IsNullOrWhiteSpace(combination)) return true; // 未设置，视为成功（无操作）

        var service = new HotKeyService();
        if (!service.Register(combination, ToggleAllWindows))
        {
            service.Dispose();
            return false;
        }
        _hotKey = service;
        return true;
    }

    public void UnregisterToggleHotKey()
    {
        _hotKey?.Dispose();
        _hotKey = null;
    }

    // 当前是否有任一便签/任务清单/管理器窗口可见（供托盘菜单动态显示文字）
    internal bool HasVisibleWindows =>
        _openWindows.Values.Any(w => w.IsVisible)
        || _openTaskLists.Values.Any(w => w.IsVisible)
        || (_manager?.IsVisible ?? false);

    // 切换所有便签/任务清单/管理器窗口的显示状态；设置窗口（模态）不受影响
    internal void ToggleAllWindows()
    {
        bool anyVisible = _openWindows.Values.Any(w => w.IsVisible)
            || _openTaskLists.Values.Any(w => w.IsVisible)
            || (_manager?.IsVisible ?? false);

        if (anyVisible)
        {
            foreach (var win in _openWindows.Values) win.Hide();
            foreach (var win in _openTaskLists.Values) win.Hide();
            _manager?.Hide();
        }
        else
        {
            foreach (var win in _openWindows.Values) win.Show();
            foreach (var win in _openTaskLists.Values) win.Show();
            if (_manager != null)
            {
                _manager.Show();
                _manager.RefreshLists();
            }
            // 清除键盘焦点：重新显示后 WPF 会把焦点分给之前点过的控件，
            // 从而画出焦点虚线框，这里主动清除避免残留
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Keyboard.ClearFocus()));
        }
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

    public void OpenTaskListSettings(TaskListWindow listWindow)
    {
        var w = new TaskListSettingsWindow(listWindow);
        w.Owner = listWindow;

        const double gap = 12;
        var screen = System.Windows.SystemParameters.WorkArea;
        double left = listWindow.Left + listWindow.Width + gap;
        double top = listWindow.Top;

        if (left + w.Width > screen.Right)
        {
            left = listWindow.Left - w.Width - gap;
            if (left < screen.Left)
                left = screen.Left + gap;
        }

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
        note.IsOpen = true;
        SaveAll();
        win.Show();
        win.ApplyForceShowAll();
    }

    // 全局“全部显示”开关切换后，实时刷新所有已打开的窗口
    public void ApplyForceShowAll()
    {
        foreach (var win in _openWindows.Values)
            win.ApplyForceShowAll();
        foreach (var win in _openTaskLists.Values)
            win.ApplyForceShowAll();
    }

    // 外观/标题等修改后，刷新已打开的便签窗口显示；refreshManager=true 时同步刷新管理界面列表
    public void RefreshNote(StickyNoteModel note, bool refreshManager = false)
    {
        if (_openWindows.TryGetValue(note.Id, out var win) && win.IsLoaded)
            win.RefreshFromModel();
        SaveAll();
        if (refreshManager) _manager?.RefreshLists();
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
        note.IsOpen = false;
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
        _manager?.RefreshLists();
    }

    // ====== 任务清单 ======

    public void CreateTaskList()
    {
        var list = new TaskListModel();
        list.TextColor = Settings.NoteTextColor;
        list.Opacity = Settings.WindowOpacity;
        list.FontSize = Settings.DefaultFontSize;
        list.Left = 200 + (_taskLists.Count % 5) * 30;
        list.Top = 150 + (_taskLists.Count % 5) * 30;
        list.Items.Add(new TaskItem { Text = "新任务" });
        _taskLists.Add(list);
        SaveAll();
        OpenTaskList(list);
        _manager?.RefreshLists();
        _tray?.ShowBalloon("已新建任务清单", "在桌面上勾选完成你的任务吧。");
    }

    public void OpenTaskList(TaskListModel list)
    {
        if (_openTaskLists.TryGetValue(list.Id, out var existing))
        {
            existing.Activate();
            return;
        }

        var win = new TaskListWindow(list);
        win.ClosedByUser += (_, _) => HideTaskList(list);
        win.Closed += (_, _) => _openTaskLists.Remove(list.Id);
        _openTaskLists[list.Id] = win;
        list.IsOpen = true;
        SaveAll();
        win.Show();
        win.ApplyForceShowAll();
    }

    public void RefreshTaskList(TaskListModel list, bool refreshManager = false)
    {
        if (_openTaskLists.TryGetValue(list.Id, out var win) && win.IsLoaded)
            win.RefreshFromModel();
        SaveAll();
        if (refreshManager) _manager?.RefreshLists();
    }

    private void HideTaskList(TaskListModel list)
    {
        if (_openTaskLists.TryGetValue(list.Id, out var win))
        {
            _openTaskLists.Remove(list.Id);
            if (win.IsLoaded)
                win.Close();
        }
        list.IsOpen = false;
        SaveAll();
    }

    public void DeleteTaskList(TaskListModel list)
    {
        if (_openTaskLists.TryGetValue(list.Id, out var win))
        {
            _openTaskLists.Remove(list.Id);
            if (win.IsLoaded)
                win.Close();
        }
        _taskLists.Remove(list);
        SaveAll();
        _manager?.RefreshLists();
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
        _manager.RefreshLists();
    }

    public void SaveAll()
    {
        int notes = _notes?.Count ?? 0;
        int lists = _taskLists?.Count ?? 0;
        Logger.Log($"SaveAll: 准备保存，内存便利贴数={notes}，任务清单数={lists}");
        NoteStore.Save(new StoreData
        {
            Notes = _notes,
            TaskLists = _taskLists
        });
    }

    public void ExitApp()
    {
        _isExiting = true;
        Logger.Log($"ExitApp: 开始退出，内存便利贴数={_notes?.Count ?? 0}，任务清单数={_taskLists?.Count ?? 0}");
        // 退出前根据当前打开的窗口写回 IsOpen，下次启动只恢复这些窗口
        foreach (var note in _notes)
            note.IsOpen = _openWindows.ContainsKey(note.Id);
        foreach (var list in _taskLists)
            list.IsOpen = _openTaskLists.ContainsKey(list.Id);
        SaveAll();
        foreach (var win in _openWindows.Values.ToList())
            win.Close();
        foreach (var win in _openTaskLists.Values.ToList())
            win.Close();
        _hotKey?.Dispose();
        _hotKey = null;
        _tray?.Dispose();
        _tray = null;
        // 释放单实例互斥锁，交出新实例接管
        _singleMutex?.ReleaseMutex();
        _singleMutex?.Close();
        _singleMutex = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"OnExit: _isExiting={_isExiting}，内存便利贴数={_notes?.Count ?? 0}，任务清单数={_taskLists?.Count ?? 0}");
        // 兜底：仅当尚未经 ExitApp 处理时，写回当前打开状态
        if (!_isExiting)
        {
            foreach (var note in _notes)
                note.IsOpen = _openWindows.ContainsKey(note.Id);
            foreach (var list in _taskLists)
                list.IsOpen = _openTaskLists.ContainsKey(list.Id);
            SaveAll();
        }
        _hotKey?.Dispose();
        _hotKey = null;
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
