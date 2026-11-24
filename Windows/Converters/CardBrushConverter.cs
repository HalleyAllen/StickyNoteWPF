using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace StickyNoteWPF;

// 保留卡片原色；文字按实际背景计算，兼容深色和浅色卡片。
public sealed class CardBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = ReadColor(value);
        var mode = parameter as string;
        if (mode is "text" or "secondary")
        {
            var background = color;
            var text = Brightness(background) > 0.179 ? Colors.Black : Colors.White;
            color = text;
            if (mode == "secondary")
            {
                var muted = Mix(text, background, 0.22);
                var a = Brightness(muted);
                var b = Brightness(background);
                if ((Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05) >= 4.5) color = muted;
            }
        }
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ReadColor(object? value)
    {
        Color color;
        if (value is SolidColorBrush brush) color = brush.Color;
        else if (value is string text)
        {
            try { color = (Color)ColorConverter.ConvertFromString(text); }
            catch (Exception ex) when (ex is FormatException or NotSupportedException) { color = Colors.White; }
        }
        else color = Colors.White;
        // 半透明颜色在管理器画布上的实际显示色，不改动模型中的颜色或透明度。
        return Mix(Color.FromRgb(245, 246, 248), color, color.A / 255d);
    }

    private static Color Mix(Color source, Color target, double amount) => Color.FromRgb(
        (byte)Math.Round(source.R + (target.R - source.R) * amount),
        (byte)Math.Round(source.G + (target.G - source.G) * amount),
        (byte)Math.Round(source.B + (target.B - source.B) * amount));

    private static double Brightness(Color color)
    {
        static double Linear(byte value)
        {
            var v = value / 255d;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
