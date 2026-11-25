using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public static class TaskTemplates
{
    private static readonly string[] DayNames = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];

    public static bool HasWeeklyTemplate(TaskListModel list)
        => list.WeeklyTemplateDays != null || DayNames.All(name => list.Items.Any(item => item.Text == name));

    public static TaskItem ApplyWeekly(TaskListModel list, DayOfWeek today)
    {
        var firstApplication = !HasWeeklyTemplate(list);
        var mapping = list.WeeklyTemplateDays ?? new Dictionary<DayOfWeek, Guid>();
        var groups = new List<TaskItem>();
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)((i + 1) % 7);
            var group = mapping.TryGetValue(day, out var id) ? list.Items.FirstOrDefault(item => item.Id == id) : null;
            group ??= list.WeeklyTemplateDays == null ? list.Items.FirstOrDefault(item => item.Text == DayNames[i]) : null;
            if (group == null)
            {
                group = new TaskItem { Text = DayNames[i], Order = 7 - i };
                list.Items.Add(group);
            }
            mapping[day] = group.Id;
            groups.Add(group);
        }
        list.WeeklyTemplateDays = mapping;
        var destination = groups.Single(item => item.Id == mapping[today]);
        var unassigned = list.Items.Where(item => !groups.Contains(item)).OrderByDescending(item => item.Order).ToArray();
        foreach (var item in unassigned)
        {
            list.Items.Remove(item);
            // 兼容旧版重复添加的星期分组，保留整个任务树，收进同一天。
            var dayIndex = Array.IndexOf(DayNames, item.Text);
            var target = dayIndex >= 0 ? groups[dayIndex] : destination;
            target.SubItems.Add(item);
            target.RefreshHasSubItems();
        }
        foreach (var group in groups)
        {
            if (firstApplication && group.SubItems.Count == 0) group.SubItems.Add(new TaskItem { Text = "新任务", Order = 1 });
            group.RefreshHasSubItems();
        }
        return destination;
    }

    public static IReadOnlyList<TaskItem> CreateWeekly(long previousMaxOrder)
    {
        if (previousMaxOrder > long.MaxValue - 7) throw new ArgumentOutOfRangeException(nameof(previousMaxOrder));
        string[] days = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        return days.Select((day, index) => new TaskItem
        {
            Text = day,
            Order = previousMaxOrder + 7 - index,
            SubItems = { new TaskItem { Text = "新任务", Order = 1 } }
        }).ToArray();
    }
}
