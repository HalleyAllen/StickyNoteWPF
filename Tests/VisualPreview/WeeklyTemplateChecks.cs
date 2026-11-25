using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using StickyNoteWPF;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

internal static class WeeklyTemplateChecks
{
    public static void Run(Action<Window, string> render)
    {
        var first = TaskTemplates.CreateWeekly(20);
        var second = TaskTemplates.CreateWeekly(20);
        string[] days = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        if (!first.Select(item => item.Text).SequenceEqual(days) || first[0].Order != 27 || first[^1].Order != 21
            || first.Any(item => item.SubItems.Count != 1 || item.IsDone || !item.IsExpanded)
            || first.SelectMany(item => new[] { item.Id, item.SubItems[0].Id }).Intersect(second.SelectMany(item => new[] { item.Id, item.SubItems[0].Id })).Any())
            throw new InvalidOperationException("每次一周模板必须按周一至周日生成独立的可编辑任务。");
        var existing = new TaskItem { Text = "已有任务", Order = 1 };
        var model = new TaskListModel { Title = "一周任务", Color = AppSettings.TaskListDefaultColorHex, TextColor = AppSettings.TaskListDefaultTextColorHex, Opacity = AppSettings.TaskListDefaultOpacity, Items = { existing } };
        var window = new TaskListWindow(model) { Width = 380, Height = 680, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            var other = (Button)window.FindName("OtherButton");
            var weekly = (MenuItem)other.ContextMenu.Items[0];
            var showCompleted = (MenuItem)other.ContextMenu.Items[1];
            weekly.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            var list = (ListBox)window.FindName("TaskList");
            var today = model.Items.Single(item => item.Id == model.WeeklyTemplateDays![DateTime.Today.DayOfWeek]);
            if (model.Items.Count != 7 || !today.SubItems.Contains(existing) || !list.Items.Cast<TaskItem>().Select(item => item.Text).SequenceEqual(days))
                throw new InvalidOperationException("模板只能有七个分组，已有任务应原样归入今天。");
            var monday = today;
            var child = existing;
            var editor = FindEditor(list, child) ?? throw new InvalidOperationException("模板子任务应生成编辑器。");
            editor.Text = "安排本周重点";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            if (child.Text != "安排本周重点") throw new InvalidOperationException("模板子任务应可编辑。");
            existing.IsDone = true; child.IsDone = true;
            showCompleted.IsChecked = false;
            showCompleted.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            if (model.ShowCompleted || CollectionViewSource.GetDefaultView(monday.SubItems).Cast<TaskItem>().Contains(existing))
                throw new InvalidOperationException("开关应隐藏根任务和子任务中的完成项。");
            showCompleted.IsChecked = true;
            showCompleted.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            if (!model.ShowCompleted || !CollectionViewSource.GetDefaultView(monday.SubItems).Cast<TaskItem>().Contains(child))
                throw new InvalidOperationException("完成项应能恢复显示，不能被删除。");
            var copy = StickyNoteWPF.Sync.SnapshotTools.ForSync(new() { TaskLists = new() { model } }).TaskLists[0];
            if (!copy.ShowCompleted || copy.Items.Count != 7 || copy.WeeklyTemplateDays?.Count != 7 || copy.Items.Single(item => item.Id == today.Id).SubItems[0].Text != child.Text)
                throw new InvalidOperationException("模板和完成项显示设置必须参与现有数据同步。");
            existing.IsDone = false; child.IsDone = false; window.ApplyShowCompleted(); Pump();
            render(window, "weekly-template");
            weekly.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            if (model.Items.Count != 7 || !today.SubItems.Contains(existing)) throw new InvalidOperationException("不能重复添加模板。");
            var previousCount = today.SubItems.Count;
            ((Button)window.FindName("AddTaskButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            if (model.Items.Count != 7 || today.SubItems.Count != previousCount + 1) throw new InvalidOperationException("新任务应自动放入今天。");
            today.Text = "今天的安排";
            var restored = System.Text.Json.JsonSerializer.Deserialize<TaskListModel>(System.Text.Json.JsonSerializer.Serialize(model))!;
            TaskTemplates.ApplyWeekly(restored, DateTime.Today.DayOfWeek);
            if (restored.Items.Count != 7 || restored.Items.Single(item => item.Id == today.Id).Text != "今天的安排") throw new InvalidOperationException("重开或改标题后不能重复应用模板。");
            var legacy = new TaskListModel();
            foreach (var day in TaskTemplates.CreateWeekly(0)) legacy.Items.Add(day);
            var loose = new TaskItem { Text = "未分组任务", IsDone = true, SubItems = { new TaskItem { Text = "原子任务" } } };
            legacy.Items.Add(loose);
            TaskTemplates.ApplyWeekly(legacy, DayOfWeek.Friday);
            if (legacy.Items.Count != 7 || !legacy.Items.Single(item => item.Text == "周五").SubItems.Contains(loose) || !loose.IsDone || loose.SubItems.Count != 1) throw new InvalidOperationException("旧模板应被识别，任务树和完成状态应保留。");
            model.IsLocked = true; window.RefreshFromModel();
            other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (weekly.IsEnabled) throw new InvalidOperationException("锁定后模板菜单应禁用。");
            other.ContextMenu.IsOpen = false;
            weekly.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (model.Items.Count != 7) throw new InvalidOperationException("锁定后不能添加模板。");
            Console.WriteLine("PASS: one-time weekly template, automatic today assignment, legacy adoption, renamed groups, reopening, completion and sync.");
        }
        finally { window.Close(); }
    }
    private static void Pump() => App.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static TextBox? FindEditor(DependencyObject parent, TaskItem item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox text && ReferenceEquals(text.DataContext, item)) return text;
            if (FindEditor(child, item) is { } found) return found;
        }
        return null;
    }
}
