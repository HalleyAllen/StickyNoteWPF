using System.Globalization;
using System.Windows.Data;

namespace StickyNoteWPF;

public sealed class NotePreviewConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        if (text.Length <= 512) return text;
        var length = char.IsHighSurrogate(text[511]) ? 511 : 512;
        return text[..length] + "…";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
