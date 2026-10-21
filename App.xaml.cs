using System.Collections.ObjectModel;
﻿using System.Configuration;
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

    private ObservableCollection<StickyNoteModel> _notes = new();
    private ObservableCollection<TaskListModel> _taskLists = new();
    private readonly Dictionary<Guid, StickyNoteWindow> _openWindows = new();
    private readonly Dictionary<Guid, TaskListWindow> _openTaskLists = new();
    private MainWindow? _manager;
    private TrayIconService? _tray;
    private bool _isExiting;
    private bool _exitInProgress;
    private long _saveRevision;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly SaveQueue<string> _saveQueue;
    private bool _saveWarningShown;
    private bool _settingsWarningShown;
    private HotKeyService? _hotKey;
    public AppSettings Settings { get; private set; } = new();

    public App() => _saveQueue = new SaveQueue<string>(WriteSnapshot);

    protected virtual bool WriteSnapshot(string json) => NoteStore.SaveJson(json);

    public IReadOnlyList<StickyNoteModel> Notes => _notes;
    public IReadOnlyList<TaskListModel> TaskLists => _taskLists;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底日志：用于定位单文件发布等场景下的启动崩溃
        DispatcherUnhandledException += (_, args) =>
            Logger.LogException("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.LogException("AppDomain.UnhandledException",
                args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        Logger.Log($"=== App.OnStartup 开始，进程PID={System.Diagnostics.Process.GetCurrentProcess().Id} ===");
        // 单实例检测：若已有实例在运行，则请求旧实例退出并等待其释放文件，由新实例接管
        _singleMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Logger.Log("OnStartup: 检测到已有实例，请求旧实例还原窗口，本次启动直接退出");
            // 通知旧实例还原并激活窗口（不重启、不接管数据）
            NativeMethods.PostMessage(
                (IntPtr)NativeMethods.HWND_BROADCAST,
                ActivateMessage,
                IntPtr.Zero,
                IntPtr.Zero);
            // 本实例不加载任何数据，直接退出；标记 _isExiting 防止 OnExit 兜底存盘覆盖旧实例数据
            _singleMutex?.Close();
            _singleMutex = null;
            _isExiting = true;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _saveTimer.Tick += SaveTimer_Tick;
        _saveQueue.SaveFailed += () => Dispatcher.BeginInvoke(new Action(ShowSaveError));
        AppSettings.SaveFailed += ShowSettingsSaveError;

        Settings = AppSettings.Load();
        var data = NoteStore.Load();
        _notes = new(data.Notes);
        _taskLists = new(data.TaskLists);
        if (!NoteStore.LoadSucceeded) ShowSaveError();
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
        if (string.IsNullOrWhiteSpace(combination))
        {
            UnregisterToggleHotKey();
            return true;
        }
        _hotKey ??= new HotKeyService();
        return _hotKey.Register(combination, ToggleAllWindows);
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

    // 默认外观：按入口类型单独打开 —— forTaskOnly=true 只设置新建任务清单的默认，
    // 反之只设置新建便利贴的默认，两套互不影响（见 DefaultAppearanceWindow）
    public void OpenDefaultAppearance(bool forTaskOnly)
    {
        var w = new DefaultAppearanceWindow(forTaskOnly);
        if (_manager != null)
            w.Owner = _manager;
        w.ShowDialog();
    }

    public void OpenNoteSettings(StickyNoteWindow noteWindow)
    {
        var w = new StickyNoteSettingsWindow(noteWindow);
        w.Owner = noteWindow;

        WindowPlacement.PlaceBeside(w, noteWindow);

        w.ShowDialog();
    }

    public void OpenTaskListSettings(TaskListWindow listWindow)
    {
        var w = new TaskListSettingsWindow(listWindow);
        w.Owner = listWindow;

        WindowPlacement.PlaceBeside(w, listWindow);

        w.ShowDialog();
    }

    public void OpenNote(StickyNoteModel note)
    {
        if (_openWindows.TryGetValue(note.Id, out var existing))
        {
            RestoreAndActivate(existing);
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
    public void RefreshNote(StickyNoteModel note, bool refreshManager = false, bool titleOnly = false)
    {
        if (_openWindows.TryGetValue(note.Id, out var win) && win.IsLoaded)
        {
            if (titleOnly) win.ApplyTitle();
            else win.RefreshFromModel();
        }
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
        // 任务清单默认外观：使用「默认外观」中独立设置的任务清单默认（与便利贴互不影响）。
        // 常量兜底仅在理论上字段缺失时生效（Load() 已对旧数据完成迁移固化）。
        list.Color = Settings.TaskListColor ?? AppSettings.TaskListDefaultColorHex;
        list.TextColor = Settings.TaskListTextColor ?? AppSettings.TaskListDefaultTextColorHex;
        list.Opacity = Settings.TaskListOpacity ?? AppSettings.TaskListDefaultOpacity;
        list.FontSize = Settings.TaskListFontSize ?? AppSettings.TaskListDefaultFontSize;
        // 新建任务清单默认是否显示已完成任务（来自任务清单“默认外观”开关）
        list.ShowCompleted = Settings.TaskListShowCompleted;
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
            RestoreAndActivate(existing);
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

    public void RefreshTaskList(TaskListModel list, bool refreshManager = false, bool titleOnly = false)
    {
        if (_openTaskLists.TryGetValue(list.Id, out var win) && win.IsLoaded)
        {
            if (titleOnly) win.ApplyTitle();
            else win.RefreshFromModel();
        }
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
        RestoreAndActivate(_manager);
        _manager.RefreshLists();
    }

    public void NotifyTaskContentChanged()
    {
        SaveAll();
        _manager?.ScheduleRefresh();
    }

    public void SaveAll()
    {
        if (_isExiting) return;
        _saveRevision++;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        QueueSnapshot();
    }

    private bool QueueSnapshot()
    {
        try
        {
            // Serialize on the UI thread; the background writer never touches mutable models.
            _saveQueue.Enqueue(NoteStore.Serialize(new StoreData { Notes = _notes.ToList(), TaskLists = _taskLists.ToList() }));
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("App.QueueSnapshot", ex);
            ShowSaveError();
            return false;
        }
    }

    private async Task<bool> FlushSavesAsync()
    {
        long revision;
        do
        {
            revision = _saveRevision;
            if (_saveTimer.IsEnabled)
            {
                _saveTimer.Stop();
                if (!QueueSnapshot()) return false;
            }
            await _saveQueue.FlushAsync();
            // Editing can continue while disk I/O runs. Include those edits before exiting.
        } while (_saveTimer.IsEnabled || revision != _saveRevision);
        return _saveQueue.LastSaveSucceeded;
    }

    // The dispatcher cannot await during OnExit/OnSessionEnding. Queue workers never use it.
    private bool FlushSaves()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            if (!QueueSnapshot()) return false;
        }
        _saveQueue.FlushAsync().GetAwaiter().GetResult();
        return _saveQueue.LastSaveSucceeded;
    }

    protected virtual void ShowSaveError()
    {
        if (_saveWarningShown || _exitInProgress) return;
        _saveWarningShown = true;
        System.Windows.MessageBox.Show("便签数据未能保存。请检查磁盘空间、数据目录权限及备份文件。\n数据目录：" +
            System.IO.Path.GetDirectoryName(NoteStore.SavePath), "保存失败",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowSettingsSaveError()
    {
        if (_settingsWarningShown) return;
        _settingsWarningShown = true;
        System.Windows.MessageBox.Show("设置未能保存。请检查磁盘空间和目录权限。\n设置文件：" + AppSettings.SavePath +
            "\n日志：" + Logger.LogPath, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public async void ExitApp() => await RequestExitAsync();

    public async Task<bool> RequestExitAsync()
    {
        if (_exitInProgress || _isExiting) return false;
        _exitInProgress = true;
        try
        {
            Logger.Log($"ExitApp: 请求退出，便利贴数={_notes.Count}，任务清单数={_taskLists.Count}");
            while (true)
            {
                UpdateOpenStates();
                SaveAll();
                if (await FlushSavesAsync()) break;
                if (!ConfirmSaveRetry()) return false;
            }
            // Keep every window, the tray and the mutex alive until the latest snapshot is safe.
            _isExiting = true;
            ShutdownAfterSave();
            return true;
        }
        finally { _exitInProgress = false; }
    }

    protected virtual bool ConfirmSaveRetry() => System.Windows.MessageBox.Show(
        "便签数据未能保存，程序将保留当前内容。请检查磁盘空间、目录权限及备份文件。\n" +
        "选择“是”重试保存；选择“否”取消退出，返回编辑。\n数据目录：" +
        System.IO.Path.GetDirectoryName(NoteStore.SavePath), "保存失败，暂不退出",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void UpdateOpenStates()
    {
        foreach (var note in _notes)
            note.IsOpen = _openWindows.ContainsKey(note.Id);
        foreach (var list in _taskLists)
            list.IsOpen = _openTaskLists.ContainsKey(list.Id);
    }

    protected virtual void ShutdownAfterSave()
    {
        foreach (var win in _openWindows.Values.ToList()) win.Close();
        foreach (var win in _openTaskLists.Values.ToList()) win.Close();
        _hotKey?.Dispose();
        _hotKey = null;
        _tray?.Dispose();
        _tray = null;
        _singleMutex?.ReleaseMutex();
        _singleMutex?.Close();
        _singleMutex = null;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (e.Cancel || _isExiting) return;
        UpdateOpenStates();
        SaveAll();
        if (!FlushSaves())
        {
            e.Cancel = true;
            ShowSaveError();
            return;
        }
        // OnExit runs after WPF closes windows; preserve the open states saved above.
        _isExiting = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"OnExit: _isExiting={_isExiting}，内存便利贴数={_notes.Count}，任务清单数={_taskLists.Count}");
        // Controlled exits have already saved before closing windows; avoid a second snapshot.
        if (!_isExiting)
        {
            UpdateOpenStates();
            SaveAll();
            if (!FlushSaves()) ShowSaveError();
        }
        _saveTimer.Tick -= SaveTimer_Tick;
        AppSettings.SaveFailed -= ShowSettingsSaveError;
        _hotKey?.Dispose();
        _hotKey = null;
        _tray?.Dispose();
        base.OnExit(e);
    }

    // 收到其他实例发来的激活消息时，还原并激活所有已打开的窗口
    internal void ActivateFromOtherInstance()
    {
        foreach (var win in _openWindows.Values)
            RestoreAndActivate(win);
        foreach (var win in _openTaskLists.Values)
            RestoreAndActivate(win);
        if (_manager != null)
        {
            RestoreAndActivate(_manager);
            _manager.RefreshLists();
        }
        Logger.Log("ActivateFromOtherInstance: 已还原并激活所有窗口");
    }

    // 显示（若隐藏）、还原（若最小化）并激活窗口
    private static void RestoreAndActivate(Window win)
    {
        if (!win.IsVisible)
            win.Show();
        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
        win.Activate();
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
