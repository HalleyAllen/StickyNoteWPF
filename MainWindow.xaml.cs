using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class MainWindow : Window
{
    private bool _showTasks;

    public MainWindow()
    {
        InitializeComponent();
        AddNoteButton.Click += (_, _) =>
        {
            if (_showTasks)
                App.Current.CreateTaskList();
            else
                App.Current.CreateNote();
        };
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
        TaskListBox.MouseDoubleClick += (_, _) =>
        {
            if (TaskListBox.SelectedItem is TaskListModel list)
                App.Current.OpenTaskList(list);
        };
        Loaded += (_, _) =>
        {
            UpdateForceShowButton();
            RefreshLists();
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

    private void ForceShowButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        s.ForceShowAll = !s.ForceShowAll;
        s.Save();
        App.Current.ApplyForceShowAll();
        UpdateForceShowButton();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.ActivateMessage)
        {
            App.Current.ActivateFromOtherInstance();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void TabNotes_Click(object sender, RoutedEventArgs e) => SwitchTab(false);
    private void TabTasks_Click(object sender, RoutedEventArgs e) => SwitchTab(true);

    private void SwitchTab(bool showTasks)
    {
        _showTasks = showTasks;
        NoteList.Visibility = showTasks ? Visibility.Collapsed : Visibility.Visible;
        TaskListBox.Visibility = showTasks ? Visibility.Visible : Visibility.Collapsed;
        AddNoteButton.ToolTip = showTasks ? "新建任务清单" : "新建便利贴";

        UpdateTabIndicator();
        RefreshLists();
    }

    private void UpdateTabIndicator()
    {
        // 指示器横条滑动到当前选中的 Tab 下方
        double tabWidth = (ActualWidth > 0 ? ActualWidth : Width) / 2;
        TabIndicator.Width = tabWidth;
        var tt = TabIndicator.RenderTransform as TranslateTransform
                 ?? new TranslateTransform();
        TabIndicator.RenderTransform = tt;
        var targetLeft = _showTasks ? tabWidth : 0;
        var anim = new DoubleAnimation(tt.X, targetLeft, new Duration(TimeSpan.FromMilliseconds(150)));
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is StickyNoteModel note)
            App.Current.DeleteNote(note);
    }

    private void DeleteTaskListButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is TaskListModel list)
            App.Current.DeleteTaskList(list);
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box && box.DataContext is StickyNoteModel note)
            App.Current.RefreshNote(note);
    }

    private void TaskTitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box && box.DataContext is TaskListModel list)
            App.Current.RefreshTaskList(list);
    }

    public void RefreshLists()
    {
        var notes = App.Current.Notes;
        var lists = App.Current.TaskLists;

        NoteList.ItemsSource = null;
        NoteList.ItemsSource = notes;
        TaskListBox.ItemsSource = null;
        TaskListBox.ItemsSource = lists;

        bool empty = _showTasks ? lists.Count == 0 : notes.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _showTasks
            ? "还没有任务清单，点击右下角「＋」新建一个吧。"
            : "还没有便利贴，点击右下角「＋」新建一个吧。";
    }
}
