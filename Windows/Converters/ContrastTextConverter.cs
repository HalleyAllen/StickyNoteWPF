using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StickyNoteWPF;

// 根据背景色亮度返回对比文字色：浅背景用深色文字，深背景用浅色文字，保证可读
public class ContrastTextConverter : IValueConverter
{
    private static readonly System.Windows.Media.Brush Dark = new SolidColorBrush(Colors.Black);
    private static readonly System.Windows.Media.Brush Light = new SolidColorBrush(Colors.White);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && TryParseColor(hex, out var c))
        {
            // 感知亮度（含 alpha 视为不透明背景判断）
            var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance > 0.6 ? Dark : Light;
        }
        return Dark;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryParseColor(string hex, out System.Windows.Media.Color color)
    {
        try
        {
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = Colors.Transparent;
            return false;
        }
    }
}
