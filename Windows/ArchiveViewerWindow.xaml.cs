using System.Text.Json;
using System.Windows;
using StickyNoteWPF.Models;
using StickyNoteWPF.Sync;

namespace StickyNoteWPF;

public partial class ArchiveViewerWindow : Window
{
    public ArchiveViewerWindow(StickyNoteModel note, Action restore)
    {
        InitializeComponent();
        Heading.Text = string.IsNullOrWhiteSpace(note.Title) ? "便利贴" : note.Title;
        Description.Text = "已归档的便利贴 · 恢复后可继续编辑";
        NoteBody.Text = note.Text;
        NoteBody.Visibility = Visibility.Visible;
        RestoreButton.Click += (_, _) => restore();
    }

    public ArchiveViewerWindow(TaskListModel list, Action restore)
    {
        InitializeComponent();
        Heading.Text = string.IsNullOrWhiteSpace(list.Title) ? "任务清单" : list.Title;
        Description.Text = "已归档的待办 · 显示全部任务，包含已完成项和子任务";
        // A separate snapshot has independent collection views. The desktop's
        // ShowCompleted filter must neither hide archived tasks nor be changed here.
        var snapshot = JsonSerializer.Deserialize<TaskListModel>(JsonSerializer.Serialize(list, SnapshotTools.Json), SnapshotTools.Json)!;
        ArchivedTaskTree.ItemsSource = snapshot.Items;
        ArchivedTaskTree.Visibility = Visibility.Visible;
        RestoreButton.Click += (_, _) => restore();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
