using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using StickyNoteWPF.Models;

namespace StickyNoteWPF;

public sealed class TaskPreviewItemsConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not IList items) return null;
        // The home preview must work before the desktop task window is opened.
        // Its view is independent of that window's default collection view.
        var view = new ListCollectionView(items);
        using (view.DeferRefresh())
        {
            if (values[1] is not true && values[2] is false)
                view.Filter = item => item is not TaskItem task || !task.IsDone;
            view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.IsDone), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Order), ListSortDirection.Descending));
            view.LiveFilteringProperties.Add(nameof(TaskItem.IsDone));
            view.IsLiveFiltering = true;
            view.LiveSortingProperties.Add(nameof(TaskItem.IsDone));
            view.LiveSortingProperties.Add(nameof(TaskItem.Order));
            view.IsLiveSorting = true;
        }
        return view;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
