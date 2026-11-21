using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using StickyNoteWPF.Models;

namespace StickyNoteWPF;

public class TaskProgressConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not System.Collections.ObjectModel.ObservableCollection<TaskItem> items)
            return string.Empty;
        if (items.Count == 0)
            return "暂无任务";
        int done = 0;
        foreach (var it in items)
            if (it.IsDone) done++;
        return $"已完成 {done}/{items.Count}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
