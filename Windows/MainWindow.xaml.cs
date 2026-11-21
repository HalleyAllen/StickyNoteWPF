using System.Linq;
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
    // ===== 便利贴 / 任务清单 各自独立的浏览状态（与「默认外观」一致，互不影响）=====
    private bool _rowViewNotes;                 // 便利贴页视图：false=卡片, true=列表
    private bool _rowViewTasks;                 // 任务清单页视图：false=卡片, true=列表
    private string _searchNoteText = "";        // 便利贴页搜索关键字
    private string _searchTaskText = "";        // 任务清单页搜索关键字
    private string _sortNoteTag = "none";       // 便利贴页排序 none | titleAsc | titleDesc
    private string _sortTaskTag = "none";       // 任务清单页排序 none | titleAsc | titleDesc

    // 当前标签页对应读取的那一套状态
    private bool CurrentRowView => _showTasks ? _rowViewTasks : _rowViewNotes;
    private string CurrentSearchText => _showTasks ? _searchTaskText : _searchNoteText;
    private string CurrentSortTag => _showTasks ? _sortTaskTag : _sortNoteTag;

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
        DefaultAppearanceButton.Click += (_, _) => App.Current.OpenDefaultAppearance(_showTasks);
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
        ApplyCurrentView();             // 应用该类目记忆的卡片/列表视图
        SearchBox.Text = CurrentSearchText;  // 搜索框恢复为该类目自己的关键字
        UpdateSortCheck();              // 排序菜单勾选为该类目自己的排序方式
        RefreshLists();                 // 兜底刷新（SearchBox 文字未变化时不会触发 TextChanged）
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
        DefaultAppearanceButton.ToolTip = _showTasks
            ? "默认外观：设置新建任务清单的默认样式（不影响便利贴与已创建的任务清单）"
            : "默认外观：设置新建便利贴的默认样式（不影响任务清单与已创建的便利贴）";
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: StickyNoteModel note })
        {
            Logger.Log($"主窗口点击删除便签：标题=\"{note.Title}\" Id={note.Id}");
            App.Current.DeleteNote(note);
        }
        else
        {
            Logger.Log($"主窗口删除便签按钮点击但未识别数据上下文（sender 类型={sender?.GetType().Name}）");
        }
    }

    private void DeleteTaskListButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: TaskListModel list })
        {
            Logger.Log($"主窗口点击删除任务清单：标题=\"{list.Title}\" Id={list.Id}");
            App.Current.DeleteTaskList(list);
        }
        else
        {
            Logger.Log($"主窗口删除清单按钮点击但未识别数据上下文（sender 类型={sender?.GetType().Name}）");
        }
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
        if (_showTasks)
        {
            var kw = _searchTaskText.Trim();
            bool totalEmpty = App.Current.TaskLists.Count == 0;
            var view = FilterLists(App.Current.TaskLists, kw).ToList();
            TaskListBox.ItemsSource = view;

            EmptyHint.Visibility = view.Any() ? Visibility.Collapsed : Visibility.Visible;
            EmptyHint.Text = totalEmpty
                ? "还没有任务清单，点击右上角「＋ 新建」创建第一个吧。"
                : "没有找到匹配的任务清单";
        }
        else
        {
            var kw = _searchNoteText.Trim();
            bool totalEmpty = App.Current.Notes.Count == 0;
            // 物化为新集合再赋给 ItemsSource：若直接返回源 List（无搜索/默认排序时引用不变），
            // WPF 会认为 ItemsSource 没变而不重建列表，导致增删后界面不刷新（需切视图/重启才生效）。
            var view = FilterNotes(App.Current.Notes, kw).ToList();
            NoteList.ItemsSource = view;

            EmptyHint.Visibility = view.Any() ? Visibility.Collapsed : Visibility.Visible;
            EmptyHint.Text = totalEmpty
                ? "还没有便利贴，点击右上角「＋ 新建」创建第一张吧。"
                : "没有找到匹配的便利贴";
        }
    }

    // ====== 搜索 / 排序 ======

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 只写入当前标签页的搜索关键字，切换标签后另一类目不会被此关键字过滤
        if (_showTasks) _searchTaskText = SearchBox.Text;
        else _searchNoteText = SearchBox.Text;
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
            // 只修改当前标签页的排序方式，两类目互不影响
            if (_showTasks) _sortTaskTag = tag;
            else _sortNoteTag = tag;
            RefreshLists();
            UpdateSortCheck();
        }
    }

    private void UpdateSortCheck()
    {
        if (SortButton.ContextMenu is not System.Windows.Controls.ContextMenu menu) return;
        foreach (var item in menu.Items)
            if (item is System.Windows.Controls.MenuItem mi)
                mi.IsChecked = string.Equals(mi.Tag as string, CurrentSortTag, StringComparison.Ordinal);
    }

    private IEnumerable<StickyNoteModel> FilterNotes(IEnumerable<StickyNoteModel> source, string kw)
    {
        IEnumerable<StickyNoteModel> q = source;
        if (!string.IsNullOrEmpty(kw))
            q = q.Where(n => ContainsIgnoreCase(n.Title, kw) || ContainsIgnoreCase(n.Text, kw));
        return ApplySort(q, n => n.Title, _sortNoteTag);
    }

    private IEnumerable<TaskListModel> FilterLists(IEnumerable<TaskListModel> source, string kw)
    {
        IEnumerable<TaskListModel> q = source;
        if (!string.IsNullOrEmpty(kw))
            q = q.Where(l => TaskMatches(l, kw));
        return ApplySort(q, l => l.Title, _sortTaskTag);
    }

    private IEnumerable<T> ApplySort<T>(IEnumerable<T> q, Func<T, string?> key, string sortTag)
        => sortTag switch
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

    // 只修改当前标签页的视图模式，两类目各自的卡片/列表选择互不影响
    private void SetViewMode(bool rowView)
    {
        if (CurrentRowView == rowView) return;
        if (_showTasks) _rowViewTasks = rowView;
        else _rowViewNotes = rowView;
        ApplyCurrentView();
    }

    // 把当前标签页记忆的视图模式应用到当前可见的列表上
    private void ApplyCurrentView()
    {
        bool row = CurrentRowView;
        if (_showTasks)
        {
            TaskListBox.ItemTemplate = (DataTemplate)FindResource(row ? "TaskRowTemplate" : "TaskCardTemplate");
            TaskListBox.ItemsPanel = (ItemsPanelTemplate)FindResource(row ? "RowViewPanel" : "CardViewPanel");
        }
        else
        {
            NoteList.ItemTemplate = (DataTemplate)FindResource(row ? "NoteRowTemplate" : "NoteCardTemplate");
            NoteList.ItemsPanel = (ItemsPanelTemplate)FindResource(row ? "RowViewPanel" : "CardViewPanel");
        }
        UpdateViewButtons();
    }

    private void UpdateViewButtons()
    {
        var activeBg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC));
        var activeFg = System.Windows.Media.Brushes.White;
        var idleBg = System.Windows.Media.Brushes.White;
        var idleFg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));

        bool row = CurrentRowView;
        CardViewButton.Background = row ? idleBg : activeBg;
        CardViewButton.Foreground = row ? idleFg : activeFg;
        RowViewButton.Background = row ? activeBg : idleBg;
        RowViewButton.Foreground = row ? activeFg : idleFg;
    }
}
