using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    // 必须初始为 true：XAML 里绑定的 ValueChanged 在 InitializeComponent() 解析时就已挂上，
    // Slider 的 Value(0) 会被 Minimum(8) 强制钳成 8 并立刻触发一次 ValueChanged，
    // 那时构造函数尚未执行完、Loaded 更未触发，若为 false 会把 8 直接写回配置。
    private bool _suppressSliderEvents = true;
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
        // 兼容旧数据：默认字体大小无效时回退，避免被 Slider 钳到最小值
        if (_settings.DefaultFontSize <= 0)
            _settings.DefaultFontSize = 14;

        UpdateOpacityLabel(_settings.WindowOpacity);
        FontSizeValue.Text = $"{Math.Round(_settings.DefaultFontSize)}";

        // 关键：Slider 必须在模板应用/布局完成后再赋值，
        // 否则构造函数中的赋值会被钳制回 Minimum，导致每次打开都显示最小值。
        Loaded += (_, _) =>
        {
            _suppressSliderEvents = true;
            OpacitySlider.Value = Math.Clamp(_settings.WindowOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            FontSizeSlider.Value = Math.Clamp(_settings.DefaultFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            _suppressSliderEvents = false;

            UpdateOpacityLabel(OpacitySlider.Value);
            FontSizeValue.Text = $"{Math.Round(FontSizeSlider.Value)}";
        };

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
        if (_settings == null || _suppressSliderEvents) return;
        var v = e.NewValue;
        _settings.WindowOpacity = v;
        _settings.Save();
        UpdateOpacityLabel(v);
    }

    // 初始字体大小：仅作为新建便签的默认值，不影响已创建的便签
    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null || _suppressSliderEvents) return;
        var v = Math.Round(e.NewValue);
        _settings.DefaultFontSize = v;
        _settings.Save();
        FontSizeValue.Text = $"{v}";
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
