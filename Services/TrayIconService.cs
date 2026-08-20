using System.Drawing;
using System.IO;
using System.Windows.Forms;
using StickyNoteWPF.Models;
using ToolStripItem = System.Windows.Forms.ToolStripItem;

namespace StickyNoteWPF.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly App _app;

    public TrayIconService(App app)
    {
        _app = app;
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "便利贴 StickyNote",
            Visible = true
        };
        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => _app.ShowManager();
    }

    private static Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "StickyNote.ico");
        if (!File.Exists(path))
            throw new InvalidOperationException("找不到托盘图标文件：" + path);
        return new Icon(path);
    }

    private ContextMenuStrip BuildMenu()
    {
        var strip = new ContextMenuStrip();
        // 「隐藏/显示全部窗口」：打开菜单时根据当前可见状态动态更新文字
        var toggleItem = new System.Windows.Forms.ToolStripMenuItem("隐藏全部窗口", null, (_, _) => _app.ToggleAllWindows());
        strip.Opening += (_, _) =>
            toggleItem.Text = _app.HasVisibleWindows ? "隐藏全部窗口" : "显示全部窗口";
        strip.Items.AddRange(new ToolStripItem[]
        {
            new System.Windows.Forms.ToolStripMenuItem("新建便利贴", null, (_, _) => _app.CreateNote()),
            new System.Windows.Forms.ToolStripMenuItem("新建任务清单", null, (_, _) => _app.CreateTaskList()),
            new System.Windows.Forms.ToolStripMenuItem("管理便利贴", null, (_, _) => _app.ShowManager()),
            new System.Windows.Forms.ToolStripSeparator(),
            toggleItem,
            new System.Windows.Forms.ToolStripSeparator(),
            new System.Windows.Forms.ToolStripMenuItem("退出", null, (_, _) => _app.ExitApp())
        });
        return strip;
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
