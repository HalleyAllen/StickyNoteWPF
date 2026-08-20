using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NewNoteButton.Click += (_, _) => App.Current.CreateNote();
        SettingsButton.Click += (_, _) => App.Current.OpenSettings();
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };
        NoteList.MouseDoubleClick += (_, _) =>
        {
            if (NoteList.SelectedItem is StickyNoteModel note)
                App.Current.OpenNote(note);
        };
        Loaded += (_, _) =>
        {
            RefreshList();
            // 注册窗口消息钩子，接收其他实例发来的“激活”消息
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);
        };
    }

    // 处理跨实例激活消息
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.ActivateMessage)
        {
            App.Current.ActivateFromOtherInstance();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is StickyNoteModel note)
            App.Current.DeleteNote(note);
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box && box.DataContext is StickyNoteModel note)
            App.Current.RefreshNote(note);
    }

    public void RefreshList()
    {
        var notes = App.Current.Notes;
        NoteList.ItemsSource = null;
        NoteList.ItemsSource = notes;
        EmptyHint.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
