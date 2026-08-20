using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class TaskListWindow : Window
{
    public TaskListModel List { get; }

    public TaskListWindow(TaskListModel list)
    {
        InitializeComponent();
        List = list;

        Left = double.IsNaN(list.Left) ? 200 : list.Left;
        Top = double.IsNaN(list.Top) ? 150 : list.Top;
        Width = 320;
        Height = 360;

        var view = CollectionViewSource.GetDefaultView(list.Items);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.IsDone), ListSortDirection.Ascending));
        // 未完成区内按 Order 降序，序号最大（最新）的置顶，保证新任务始终在最顶
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Order), ListSortDirection.Descending));
        TaskList.ItemsSource = view;
        EnsureSubSorting(list.Items);

        RefreshFromModel();
        ApplyTitle();

        TitleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        CloseButton.Click += (_, _) => RequestClose();
        SettingsButton.Click += (_, _) => App.Current.OpenTaskListSettings(this);
        EyeButton.Click += (_, _) => ToggleHover();
        LockButton.Click += (_, _) => ToggleLock();

        ApplyHoverState();
    }

    // 眼睛按钮：切换是否启用“鼠标移入才显示”
    private void ToggleHover()
    {
        List.HoverToShow = !List.HoverToShow;
        Persist();
        ApplyHoverState();
    }

    private void ToggleLock()
    {
        List.IsLocked = !List.IsLocked;
        Persist();
        ApplyLockState();
    }

    private void ApplyLockState()
    {
        LockButton.Content = List.IsLocked ? "🔒" : "🔓";
        LockButton.ToolTip = List.IsLocked ? "已锁定（点此解锁，解锁后可编辑）" : "未锁定（点此锁定，锁定后不可编辑）";

        // 锁定：仅关闭编辑（文本框只读）、隐藏删除与添加按钮；勾选仍可用
        AddTaskButton.Visibility = List.IsLocked ? Visibility.Collapsed : Visibility.Visible;
        ApplyLockToItems();
    }

    private void ApplyLockToItems()
    {
        // 删除按钮可见性已改为 XAML 绑定 List.IsLocked（见 TaskListWindow.xaml）
        // 此处保留方法以便锁定状态切换时统一刷新，无需再手动设置
    }

    private void ApplyHoverState()
    {
        EyeButton.Content = List.HoverToShow ? "👁" : "🚫";
        EyeButton.ToolTip = List.HoverToShow ? "鼠标移入才显示（点此关闭）" : "始终显示（点此开启悬停）";

        if (App.Current?.Settings.ForceShowAll == true)
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
            return;
        }

        if (List.HoverToShow)
        {
            if (_hoverTimer == null)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                t.Tick += HoverTick;
                t.Start();
                _hoverTimer = t;
            }
            HideContent();
        }
        else
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
        }
    }

    public void ApplyForceShowAll()
    {
        if (App.Current?.Settings.ForceShowAll == true)
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
        }
        else
        {
            ApplyHoverState();
        }
    }

    private DispatcherTimer? _hoverTimer;

    private void HoverTick(object? sender, EventArgs e)
    {
        try
        {
            var p = PointFromScreen(new System.Windows.Point(
                System.Windows.Forms.Cursor.Position.X,
                System.Windows.Forms.Cursor.Position.Y));
            if (p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight)
                ShowContent();
            else
                HideContent();
        }
        catch { }
    }

    private void ShowContent()
    {
        Opacity = 1;
        IsHitTestVisible = true;
        Topmost = App.Current?.Settings.GlobalTopmost ?? true;
    }

    private void HideContent()
    {
        Opacity = 0;
        IsHitTestVisible = false;
        Topmost = false;
    }

    public void RefreshFromModel()
    {
        ApplyBackground();
        ApplyTextStyle();
        ApplyLockState();
        ApplyTitle();
    }

    public void ApplyTitle()
    {
        TitleText.Text = string.IsNullOrWhiteSpace(List.Title) ? "任务清单" : List.Title;
    }

    public void ApplyOpacity(double opacity)
    {
        List.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        ApplyBackground();
        Persist();
    }

    public void ApplyBackground()
    {
        var o = Math.Clamp(List.Opacity, 0.0, 1.0);
        try
        {
            if (!string.IsNullOrEmpty(List.BackgroundImagePath) && File.Exists(List.BackgroundImagePath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(List.BackgroundImagePath, UriKind.Absolute);
                bmp.EndInit();

                var stretch = List.BackgroundImageMode switch
                {
                    "Center" => System.Windows.Media.Stretch.None,
                    "Tile" => System.Windows.Media.Stretch.None,
                    "Stretch" => System.Windows.Media.Stretch.Fill,
                    _ => System.Windows.Media.Stretch.UniformToFill
                };

                var brush = new System.Windows.Media.ImageBrush(bmp)
                {
                    Stretch = stretch,
                    Opacity = o
                };
                if (List.BackgroundImageMode == "Tile")
                {
                    brush.TileMode = System.Windows.Media.TileMode.Tile;
                    brush.Viewport = new Rect(0, 0, 0.25, 0.25);
                    brush.ViewportUnits = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox;
                }
                else if (List.BackgroundImageMode == "Center")
                {
                    brush.AlignmentX = System.Windows.Media.AlignmentX.Center;
                    brush.AlignmentY = System.Windows.Media.AlignmentY.Center;
                }
                RootBorder.Background = brush;
            }
            else
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(List.Color);
                c.A = (byte)(o * 255);
                RootBorder.Background = new System.Windows.Media.SolidColorBrush(c);
            }

            RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x55), 0, 0, 0));
            TitleBar.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x22), 0, 0, 0));
        }
        catch
        {
            RootBorder.Background = new SolidColorBrush(Colors.White);
        }

        RootBorder.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 12,
            ShadowDepth = 3,
            Opacity = 0.3
        };
    }

    public void ApplyTextStyle()
    {
        TitleText.Foreground = AppearanceHelper.MakeBrush(List.TextColor);
        SettingsButton.Foreground = AppearanceHelper.MakeBrush(List.TextColor);
        CloseButton.Foreground = AppearanceHelper.MakeBrush(List.TextColor);
        EyeButton.Foreground = AppearanceHelper.MakeBrush(List.TextColor);
        LockButton.Foreground = AppearanceHelper.MakeBrush(List.TextColor);

        // 底部“添加任务”按钮颜色跟随文字色
        AddTaskButton.Foreground = AppearanceHelper.MakeBrush(List.TextColor);

        // 任务文本与滚动条主题（ListBox 内 TextBox/删除按钮 的文字色改用 XAML 绑定，避免刷新丢失）
        var textColor = AppearanceHelper.ParseColor(List.TextColor, Colors.Black);
        var bgColor = AppearanceHelper.ParseColor(List.Color, Colors.White);
        TaskList.Resources["ScrollThumbBrush"] = new SolidColorBrush(textColor);
        TaskList.Resources["ScrollThumbHoverBrush"] = new SolidColorBrush(textColor);
        TaskList.Resources["ScrollTrackBrush"] = new SolidColorBrush(bgColor);
    }

    private void Task_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // 刷新所在层级（根或子任务集合）的排序视图，完成项沉底、未完成项保持置顶
        var ic = (sender as DependencyObject)?.FindAncestor<ItemsControl>();
        if (ic is System.Windows.Controls.ListBox)
        {
            CollectionViewSource.GetDefaultView(List.Items).Refresh();
        }
        else if (ic?.ItemsSource is System.Collections.IEnumerable src)
        {
            CollectionViewSource.GetDefaultView(src)?.Refresh();
        }
        Persist();
    }

    private void TaskText_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Enter 提交当前任务并新建一条同级任务
            ((System.Windows.Controls.TextBox)sender).GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            var ic = (sender as DependencyObject)?.FindAncestor<ItemsControl>();
            if (ic is System.Windows.Controls.ListBox)
                AddTask();
            else if (ic?.DataContext is TaskItem parent)
                AddSubTask(parent);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Tab 提交当前任务并在其下添加子任务（Shift+Tab 保留默认反向焦点移动）
            ((System.Windows.Controls.TextBox)sender).GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            if ((sender as FrameworkElement)?.DataContext is TaskItem item)
                AddSubTask(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            // Shift+Enter 允许换行（交由 TextBox 默认行为）
        }
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskItem item) return;
        var ic = (sender as DependencyObject)?.FindAncestor<ItemsControl>();
        if (ic is System.Windows.Controls.ListBox)
        {
            List.Items.Remove(item);
        }
        else if (ic?.DataContext is TaskItem parent)
        {
            parent.SubItems.Remove(item);
            parent.RefreshHasSubItems();
        }
        Persist();
    }

    private void AddTask_Click(object sender, RoutedEventArgs e) => AddTask();

    private void AddTask()
    {
        var newItem = new TaskItem { Text = "新任务" };
        // Order 取当前最大+1，配合排序（IsDone 升序 + Order 降序）保证新任务置顶
        long maxOrder = List.Items.Count == 0 ? 0 : List.Items.Max(i => i.Order);
        newItem.Order = maxOrder + 1;
        List.Items.Add(newItem);
        Persist();
        FocusItemTextBox(newItem);
    }

    // 展开/折叠子任务
    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskItem item)
            item.IsExpanded = !item.IsExpanded;
    }

    // 滚动条轨道点击：默认按整页跳，改为按行小步滚动。
    // 用 Preview（隧道）事件在默认行为之前拦截，点击 Thumb 上方/下方空白处时手动滚动一行。
    private void Track_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Track track) return;
        var thumb = track.Thumb;
        if (thumb is null) return;

        var pos = e.GetPosition(track);
        var thumbTop = thumb.TranslatePoint(new System.Windows.Point(0, 0), track).Y;
        var thumbBottom = thumbTop + thumb.ActualHeight;
        if (pos.Y >= thumbTop && pos.Y <= thumbBottom) return; // 点在 Thumb 上，交给默认拖拽

        var scrollViewer = track.FindAncestor<ScrollViewer>();
        if (scrollViewer is null) return;

        e.Handled = true; // 阻止默认的整页跳转
        if (pos.Y < thumbTop)
            scrollViewer.LineUp();
        else
            scrollViewer.LineDown();
    }

    // 添加子任务
    private void AddSubTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskItem parent)
            AddSubTask(parent);
    }

    private void AddSubTask(TaskItem parent)
    {
        long maxOrder = parent.SubItems.Count == 0 ? 0 : parent.SubItems.Max(i => i.Order);
        var sub = new TaskItem { Text = "新子任务", Order = maxOrder + 1 };
        parent.SubItems.Add(sub);
        parent.IsExpanded = true;
        parent.RefreshHasSubItems();
        EnsureSubSorting(parent.SubItems);
        Persist();
        FocusItemTextBox(sub);
    }

    // 聚焦到指定任务项的文本框（可用于任意层级）
    private void FocusItemTextBox(TaskItem item)
    {
        TaskList.Dispatcher.BeginInvoke(new Action(() =>
        {
            var tb = FindTextBoxForItem(item);
            if (tb == null)
            {
                // 容器可能尚未生成，再等一轮
                TaskList.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (FindTextBoxForItem(item) is System.Windows.Controls.TextBox tb2) { tb2.Focus(); tb2.SelectAll(); }
                }), DispatcherPriority.Loaded);
                return;
            }
            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Loaded);
    }

    private System.Windows.Controls.TextBox? FindTextBoxForItem(TaskItem item)
        => FindTextBoxRecursive(TaskList, item);

    private static System.Windows.Controls.TextBox? FindTextBoxRecursive(DependencyObject parent, TaskItem item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.TextBox tb && tb.DataContext == item) return tb;
            var found = FindTextBoxRecursive(child, item);
            if (found != null) return found;
        }
        return null;
    }

    // 为子任务集合设置与根级一致的排序（IsDone 升序 + Order 降序），幂等可重复调用
    private static void EnsureSubSorting(ObservableCollection<TaskItem> items)
    {
        var view = CollectionViewSource.GetDefaultView(items);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.IsDone), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.Order), ListSortDirection.Descending));
        foreach (var item in items)
            if (item.SubItems.Count > 0)
                EnsureSubSorting(item.SubItems);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        List.Left = Left;
        List.Top = Top;
        Persist();
        base.OnLocationChanged(e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        Persist();
        base.OnRenderSizeChanged(sizeInfo);
    }

    public void RequestClose()
    {
        ClosedByUser?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ClosedByUser;

    private void Persist()
    {
        App.Current?.SaveAll();
    }
}
