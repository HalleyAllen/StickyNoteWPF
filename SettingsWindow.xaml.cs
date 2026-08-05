using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string[] Palette =
    {
        "#FFF7A900", "#FFFFF275", "#FFA0E7A0", "#FF9AD0EC",
        "#FFE6A8E0", "#FFFFB3A7", "#FFD6C8FF", "#FFC0C0C0"
    };

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        // 恢复上次保存的窗口大小
        if (_settings.SettingsWindowWidth > 0 && _settings.SettingsWindowHeight > 0)
        {
            Width = _settings.SettingsWindowWidth;
            Height = _settings.SettingsWindowHeight;
        }

        StartupCheck.IsChecked = AppSettings.IsStartupEnabled();
        TopmostCheck.IsChecked = _settings.GlobalTopmost;
        OpacitySlider.Value = _settings.WindowOpacity;
        UpdateOpacityLabel(_settings.WindowOpacity);

        BuildSwatches(ColorPanel, _settings.DefaultColor,
            hex => { _settings.DefaultColor = hex; _settings.Save(); BuildSwatches(ColorPanel, hex, null); },
            hex => { _settings.DefaultColor = hex; _settings.Save(); BuildSwatches(ColorPanel, hex, null); });
        // 统一的文字与按钮颜色：仅作为新建便签的默认值，不影响已创建的便签
        BuildSwatches(TextColorPanel, _settings.NoteTextColor,
            hex =>
            {
                _settings.NoteTextColor = hex;
                _settings.TitleTextColor = hex;
                _settings.ButtonColor = hex;
                _settings.Save();
                BuildSwatches(TextColorPanel, hex, null);
            },
            hex =>
            {
                _settings.NoteTextColor = hex;
                _settings.TitleTextColor = hex;
                _settings.ButtonColor = hex;
                _settings.Save();
                BuildSwatches(TextColorPanel, hex, null);
            });

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        Closing += (_, _) =>
        {
            _settings.SettingsWindowWidth = Width;
            _settings.SettingsWindowHeight = Height;
            _settings.Save();
        };
        StartupCheck.Checked += (_, _) => { AppSettings.SetStartupWithWindows(true); _settings.StartupWithWindows = true; _settings.Save(); };
        StartupCheck.Unchecked += (_, _) => { AppSettings.SetStartupWithWindows(false); _settings.StartupWithWindows = false; _settings.Save(); };
        TopmostCheck.Checked += (_, _) => { _settings.GlobalTopmost = true; _settings.Save(); App.Current.ApplyTopmost(true); };
        TopmostCheck.Unchecked += (_, _) => { _settings.GlobalTopmost = false; _settings.Save(); App.Current.ApplyTopmost(false); };
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null) return;
        var v = e.NewValue;
        _settings.WindowOpacity = v;
        _settings.Save();
        UpdateOpacityLabel(v);
    }

    private void UpdateOpacityLabel(double v)
    {
        OpacityValue.Text = $"{Math.Round(v * 100)}%";
    }

    // onPick: 选中色板颜色时回调；onCustom: 点击"自定义"按钮（打开取色器）时回调
    private void BuildSwatches(WrapPanel panel, string current, Action<string>? onPick, Action<string>? onCustom = null)
    {
        panel.Children.Clear();

        // 自定义颜色按钮（✎）
        var custom = new Border
        {
            Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(4),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            BorderThickness = new Thickness(1),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        custom.Child = new TextBlock
        {
            Text = "✎", FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray)
        };
        custom.MouseLeftButtonDown += (_, _) => PickCustomColor(hex => onCustom?.Invoke(hex));
        panel.Children.Add(custom);

        foreach (var hex in Palette)
        {
            var btn = new Border
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(4), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(hex == current ? 3 : 1),
                BorderBrush = hex == current ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = hex;
            btn.MouseLeftButtonDown += (_, _) => onPick?.Invoke(captured);
            panel.Children.Add(btn);
        }
    }

    private void PickCustomColor(Action<string> onPicked)
    {
        using var dlg = new System.Windows.Forms.ColorDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var hex = "#" + dlg.Color.ToArgb().ToString("X8");
            onPicked(hex);
        }
    }
}
