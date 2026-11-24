using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StickyNoteWPF;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

internal static class NoteListChecks
{
    public static void Run(Action<Window, string> render)
    {
        Toggle("", 0, 0, NoteListKind.Ordered, "1. ");
        Toggle("甲", 0, 0, NoteListKind.Unordered, "• 甲");
        Toggle("甲\r\n乙\r\n丙", 0, 7, NoteListKind.Ordered, "1. 甲\r\n2. 乙\r\n3. 丙");
        Toggle("1. 甲\r\n2. 乙", 0, 10, NoteListKind.Ordered, "甲\r\n乙");
        Toggle("1. 甲\r\n• 乙", 0, 9, NoteListKind.Ordered, "1. 甲\r\n2. 乙");
        Toggle("  甲\n  乙", 0, 7, NoteListKind.Unordered, "  • 甲\n  • 乙");
        Toggle("甲\r\n乙", 0, 3, NoteListKind.Unordered, "• 甲\r\n乙");
        Toggle("1. 甲\r\n乙", 6, 0, NoteListKind.Ordered, "1. 甲\r\n2. 乙");
        Toggle("- 甲", 0, 0, NoteListKind.Unordered, "甲");
        Toggle("• 甲\n2) 乙", 0, 8, NoteListKind.None, "甲\n乙");
        Toggle("1.23 数据", 0, 0, NoteListKind.Ordered, "1. 1.23 数据");
        Continue("1. 甲", 4, 0, "1. 甲\r\n2. ");
        Continue("1. 甲\n2. 乙", 4, 0, "1. 甲\n2. \n3. 乙");
        Continue("1. 甲\r\n2. 乙\r\n3. 丙\r\n普通文字", 4, 0, "1. 甲\r\n2. \r\n3. 乙\r\n4. 丙\r\n普通文字");
        Continue("1. abcd", 5, 0, "1. ab\r\n2. cd");
        Continue("1. abc", 4, 1, "1. a\r\n2. c");
        Continue("• 甲", 3, 0, "• 甲\r\n• ");
        Continue("  - 甲", 5, 0, "  - 甲\r\n  - ");
        Continue("3) 甲", 4, 0, "3) 甲\r\n4) ");
        Continue("1. ", 3, 0, "");
        Continue("  •   ", 6, 0, "  ");
        Continue("1. 甲\r\n2. ", 9, 0, "1. 甲\r\n");
        if (NoteListEditor.Continue("普通文字", 2, 0) != null || NoteListEditor.Continue("1. 甲\n乙", 3, 3) != null)
            throw new InvalidOperationException("普通段落或跨段选区应保留原始回车行为。");
        var syncText = "1. 中文 😀\r\n2. 第二项\r\n• 随手记录";
        var sync = StickyNoteWPF.Sync.SnapshotTools.ForSync(new() { Notes = new() { new() { Text = syncText } } });
        if (sync.Notes[0].Text != syncText || StickyNoteWPF.Sync.SnapshotTools.Validate(sync) != null)
            throw new InvalidOperationException("列表正文必须兼容现有同步格式。");
        Console.WriteLine("PASS: list conversion, mixed/indented lines, CRLF/LF, continuation, renumbering and sync compatibility.");

        var model = new StickyNoteModel { Title = "正文列表", Width = 300, Height = 360, Color = "#FFFFE8B4" };
        var window = new StickyNoteWindow(model) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.Show();
        try
        {
            var editor = (TextBox)window.FindName("NoteTextBox");
            var button = (Button)window.FindName("ListButton");
            editor.IsUndoEnabled = false; editor.Text = "甲\r\n乙"; editor.IsUndoEnabled = true; editor.SelectAll();
            Menu(NoteListKind.Ordered);
            Equal(editor.Text, "1. 甲\r\n2. 乙"); Equal(model.Text, editor.Text);
            editor.Undo(); Equal(editor.Text, "甲\r\n乙");
            editor.Redo(); Equal(editor.Text, "1. 甲\r\n2. 乙");
            editor.SelectAll(); Menu(NoteListKind.Unordered); Equal(editor.Text, "• 甲\r\n• 乙");
            editor.Select(editor.Text.Length, 0);
            Enter(); Equal(editor.Text, "• 甲\r\n• 乙\r\n• ");
            Enter(); Equal(editor.Text, "• 甲\r\n• 乙\r\n");
            editor.Undo(); Equal(editor.Text, "• 甲\r\n• 乙\r\n• ");
            var before = editor.Text;
            model.IsLocked = true; window.RefreshFromModel();
            Menu(NoteListKind.Ordered); Equal(editor.Text, before);
            if (button.IsEnabled || !editor.IsReadOnly) throw new InvalidOperationException("锁定状态不能修改列表。");
            model.IsLocked = false; window.RefreshFromModel();
            editor.Text = "今日安排\r\n1. 整理资料\r\n2. 完成功能\r\n\r\n随手记录\r\n• 一个想法\r\n• 一点提醒";
            editor.Select(editor.Text.IndexOf("整理", StringComparison.Ordinal), 0);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!button.ContextMenu.IsOpen || !button.ContextMenu.Items.Cast<MenuItem>().Single(item => (string)item.Tag == "Ordered").IsChecked)
                throw new InvalidOperationException("列表菜单应显示光标所在段落的状态。");
            button.ContextMenu.IsOpen = false;
            render(window, "note-lists");
            window.Width = window.MinWidth;
            render(window, "note-lists-small");
            var cloned = System.Text.Json.JsonSerializer.Deserialize<StickyNoteModel>(System.Text.Json.JsonSerializer.Serialize(model))!;
            var reopened = new StickyNoteWindow(cloned);
            try { Equal(((TextBox)reopened.FindName("NoteTextBox")).Text, model.Text); }
            finally { reopened.Close(); }
            Console.WriteLine("PASS: list menu, model updates, undo/redo, Enter/exit, lock protection and reopening.");

            void Menu(NoteListKind kind) => button.ContextMenu.Items.Cast<MenuItem>().Single(item => (string)item.Tag == kind.ToString()).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            void Enter()
            {
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                editor.RaiseEvent(key);
                if (!key.Handled) throw new InvalidOperationException("列表内回车应由列表逻辑接管。");
            }
        }
        finally { window.Close(); }
    }

    private static void Toggle(string text, int start, int length, NoteListKind kind, string expected) => Equal(Apply(text, NoteListEditor.Toggle(text, start, length, kind)), expected);
    private static void Continue(string text, int start, int length, string expected) => Equal(Apply(text, NoteListEditor.Continue(text, start, length) ?? throw new InvalidOperationException("未识别列表。")), expected);
    private static string Apply(string text, NoteTextEdit edit)
    {
        var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        if (edit.SelectionStart < 0 || edit.SelectionLength < 0 || edit.SelectionStart + edit.SelectionLength > result.Length)
            throw new InvalidOperationException("列表修改后的选区越界。");
        return result;
    }
    private static void Equal(string actual, string expected)
    {
        if (actual != expected) throw new InvalidOperationException($"列表结果不正确：{System.Text.Json.JsonSerializer.Serialize(actual)}，预期 {System.Text.Json.JsonSerializer.Serialize(expected)}。");
    }
}
