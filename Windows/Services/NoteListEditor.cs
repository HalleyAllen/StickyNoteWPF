using System.Globalization;
using System.Text;

namespace StickyNoteWPF.Services;

public enum NoteListKind { None, Ordered, Unordered }

public readonly record struct NoteTextEdit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

// 列表标记作为普通正文保存，旧数据、搜索、备份和跨设备同步无需迁移。
public static class NoteListEditor
{
    private readonly record struct Line(int Start, int End, int BreakLength);
    private readonly record struct Prefix(int Indent, int Content, NoteListKind Kind, int Number, char Symbol);

    public static NoteListKind GetKind(string text, int start, int length)
    {
        var lines = SelectedLines(text, start, length);
        var kind = Read(text, lines[0]).Kind;
        return lines.All(line => Read(text, line).Kind == kind) ? kind : NoteListKind.None;
    }

    public static NoteTextEdit Toggle(string text, int start, int length, NoteListKind requested)
    {
        var lines = SelectedLines(text, start, length);
        var kind = requested != NoteListKind.None && lines.All(line => Read(text, line).Kind == requested)
            ? NoteListKind.None : requested;
        var first = lines[0];
        var number = 1;
        if (kind == NoteListKind.Ordered && first.Start > 0)
        {
            var previous = At(text, PreviousLineStart(text, first.Start));
            var prefix = Read(text, previous);
            if (prefix.Kind == NoteListKind.Ordered && prefix.Indent == Read(text, first).Indent) number = prefix.Number + 1;
        }
        var builder = new StringBuilder();
        var mappedStart = start;
        var mappedEnd = start + length;
        foreach (var line in lines)
        {
            var prefix = Read(text, line);
            var marker = kind switch
            {
                NoteListKind.Ordered => (number++).ToString(CultureInfo.InvariantCulture) + ". ",
                NoteListKind.Unordered => "• ",
                _ => string.Empty
            };
            var newStart = first.Start + builder.Length;
            if (start >= line.Start && start <= line.End) mappedStart = Map(start);
            if (start + length >= line.Start && start + length <= line.End) mappedEnd = Map(start + length);
            builder.Append(text, line.Start, prefix.Indent);
            builder.Append(marker);
            builder.Append(text, line.Start + prefix.Content, line.End - line.Start - prefix.Content);
            if (line != lines[^1]) builder.Append(text, line.End, line.BreakLength);

            int Map(int position)
            {
                var relative = position - line.Start;
                return newStart + (relative < prefix.Indent ? relative : prefix.Indent + marker.Length + Math.Max(0, relative - prefix.Content));
            }
        }
        // 选区恰好结束在下一行开头时，不处理该行，但保留选中的换行。
        var end = lines[^1].End;
        if (start + length > end)
        {
            builder.Append(text, end, start + length - end);
            end = start + length;
            mappedEnd = first.Start + builder.Length;
        }
        return new NoteTextEdit(first.Start, end - first.Start, builder.ToString(), mappedStart, Math.Max(0, mappedEnd - mappedStart));
    }

    public static NoteTextEdit? Continue(string text, int start, int length)
    {
        ValidateSelection(text, start, length);
        var line = At(text, LineStart(text, start));
        var prefix = Read(text, line);
        if (prefix.Kind == NoteListKind.None || start < line.Start + prefix.Content || start + length > line.End) return null;
        if (length == 0 && string.IsNullOrWhiteSpace(text[(line.Start + prefix.Content)..line.End]))
            return new NoteTextEdit(line.Start + prefix.Indent, line.End - line.Start - prefix.Indent, string.Empty, line.Start + prefix.Indent, 0);

        var newline = line.BreakLength > 0 ? text.Substring(line.End, line.BreakLength) : Newline(text);
        var indent = text.Substring(line.Start, prefix.Indent);
        var number = prefix.Number + 1;
        var marker = prefix.Kind == NoteListKind.Ordered ? number.ToString(CultureInfo.InvariantCulture) + prefix.Symbol + " " : prefix.Symbol + " ";
        var builder = new StringBuilder(newline + indent + marker);
        var caret = start + builder.Length;
        var end = line.End;
        builder.Append(text, start + length, line.End - start - length);
        // 在有序列表中间回车时，顺延后面的同级条目，避免出现两个“2.”。
        while (prefix.Kind == NoteListKind.Ordered && line.BreakLength > 0 && line.End + line.BreakLength < text.Length)
        {
            var next = At(text, line.End + line.BreakLength);
            var nextPrefix = Read(text, next);
            if (nextPrefix.Kind != NoteListKind.Ordered || nextPrefix.Indent != prefix.Indent) break;
            builder.Append(text, line.End, line.BreakLength);
            builder.Append(text, next.Start, nextPrefix.Indent);
            builder.Append((++number).ToString(CultureInfo.InvariantCulture)).Append(nextPrefix.Symbol).Append(' ');
            builder.Append(text, next.Start + nextPrefix.Content, next.End - next.Start - nextPrefix.Content);
            line = next;
            end = next.End;
        }
        return new NoteTextEdit(start, end - start, builder.ToString(), caret, 0);
    }

    private static List<Line> SelectedLines(string text, int start, int length)
    {
        ValidateSelection(text, start, length);
        var lines = new List<Line>();
        var position = LineStart(text, start);
        var last = length == 0 ? start : start + length - 1;
        do
        {
            var line = At(text, position);
            lines.Add(line);
            position = line.End + line.BreakLength;
            if (line.BreakLength == 0) break;
        } while (position <= last);
        return lines;
    }
    private static Line At(string text, int start)
    {
        var end = start;
        while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
        var lineBreak = end == text.Length ? 0 : text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
        return new Line(start, end, lineBreak);
    }
    private static int LineStart(string text, int position)
    {
        // CRLF 中间的光标位置也属于上一段。
        if (position > 0 && position < text.Length && text[position - 1] == '\r' && text[position] == '\n') position--;
        while (position > 0 && text[position - 1] is not ('\r' or '\n')) position--;
        return position;
    }
    private static int PreviousLineStart(string text, int start)
    {
        var position = start - 1;
        if (position > 0 && text[position] == '\n' && text[position - 1] == '\r') position--;
        return LineStart(text, position);
    }
    private static Prefix Read(string text, Line line)
    {
        var span = text.AsSpan(line.Start, line.End - line.Start);
        var indent = 0;
        while (indent < span.Length && span[indent] is ' ' or '\t') indent++;
        var end = indent;
        while (end < span.Length && span[end] is >= '0' and <= '9') end++;
        if (end > indent && end - indent <= 9 && end < span.Length && span[end] is '.' or ')' or '、'
            && end + 1 < span.Length && span[end + 1] is ' ' or '\t'
            && int.TryParse(span[indent..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            var symbol = span[end++];
            while (end < span.Length && span[end] is ' ' or '\t') end++;
            return new Prefix(indent, end, NoteListKind.Ordered, number, symbol);
        }
        if (indent + 1 < span.Length && span[indent] is '•' or '-' or '*' && span[indent + 1] is ' ' or '\t')
        {
            end = indent + 1;
            while (end < span.Length && span[end] is ' ' or '\t') end++;
            return new Prefix(indent, end, NoteListKind.Unordered, 0, span[indent]);
        }
        return new Prefix(indent, indent, NoteListKind.None, 0, '\0');
    }
    private static string Newline(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? Environment.NewLine : text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : text[index].ToString();
    }
    private static void ValidateSelection(string text, int start, int length)
    {
        if (start < 0 || length < 0 || start > text.Length || length > text.Length - start) throw new ArgumentOutOfRangeException(nameof(start));
    }
}
