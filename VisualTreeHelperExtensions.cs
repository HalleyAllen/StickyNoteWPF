using System.Windows;
using System.Windows.Media;

namespace StickyNoteWPF;

public static class VisualTreeHelperExtensions
{
    public static T? FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                return t;
            var descendant = child.FindVisualChild<T>();
            if (descendant != null)
                return descendant;
        }
        return null;
    }
}
