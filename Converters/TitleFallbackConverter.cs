using System;
using System.Globalization;
using System.Windows.Data;

namespace StickyNoteWPF;

// 便签标题为空时显示默认占位“便利贴”，编辑写回时把占位还原为空
public class TitleFallbackConverter : IValueConverter
{
    private const string Fallback = "便利贴";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) ? Fallback : s;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string) ?? string.Empty;
        return string.Equals(s.Trim(), Fallback, StringComparison.Ordinal) ? string.Empty : s;
    }
}
