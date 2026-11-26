using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class StickyNoteWindow : Window
{
    public StickyNoteModel Note { get; }
    private readonly WindowBackgroundImage _backgroundImage;

    public StickyNoteWindow(StickyNoteModel note)
    {
        InitializeComponent();
        Note = note;
        _backgroundImage = new(this, RootBorder);

        WindowPlacement.Restore(this, new(note.Left, note.Top, note.Width, note.Height), 220, 220);
        NoteTextBox.Text = note.Text;
        // 兼容旧数据：若字体大小无效（<=0），回退到默认字体大小并写回模型
        if (note.FontSize <= 0)
            note.FontSize = App.Current?.Settings.DefaultFontSize ?? 14;
        NoteTextBox.FontSize = note.FontSize;

        RefreshFromModel();
        ApplyTitle();

        TitleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        NoteTextBox.PreviewKeyDown += NoteTextBox_PreviewKeyDown;
        SizeChanged += (_, _) => TitleText.Visibility = ActualWidth < 200 ? Visibility.Hidden : Visibility.Visible;
        CloseButton.Click += (_, _) => RequestClose();
        SettingsButton.Click += (_, _) => App.Current?.OpenNoteSettings(this);
        EyeButton.Click += (_, _) => ToggleHover();
        LockButton.Click += (_, _) => ToggleLock();
        NoteTextBox.TextChanged += (_, _) => { Note.Text = NoteTextBox.Text; Persist(); };

        // 鼠标悬停显示：用全局光标坐标判定是否落在便签矩形内（Opacity=0 时窗口不可命中，需轮询）
        IsVisibleChanged += Window_IsVisibleChanged;
        ApplyHoverState();
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) ApplyHoverState();
        else StopHoverTimer();
    }

    private void StopHoverTimer()
    {
        if (_hoverTimer == null) return;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= HoverTick;
        _hoverTimer = null;
    }

    // 眼睛按钮：切换该便签是否启用“鼠标移入才显示”
    private void ListButton_Click(object sender, RoutedEventArgs e)
    {
        ListButton.ContextMenu.PlacementTarget = ListButton;
        ListButton.ContextMenu.IsOpen = true;
    }

    private void ListMenu_Opened(object sender, RoutedEventArgs e)
    {
        var kind = NoteListEditor.GetKind(NoteTextBox.Text, NoteTextBox.SelectionStart, NoteTextBox.SelectionLength);
        OrderedListMenu.IsChecked = kind == NoteListKind.Ordered;
        UnorderedListMenu.IsChecked = kind == NoteListKind.Unordered;
    }

    private void ListMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Note.IsLocked || sender is not System.Windows.Controls.MenuItem { Tag: string tag }
            || !Enum.TryParse<NoteListKind>(tag, out var kind)) return;
        ApplyListEdit(NoteListEditor.Toggle(NoteTextBox.Text, NoteTextBox.SelectionStart, NoteTextBox.SelectionLength, kind));
    }

    private void NoteTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Note.IsLocked) return;
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.D7 or Key.D8)
        {
            ApplyListEdit(NoteListEditor.Toggle(NoteTextBox.Text, NoteTextBox.SelectionStart, NoteTextBox.SelectionLength,
                e.Key == Key.D7 ? NoteListKind.Ordered : NoteListKind.Unordered));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
            && NoteListEditor.Continue(NoteTextBox.Text, NoteTextBox.SelectionStart, NoteTextBox.SelectionLength) is { } edit)
        {
            ApplyListEdit(edit);
            e.Handled = true;
        }
    }

    private void ApplyListEdit(NoteTextEdit edit)
    {
        NoteTextBox.BeginChange();
        try
        {
            NoteTextBox.Select(edit.Start, edit.Length);
            NoteTextBox.SelectedText = edit.Text;
            NoteTextBox.Select(edit.SelectionStart, edit.SelectionLength);
        }
        finally { NoteTextBox.EndChange(); }
        NoteTextBox.Focus();
    }


    private void ToggleHover()
    {
        Note.HoverToShow = !Note.HoverToShow;
        Persist();
        ApplyHoverState();
    }

    // 锁按钮：切换该便签是否锁定（锁定后内容只读，不可编辑）
    private void ToggleLock()
    {
        Note.IsLocked = !Note.IsLocked;
        Persist();
        ApplyLockState();
    }

    // 根据当前便签的 IsLocked 刷新锁图标与文本框只读状态
    private void ApplyLockState()
    {
        LockButton.Content = Note.IsLocked ? "\uE72E" : "\uE785";
        LockButton.ToolTip = Note.IsLocked ? "已锁定（点此解锁，解锁后可编辑）" : "未锁定（点此锁定，锁定后不可编辑）";
        NoteTextBox.IsReadOnly = Note.IsLocked;
        ListButton.IsEnabled = !Note.IsLocked;
        NoteTextBox.CaretBrush = Note.IsLocked ? System.Windows.Media.Brushes.Transparent : MakeBrush(Note.TextColor);
    }

    // 根据当前便签的 HoverToShow 应用悬停显隐状态，并刷新眼睛图标
    private void ApplyHoverState()
    {
        EyeButton.Content = Note.HoverToShow ? "\uE890" : "\uED1A";
        EyeButton.ToolTip = Note.HoverToShow ? "鼠标移入才显示（点此关闭）" : "始终显示（点此开启悬停）";
        if (!IsVisible)
        {
            StopHoverTimer();
            return;
        }
        if (Note.HoverToShow)
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
            StopHoverTimer();
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
        }
    }

    // 恢复该便签自己的背景透明度和鼠标悬停显隐。
    public void RestoreAppearance()
    {
        ApplyBackground();
        ApplyHoverState();
    }

    private DispatcherTimer? _hoverTimer;

    // 定时器按全局鼠标坐标判定是否进入便签区域
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

    // 鼠标移入：显示完整便签并恢复置顶、可交互
    private void ShowContent()
    {
        Opacity = 1;
        IsHitTestVisible = true;
        Topmost = App.Current?.Settings.GlobalTopmost ?? true;
    }

    // 鼠标移开：隐藏并穿透（不挡桌面操作），仍可被坐标判定重新显示
    private void HideContent()
    {
        Opacity = 0;
        IsHitTestVisible = false;
        Topmost = false;
    }

    // 根据 Note 自身存储的外观重新应用（颜色/透明度/字体/文字色）
    public void RefreshFromModel()
    {
        ApplyBackground();
        ApplyTextStyle();
        ApplyLockState();
        ApplyTitle();
    }

    // 应用便签标题：空则用默认“便利贴”
    public void ApplyTitle()
    {
        TitleText.Text = string.IsNullOrWhiteSpace(Note.Title) ? "便利贴" : Note.Title;
    }

    // 仅调节窗口背景透明度，文字保持不透明（供全局滑块批量调用）
    public void ApplyOpacity(double opacity)
    {
        Note.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        ApplyBackground();
        Persist();
    }

    // 应用背景：有图片用图片背景，否则用纯色背景；两者都遵循窗口透明度。
    // 关键：图片模式下 RootBorder.Background 直接设为 ImageBrush，
    // 不再叠加纯色层，否则透明度拉满时纯色不透明会盖住图片。
    public void ApplyBackground()
    {
        var o = WindowAppearance.EffectiveOpacity(Note.Opacity);
        var color = AppearanceHelper.ParseColor(Note.Color, System.Windows.Media.Colors.White);
        color.A = (byte)(o * 255);
        _backgroundImage.Apply(Note.BackgroundImagePath, "Fill", o, new SolidColorBrush(color));
        RootBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(o * 0x55), 0, 0, 0));
        TitleBar.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(o * 0x22), 0, 0, 0));

        ApplyShadow();
    }

    private void ApplyShadow()
    {
        RootBorder.Effect ??= new DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            BlurRadius = 12,
            ShadowDepth = 3,
            Opacity = 0.3
        };
    }

    // 应用便签文字 / 标题文字 / 按钮 的颜色（来自该便签自身存储）
    public void ApplyTextStyle()
    {
        NoteTextBox.FontSize = Note.FontSize > 0 ? Note.FontSize : (App.Current?.Settings.DefaultFontSize ?? 14);
        NoteTextBox.Foreground = MakeBrush(Note.TextColor);
        NoteTextBox.CaretBrush = MakeBrush(Note.TextColor);
        TitleText.Foreground = MakeBrush(Note.TextColor);
        SettingsButton.Foreground = MakeBrush(Note.TextColor);
        CloseButton.Foreground = MakeBrush(Note.TextColor);
        EyeButton.Foreground = MakeBrush(Note.TextColor);
        ListButton.Foreground = MakeBrush(Note.TextColor);
        LockButton.Foreground = MakeBrush(Note.TextColor);

        ApplyScrollBarTheme();
    }

    // 便签文本区滚动条跟随主题：滑块/箭头用文字色，轨道用背景色（均按便签透明度半透明），
    // 改便签主题时由 RefreshFromModel / ApplyTextStyle 自动同步，无需单独设置。
    private void ApplyScrollBarTheme()
        => AppearanceHelper.ApplyScrollBarTheme(NoteTextBox, Note.TextColor, Note.Color);

    private static System.Windows.Media.Brush MakeBrush(string hex)
    {
        try
        {
            return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return System.Windows.Media.Brushes.Black;
        }
    }

    private static System.Windows.Media.Color ParseColor(string hex, System.Windows.Media.Color fallback)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        Note.Left = Left;
        Note.Top = Top;
        Persist();
        base.OnLocationChanged(e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        Note.Width = Width;
        Note.Height = Height;
        Persist();
        base.OnRenderSizeChanged(sizeInfo);
    }

    protected override void OnClosed(EventArgs e)
    {
        IsVisibleChanged -= Window_IsVisibleChanged;
        StopHoverTimer();
        _backgroundImage.Dispose();
        base.OnClosed(e);
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
