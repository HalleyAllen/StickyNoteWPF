using System;
using System.ComponentModel;
using System.IO;
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
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.IsDone), ListSortDirection.Ascending));
        TaskList.ItemsSource = view;

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
        // 排序由 CollectionView 处理，无需重建容器，因此不会丢失调色/可见性
        CollectionViewSource.GetDefaultView(List.Items).Refresh();
        Persist();
    }

    private void TaskText_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Enter 提交当前任务并新建一条
            ((System.Windows.Controls.TextBox)sender).GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            AddTask();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            // Shift+Enter 允许换行（交由 TextBox 默认行为）
        }
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskItem item)
        {
            List.Items.Remove(item);
            Persist();
        }
    }

    private void AddTask_Click(object sender, RoutedEventArgs e) => AddTask();

    private void AddTask()
    {
        var newItem = new TaskItem { Text = "新任务" };
        // 插入到列表最前面：未完成任务区（IsDone 相同）内按源集合顺序稳定排序，
        // 因此新任务会显示在未完成区的置顶位置
        List.Items.Insert(0, newItem);
        Persist();
        // 聚焦到新添加任务的文本框（文字色由 XAML 绑定处理）
        // 注意：CollectionView 按 IsDone 排序，不能按索引取，必须用新项对象定位
        TaskList.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (TaskList.ItemContainerGenerator.ContainerFromItem(newItem) is ListBoxItem container)
            {
                if (container.FindVisualChild<System.Windows.Controls.TextBox>() is System.Windows.Controls.TextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }
            // 新项同步锁定状态（只读/删除按钮可见性）
            ApplyLockToItems();
        }), DispatcherPriority.Loaded);
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
