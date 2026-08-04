using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StickyNoteWPF.Models;

namespace StickyNoteWPF;

public partial class StickyNoteSettingsWindow : Window
{
    private readonly StickyNoteWindow _owner;
    private readonly StickyNoteModel _note;

    private static readonly string[] Palette =
    {
        "#FFF7A900", "#FFFFF275", "#FFA0E7A0", "#FF9AD0EC",
        "#FFE6A8E0", "#FFFFB3A7", "#FFD6C8FF", "#FFC0C0C0"
    };

    public StickyNoteSettingsWindow(StickyNoteWindow owner)
    {
        _owner = owner;
        _note = owner.Note;
        InitializeComponent();

        // 颜色设置后用本地函数重绘，保证 onPick 始终有效（避免重绘传入 null 导致后续点击无反应）
        void SetBg(string hex)
        {
            _note.Color = hex;
            // 刷新便签窗口外观 + 保存 + 同步管理界面列表背景
            App.Current.RefreshNote(_note, true);
            BuildSwatches(BgColorPanel, hex, SetBg);
        }
        void SetText(string hex)
        {
            _note.TextColor = hex;
            App.Current.RefreshNote(_note, true);
            BuildSwatches(TextColorPanel, hex, SetText);
        }

        BgColorPanel.Children.Add(MakeCustomSwatch(SetBg));
        TextColorPanel.Children.Add(MakeCustomSwatch(SetText));
        BuildSwatches(BgColorPanel, _note.Color, SetBg);
        BuildSwatches(TextColorPanel, _note.TextColor, SetText);

        FontSizeSlider.Value = _note.FontSize;
        FontSizeValue.Text = $"{Math.Round(_note.FontSize)}";

        OpacitySlider.Value = _note.Opacity;
        OpacityValue.Text = $"{Math.Round(_note.Opacity * 100)}%";

        TitleBox.Text = _note.Title;

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
    }

    private void Commit()
    {
        _owner.RefreshFromModel();
        App.Current.SaveAll();
    }

    private Border MakeCustomSwatch(Action<string> onPicked)
    {
        var btn = new Border
        {
            Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(System.Windows.Media.Colors.White),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Child = new TextBlock
        {
            Text = "✎", FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = new SolidColorBrush(System.Windows.Media.Colors.Gray)
        };
        btn.MouseLeftButtonDown += (_, _) => PickCustom(hex =>
        {
            onPicked?.Invoke(hex);
            // 自定义选色后重绘该面板高亮（由回调内的 BuildSwatches 处理）
        });
        return btn;
    }

    private void PickCustom(Action<string> onPicked)
    {
        using var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var argb = dlg.Color.ToArgb();
            var hex = "#" + argb.ToString("X8");
            onPicked(hex);
        }
    }

    private void BuildSwatches(WrapPanel panel, string current, Action<string>? onPick)
    {
        // 保留第一个子元素（自定义按钮），重建其余色板
        while (panel.Children.Count > 1)
            panel.Children.RemoveAt(panel.Children.Count - 1);

        foreach (var hex in Palette)
        {
            var btn = new Border
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(hex == current ? 3 : 1),
                BorderBrush = hex == current
                    ? new SolidColorBrush(System.Windows.Media.Colors.Black)
                    : new SolidColorBrush(System.Windows.Media.Colors.Gray),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = hex;
            btn.MouseLeftButtonDown += (_, _) => onPick?.Invoke(captured);
            panel.Children.Add(btn);
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_note == null) return;
        var v = e.NewValue;
        _note.FontSize = v;
        FontSizeValue.Text = $"{Math.Round(v)}";
        Commit();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_note == null) return;
        var v = e.NewValue;
        _note.Opacity = v;
        OpacityValue.Text = $"{Math.Round(v * 100)}%";
        Commit();
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_note == null) return;
        // “便利贴”视为默认占位，不写入模型，保持与主窗口占位逻辑一致
        _note.Title = string.Equals(TitleBox.Text.Trim(), "便利贴", StringComparison.Ordinal)
            ? string.Empty
            : TitleBox.Text;
        // 刷新便签窗口标题 + 保存 + 同步管理界面列表
        App.Current.RefreshNote(_note, true);
    }
}
