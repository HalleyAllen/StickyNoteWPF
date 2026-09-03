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
    private bool _rowView;              // false=卡片视图, true=列表视图
    private string _searchText = "";
    private string _sortTag = "none";   // none | titleAsc | titleDesc

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
            UpdateNavVisuals();
            UpdateViewButtons();
            UpdateSortCheck();
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
        else if (msg == App.ShutdownMessage)
        {
            // 新实例请求接管：正常退出并存盘（释放互斥锁），由新实例启动
            Logger.Log("MainWindow.WndProc: 收到 ShutdownMessage，准备退出存盘");
            App.Current.ExitApp();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void NavNotes_Click(object sender, RoutedEventArgs e) => SwitchTab(false);
    private void NavTasks_Click(object sender, RoutedEventArgs e) => SwitchTab(true);

    private void SwitchTab(bool showTasks)
    {
        _showTasks = showTasks;
        NoteList.Visibility = showTasks ? Visibility.Collapsed : Visibility.Visible;
        TaskListBox.Visibility = showTasks ? Visibility.Visible : Visibility.Collapsed;
        AddNoteButton.ToolTip = showTasks ? "新建任务清单" : "新建便利贴";

        UpdateNavVisuals();
        RefreshLists();
    }

    // 左侧导航选中态：选中项白色加粗 + 左侧蓝色指示条
    private void UpdateNavVisuals()
    {
        var selected = System.Windows.Media.Brushes.White;
        var normal = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA0, 0xA6));
        NavNotes.Foreground = _showTasks ? normal : selected;
        NavNotes.FontWeight = _showTasks ? FontWeights.Normal : FontWeights.SemiBold;
        NavTasks.Foreground = _showTasks ? selected : normal;
        NavTasks.FontWeight = _showTasks ? FontWeights.SemiBold : FontWeights.Normal;
        NavIndicatorNotes.Opacity = _showTasks ? 0 : 1;
        NavIndicatorTasks.Opacity = _showTasks ? 1 : 0;
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
        var kw = _searchText.Trim();
        bool empty = _showTasks
            ? App.Current.TaskLists.Count == 0
            : App.Current.Notes.Count == 0;

        var noteView = FilterNotes(App.Current.Notes, kw);
        var listView = FilterLists(App.Current.TaskLists, kw);

        NoteList.ItemsSource = noteView;
        TaskListBox.ItemsSource = listView;

        bool showHint = _showTasks ? !listView.Any() : !noteView.Any();
        EmptyHint.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
            EmptyHint.Text = _showTasks
                ? "还没有任务清单，点击右上角「＋ 新建」创建第一个吧。"
                : "还没有便利贴，点击右上角「＋ 新建」创建第一张吧。";
        else
            EmptyHint.Text = _showTasks
                ? "没有找到匹配的任务清单"
                : "没有找到匹配的便利贴";
    }

    // ====== 搜索 / 排序 ======

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        RefreshLists();
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is System.Windows.Controls.ContextMenu menu)
        {
            menu.PlacementTarget = SortButton;
            menu.IsOpen = true;
        }
    }

    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string tag)
        {
            _sortTag = tag;
            RefreshLists();
            UpdateSortCheck();
        }
    }

    private void UpdateSortCheck()
    {
        if (SortButton.ContextMenu is not System.Windows.Controls.ContextMenu menu) return;
        foreach (var item in menu.Items)
            if (item is System.Windows.Controls.MenuItem mi)
                mi.IsChecked = string.Equals(mi.Tag as string, _sortTag, StringComparison.Ordinal);
    }

    private IEnumerable<StickyNoteModel> FilterNotes(IEnumerable<StickyNoteModel> source, string kw)
    {
        IEnumerable<StickyNoteModel> q = source;
        if (!string.IsNullOrEmpty(kw))
            q = q.Where(n => ContainsIgnoreCase(n.Title, kw) || ContainsIgnoreCase(n.Text, kw));
        return ApplySort(q, n => n.Title);
    }

    private IEnumerable<TaskListModel> FilterLists(IEnumerable<TaskListModel> source, string kw)
    {
        IEnumerable<TaskListModel> q = source;
        if (!string.IsNullOrEmpty(kw))
            q = q.Where(l => TaskMatches(l, kw));
        return ApplySort(q, l => l.Title);
    }

    private IEnumerable<T> ApplySort<T>(IEnumerable<T> q, Func<T, string?> key)
        => _sortTag switch
        {
            "titleAsc" => q.OrderBy(key, StringComparer.OrdinalIgnoreCase),
            "titleDesc" => q.OrderByDescending(key, StringComparer.OrdinalIgnoreCase),
            _ => q
        };

    private static bool TaskMatches(TaskListModel list, string kw)
        => ContainsIgnoreCase(list.Title, kw) || TaskItemsMatch(list.Items, kw);

    private static bool TaskItemsMatch(IEnumerable<TaskItem> items, string kw)
    {
        foreach (var item in items)
            if (ContainsIgnoreCase(item.Text, kw) || TaskItemsMatch(item.SubItems, kw))
                return true;
        return false;
    }

    private static bool ContainsIgnoreCase(string? text, string kw)
        => !string.IsNullOrEmpty(text) && text.Contains(kw, StringComparison.OrdinalIgnoreCase);

    // ====== 卡片 / 列表视图切换 ======

    private void CardViewButton_Click(object sender, RoutedEventArgs e) => SetViewMode(false);
    private void RowViewButton_Click(object sender, RoutedEventArgs e) => SetViewMode(true);

    private void SetViewMode(bool rowView)
    {
        if (_rowView == rowView) return;
        _rowView = rowView;

        NoteList.ItemTemplate = (DataTemplate)FindResource(rowView ? "NoteRowTemplate" : "NoteCardTemplate");
        TaskListBox.ItemTemplate = (DataTemplate)FindResource(rowView ? "TaskRowTemplate" : "TaskCardTemplate");
        NoteList.ItemsPanel = (ItemsPanelTemplate)FindResource(rowView ? "RowViewPanel" : "CardViewPanel");
        TaskListBox.ItemsPanel = (ItemsPanelTemplate)FindResource(rowView ? "RowViewPanel" : "CardViewPanel");

        UpdateViewButtons();
    }

    private void UpdateViewButtons()
    {
        var activeBg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC));
        var activeFg = System.Windows.Media.Brushes.White;
        var idleBg = System.Windows.Media.Brushes.White;
        var idleFg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));

        CardViewButton.Background = _rowView ? idleBg : activeBg;
        CardViewButton.Foreground = _rowView ? idleFg : activeFg;
        RowViewButton.Background = _rowView ? activeBg : idleBg;
        RowViewButton.Foreground = _rowView ? activeFg : idleFg;
    }
}
