using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using StickyNoteWPF;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

internal static class HotKeyChecks
{
    public static void Run(MainWindow manager)
    {
        for (var i = 1; i <= 12; i++)
            if (!HotKeyService.TryParse("F" + i, out var modifiers, out var vk) || modifiers != 0 || vk != 0x6f + i)
                throw new InvalidOperationException("单独的功能键解析失败。");
        foreach (var key in new[] { "A", "2", "Space", "+", "Ctrl+Alt+H", "Ctrl+F2", "Ctrl++" })
            if (!HotKeyService.TryParse(key, out _, out _)) throw new InvalidOperationException("有效快捷键被拒绝：" + key);
        foreach (var key in new[] { "", "Ctrl", "Alt+Shift", "F1+F2", "Ctrl++F1", "F0" })
            if (HotKeyService.TryParse(key, out _, out _)) throw new InvalidOperationException("无效快捷键被接受：" + key);

        var app = App.Current;
        var managerField = typeof(App).GetField("_manager", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var oldManager = managerField.GetValue(app);
        var notes = (Dictionary<Guid, StickyNoteWindow>)typeof(App).GetField("_openWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        var tasks = (Dictionary<Guid, TaskListWindow>)typeof(App).GetField("_openTaskLists", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        var toggle = typeof(App).GetMethod("ToggleAllWindows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var visible = typeof(App).GetProperty("HasVisibleWindows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var noteModel = new StickyNoteModel { Left = -10000, Top = -10000, IsOpen = true };
        var taskModel = new TaskListModel { Left = -10000, Top = -10000, IsOpen = true };
        var note = new StickyNoteWindow(noteModel);
        var task = new TaskListWindow(taskModel);
        try
        {
            managerField.SetValue(app, manager);
            notes.Add(noteModel.Id, note); tasks.Add(taskModel.Id, task);
            note.Show(); task.Show();
            toggle.Invoke(app, null); Pump();
            Check(!note.IsVisible && !task.IsVisible && manager.IsVisible && !(bool)visible.GetValue(app)!, "隐藏只影响便签和清单");
            toggle.Invoke(app, null); Pump();
            Check(note.IsVisible && task.IsVisible && manager.IsVisible, "主页可见时仍能恢复便签");
            manager.Hide(); toggle.Invoke(app, null); toggle.Invoke(app, null); Pump();
            Check(note.IsVisible && task.IsVisible && !manager.IsVisible, "主页原先隐藏时不应被显示");
            manager.Show();
            var registered = Enumerable.Range(1, 11).Select(i => "F" + i).FirstOrDefault(app.RegisterToggleHotKey);
            Check(registered != null, "至少一个样例功能键可注册");
            var hotKeyField = typeof(App).GetField("_hotKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var service = (HotKeyService)hotKeyField.GetValue(app)!;
            Check(!app.RegisterToggleHotKey("invalid key") && ReferenceEquals(service, hotKeyField.GetValue(app)) && service.IsRegistered, "失败更换保留旧注册");
            var source = (HwndSource)typeof(HotKeyService).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            Check(PostMessage(source.Handle, 0x0312, new IntPtr(0x5A11), IntPtr.Zero), "发送热键消息");
            Pump();
            Check(!note.IsVisible && !task.IsVisible && manager.IsVisible, "单键热键消息隐藏便签并保留主页");
            Check(PostMessage(source.Handle, 0x0312, new IntPtr(0x5A11), IntPtr.Zero), "再次发送热键消息");
            Pump();
            Check(note.IsVisible && task.IsVisible && manager.IsVisible && noteModel.IsOpen && taskModel.IsOpen, "热键恢复窗口且保留打开状态");
            Console.WriteLine($"PASS: single function keys ({registered}), hotkey callback, replacement failure and manager visibility.");
        }
        finally
        {
            app.UnregisterToggleHotKey();
            notes.Remove(noteModel.Id); tasks.Remove(taskModel.Id);
            note.Close(); task.Close();
            manager.Show(); managerField.SetValue(app, oldManager);
        }
    }
    private static void Pump() => App.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
}
