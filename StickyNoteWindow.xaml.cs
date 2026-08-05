using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
        NoteTextBox.FontSize = note.FontSize;

        RefreshFromModel();
        ApplyTitle();

        TitleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
        CloseButton.Click += (_, _) => RequestClose();
        SettingsButton.Click += (_, _) => App.Current.OpenNoteSettings(this);
        NoteTextBox.TextChanged += (_, _) => { Note.Text = NoteTextBox.Text; Persist(); };

        // 鼠标移入便签区域才显示，移开则隐藏（鼠标穿透仍可捕获进入）
        MouseEnter += (_, _) => ShowContent();
        MouseLeave += (_, _) => HideContent();
        HideContent();
    }

    // 鼠标移入：显示完整便签并恢复置顶
    private void ShowContent()
    {
        Opacity = 1;
        Topmost = App.Current?.Settings.GlobalTopmost ?? true;
    }

    // 鼠标移开：隐藏（Opacity=0 但仍可命中，以便再次移入时显示）
    private void HideContent()
    {
        Opacity = 0;
        Topmost = false;
    }

    // 根据 Note 自身存储的外观重新应用（颜色/透明度/字体/文字色）
    public void RefreshFromModel()
    {
        ApplyColor(Note.Color);
        ApplyTextStyle();
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
        ApplyColor(Note.Color);
        Persist();
    }

    private void ApplyColor(string hex)
    {
        Note.Color = hex;
        var o = Math.Clamp(Note.Opacity, 0.0, 1.0);
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            c.A = (byte)(o * 255);
            RootBorder.Background = new System.Windows.Media.SolidColorBrush(c);

            // 边框与标题栏同样随透明度比例变化（保持相对浓淡：背景最浓、边框次之、标题栏最淡）
            RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x55), 0, 0, 0));
            TitleBar.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(o * 0x22), 0, 0, 0));

            ApplyShadow();
        }
        catch { }
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
        NoteTextBox.FontSize = Note.FontSize;
        NoteTextBox.Foreground = MakeBrush(Note.TextColor);
        NoteTextBox.CaretBrush = MakeBrush(Note.TextColor);
        TitleText.Foreground = MakeBrush(Note.TextColor);
        SettingsButton.Foreground = MakeBrush(Note.TextColor);
        CloseButton.Foreground = MakeBrush(Note.TextColor);
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
