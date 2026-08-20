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
        ForceShowButton.Click += ForceShowButton_Click;
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
            UpdateForceShowButton();
            RefreshList();
            // 注册窗口消息钩子，接收其他实例发来的“激活”消息
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);
        };
    }

    // 根据设置更新“全部显示”按钮状态（图标高亮表示已开启）
    private void UpdateForceShowButton()
    {
        var on = App.Current.Settings.ForceShowAll;
        ForceShowButton.Foreground = on
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00))
            : System.Windows.Media.Brushes.White;
        ForceShowButton.ToolTip = on
            ? "全部显示：开启（所有便签已强制不透明且始终显示，点此关闭）"
            : "全部显示：关闭（点此开启，强制所有便签不透明且始终显示）";
    }

    // 切换全局“全部显示”开关
    private void ForceShowButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        s.ForceShowAll = !s.ForceShowAll;
        s.Save();
        App.Current.ApplyForceShowAll();
        UpdateForceShowButton();
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
