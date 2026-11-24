using System.Windows;

namespace StickyNoteWPF.Services;

public static class ThemeManager
{
    // 只替换调色板，已打开的窗口通过 DynamicResource 立即更新，无需重建或丢失输入。
    public static void Apply(bool darkMode)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/StickyNote;component/Themes/{(darkMode ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source?.EndsWith("/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
                || source?.EndsWith("/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[i] = palette;
                return;
            }
        }
        dictionaries.Add(palette);
    }
}
