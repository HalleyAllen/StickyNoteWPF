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

    public StickyNoteWindow(StickyNoteModel note)
    {
        InitializeComponent();
        Note = note;

        Left = note.Left;
        Top = note.Top;
        Width = note.Width;
        Height = note.Height;
        NoteTextBox.Text = note.Text;
        // 兼容旧数据：若字体大小无效（<=0），回退到默认字体大小并写回模型
        if (note.FontSize <= 0)
            note.FontSize = App.Current?.Settings.DefaultFontSize ?? 14;
        NoteTextBox.FontSize = note.FontSize;

        RefreshFromModel();
        ApplyTitle();

        TitleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        CloseButton.Click += (_, _) => RequestClose();
        SettingsButton.Click += (_, _) => App.Current.OpenNoteSettings(this);
        EyeButton.Click += (_, _) => ToggleHover();
        NoteTextBox.TextChanged += (_, _) => { Note.Text = NoteTextBox.Text; Persist(); };

        // 鼠标悬停显示：用全局光标坐标判定是否落在便签矩形内（Opacity=0 时窗口不可命中，需轮询）
        ApplyHoverState();
    }

    // 眼睛按钮：切换该便签是否启用“鼠标移入才显示”
    private void ToggleHover()
    {
        Note.HoverToShow = !Note.HoverToShow;
        Persist();
        ApplyHoverState();
    }

    // 根据当前便签的 HoverToShow 应用悬停显隐状态，并刷新眼睛图标
    private void ApplyHoverState()
    {
        EyeButton.Content = Note.HoverToShow ? "👁" : "🚫";
        EyeButton.ToolTip = Note.HoverToShow ? "鼠标移入才显示（点此关闭）" : "始终显示（点此开启悬停）";

        // 全局“全部显示”开启时，无论单个便签开关如何，一律强制不透明且始终显示
        if (App.Current?.Settings.ForceShowAll == true)
        {
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
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
            _hoverTimer?.Stop();
            _hoverTimer = null;
            Opacity = 1;
            IsHitTestVisible = true;
            Topmost = App.Current?.Settings.GlobalTopmost ?? true;
        }
    }

    // 全局“全部显示”开关：开启时强制所有便签不透明且始终可见（停用各自的隐藏/透明效果）
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
            // 恢复该便签自身设置
            ApplyHoverState();
        }
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
        ApplyScrollBarTheme();
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
        var o = Math.Clamp(Note.Opacity, 0.0, 1.0);
        try
        {
            if (!string.IsNullOrEmpty(Note.BackgroundImagePath) && File.Exists(Note.BackgroundImagePath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(Note.BackgroundImagePath, UriKind.Absolute);
                bmp.EndInit();

                RootBorder.Background = new System.Windows.Media.ImageBrush(bmp)
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                    Opacity = o
                };
            }
            else
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Note.Color);
                c.A = (byte)(o * 255);
                RootBorder.Background = new System.Windows.Media.SolidColorBrush(c);
            }

            // 边框与标题栏同样随透明度比例变化（保持相对浓淡）
            RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x55), 0, 0, 0));
            TitleBar.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x22), 0, 0, 0));
        }
        catch
        {
            // 解析失败回退纯色
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Note.Color);
                c.A = (byte)(o * 255);
                RootBorder.Background = new System.Windows.Media.SolidColorBrush(c);
            }
            catch { }
        }

        ApplyShadow();
    }

    private void ApplyShadow()
    {
        RootBorder.Effect = new DropShadowEffect
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

        ApplyScrollBarTheme();
    }

    // 便签文本区滚动条跟随主题：滑块/箭头用文字色，轨道用背景色（均按便签透明度半透明），
    // 改便签主题时由 RefreshFromModel / ApplyTextStyle 自动同步，无需单独设置。
    private void ApplyScrollBarTheme()
    {
        try
        {
            var textColor = ParseColor(Note.TextColor, System.Windows.Media.Colors.Black);
            var bgColor = ParseColor(Note.Color, System.Windows.Media.Colors.White);

            // 滚动条只看颜色、不随便签透明度变化：
            // 轨道（背景）= 便签主题背景色，滑块（Thumb）= 便签文字色，均为实色
            var thumb = textColor;
            var track = bgColor;
            var hover = textColor;

            // 这些画刷供滚动条模板以 DynamicResource 引用，主题变化时替换即可刷新
            NoteTextBox.Resources["ScrollThumbBrush"] = new System.Windows.Media.SolidColorBrush(thumb);
            NoteTextBox.Resources["ScrollThumbHoverBrush"] = new System.Windows.Media.SolidColorBrush(hover);
            NoteTextBox.Resources["ScrollTrackBrush"] = new System.Windows.Media.SolidColorBrush(track);

            // 方案（最可靠 + 经标准模板验证可显隐）：
            // 自定义 TextBox 模板（提供 PART_ContentHost=ScrollViewer），并为该 ScrollViewer 自定义模板。
            // ScrollViewer 模板采用【标准完整结构】：
            //   - ScrollContentPresenter（x:Name=PART_ScrollContentPresenter）承载内容，保证 ExtentHeight 正确计算
            //   - 垂直 ScrollBar（x:Name=PART_VerticalScrollBar）绑定 ScrollableHeight/ViewportHeight/VerticalOffset/
            //     ComputedVerticalScrollBarVisibility，内容溢出时自动出现
            //   - 两个角落 Corner（x:Name=PART_ScrollCorner 等）保持标准
            // 垂直 ScrollBar 显式引用同模板 Resources 中的细样式 NoteScrollBarStyle。
            // 颜色用 DynamicResource 引用 NoteTextBox.Resources 的画刷，主题变化时替换即可实时刷新。
            const string templateXaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "xmlns:s='clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework' " +
                "TargetType='{x:Type TextBox}'>" +
                "<Border x:Name='PART_Border' Background='{TemplateBinding Background}' " +
                "BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' " +
                "SnapsToDevicePixels='True'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='0' Padding='{TemplateBinding Padding}' " +
                "Focusable='False' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>" +
                "<ScrollViewer.Template>" +
                "<ControlTemplate TargetType='{x:Type ScrollViewer}'>" +
                "<ControlTemplate.Resources>" +
                "<Style x:Key='NoteScrollBarStyle' TargetType='{x:Type ScrollBar}'>" +
                "<Setter Property='Width' Value='5'/>" +
                "<Setter Property='Background' Value='{DynamicResource ScrollTrackBrush}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ScrollBar}'>" +
                "<Track x:Name='PART_Track' Width='5' HorizontalAlignment='Stretch' IsDirectionReversed='True'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='{x:Static ScrollBar.LineUpCommand}' Focusable='False' Opacity='0'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type ButtonBase}'>" +
                "<Border Background='Transparent'/>" +
                "</ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType='{x:Type Thumb}'>" +
                "<Rectangle Name='Rect' Width='5' Fill='{DynamicResource ScrollThumbBrush}' RadiusX='3' RadiusY='3'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Rect' Property='Fill' Value='{DynamicResource ScrollThumbHoverBrush}'/>" +
                "</Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='{x:Static ScrollBar.LineDownCommand}' Focusable='False' Opacity='0'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type ButtonBase}'>" +
                "<Border Background='Transparent'/>" +
                "</ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.IncreaseRepeatButton>" +
                "</Track>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>" +
                "</ControlTemplate.Resources>" +
                "<Grid Background='Transparent'>" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
                "<Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>" +
                "<ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Grid.Column='0' Grid.Row='0' " +
                "Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' " +
                "CanContentScroll='{TemplateBinding CanContentScroll}'/>" +
                "<ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Grid.Row='0' Orientation='Vertical' " +
                "Style='{StaticResource NoteScrollBarStyle}' " +
                "Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' " +
                "Value='{TemplateBinding VerticalOffset}' " +
                "Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>" +
                "<DockPanel Grid.Column='0' Grid.Row='1' LastChildFill='False' Background='Transparent'>" +
                "<Rectangle x:Name='PART_ScrollCorner' DockPanel.Dock='Right' Width='5' Height='5' Fill='Transparent'/>" +
                "</DockPanel>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</ScrollViewer.Template>" +
                "</ScrollViewer>" +
                "</Border>" +
                "</ControlTemplate>";
            NoteTextBox.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(templateXaml);
            NoteTextBox.ApplyTemplate();
        }
        catch (Exception ex)
        {
            // 滚动条样式失败不应影响便签正常使用，静默忽略
            System.Diagnostics.Debug.WriteLine("ApplyScrollBarTheme failed: " + ex.Message);
        }
    }

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
