using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyNoteWPF.Services;

public static class AppearanceHelper
{
    public static System.Windows.Media.Brush MakeBrush(string hex)
    {
        try
        {
            return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return System.Windows.Media.Brushes.Black;
        }
    }

    public static System.Windows.Media.Color ParseColor(string hex, System.Windows.Media.Color fallback)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }

    public static bool TryParseColor(string? hex, out System.Windows.Media.Color color)
    {
        try
        {
            color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex!);
            return true;
        }
        catch
        {
            color = System.Windows.Media.Colors.Transparent;
            return false;
        }
    }

    // 根据背景色亮度返回对比前景色（浅背景深色，深背景浅色），保证图标可读
    public static System.Windows.Media.Color GetContrastColor(System.Windows.Media.Color bg)
    {
        var luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance > 0.6 ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White;
    }

    // ---- 颜色选择器：最近使用（MRU）色板，最多 8 个 ----

    // 初始预置色板（后续随选择动态更新：新颜色插到最前，挤掉最后一个）
    public const int MaxRecentColors = 8;

    public static readonly string[] DefaultColors =
    {
        "#FFF7A900", "#FFFFF275", "#FFA0E7A0", "#FF9AD0EC",
        "#FFE6A8E0", "#FFFFB3A7", "#FFD6C8FF", "#FFC0C0C0"
    };

    // 取当前颜色列表（设置缺失/旧数据时回退到初始色板）
    public static List<string> GetRecentColors(AppSettings settings)
        => settings.RecentColors is { Count: > 0 }
            ? settings.RecentColors
            : new List<string>(DefaultColors);

    // 记录一次选色：已有颜色移到最前；新颜色插入最前并挤出最后一个（保持最多 8 个）
    public static void AddRecentColor(AppSettings settings, string hex)
    {
        var list = GetRecentColors(settings);
        list.Remove(hex);
        list.Insert(0, hex);
        while (list.Count > MaxRecentColors)
            list.RemoveAt(list.Count - 1);
        settings.RecentColors = list;
        settings.Save();
    }

    // 构建整个色板：✎ 自定义按钮 + MRU 颜色列表。
    // 点击颜色或取色器选色后，自动更新 MRU 并调用 onPicked(hex)。
    public static void BuildColorSwatches(
        WrapPanel panel, AppSettings settings, string current, Action<string> onPicked)
    {
        panel.Children.Clear();
        var colors = GetRecentColors(settings);

        // 自定义颜色按钮（✎）：当前颜色不在色板列表中时，显示该色并高亮为选中态
        var isCustom = current != null && !colors.Contains(current) && TryParseColor(current, out var cc);
        var custom = new Border
        {
            Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(isCustom ? cc : Colors.White),
            BorderThickness = new Thickness(isCustom ? 3 : 1),
            BorderBrush = new SolidColorBrush(isCustom ? Colors.Black : Colors.Gray),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        custom.Child = new TextBlock
        {
            Text = "✎", FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = new SolidColorBrush(isCustom ? GetContrastColor(cc) : Colors.Gray)
        };
        custom.MouseLeftButtonDown += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.ColorDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var hex = "#" + dlg.Color.ToArgb().ToString("X8");
                AddRecentColor(settings, hex);
                onPicked(hex);
            }
        };
        panel.Children.Add(custom);

        foreach (var hex in colors)
        {
            var swatch = new Border
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ParseColor(hex, Colors.White)),
                BorderThickness = new Thickness(hex == current ? 3 : 1),
                BorderBrush = new SolidColorBrush(hex == current ? Colors.Black : Colors.Gray),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex
            };
            var captured = hex;
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                AddRecentColor(settings, captured);
                onPicked(captured);
            };
            panel.Children.Add(swatch);
        }
    }

    // 为给定的 TextBox 注入与便利贴一致的细滚动条主题（滑块=文字色，轨道=背景色，宽5，方向正确）
    // 颜色通过 DynamicResource 引用 tb.Resources 中的 ScrollThumbBrush / ScrollThumbHoverBrush / ScrollTrackBrush
    public static void ApplyScrollBarTheme(System.Windows.Controls.TextBox tb, string textColorHex, string bgColorHex)
    {
        try
        {
            var textColor = ParseColor(textColorHex, Colors.Black);
            var bgColor = ParseColor(bgColorHex, Colors.White);

            tb.Resources["ScrollThumbBrush"] = new SolidColorBrush(textColor);
            tb.Resources["ScrollThumbHoverBrush"] = new SolidColorBrush(textColor);
            tb.Resources["ScrollTrackBrush"] = new SolidColorBrush(bgColor);

            var template = (ControlTemplate)tb.FindResource("NoteTextBoxTemplate");
            if (!ReferenceEquals(tb.Template, template)) tb.Template = template;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ApplyScrollBarTheme failed: " + ex.Message);
        }
    }
}
