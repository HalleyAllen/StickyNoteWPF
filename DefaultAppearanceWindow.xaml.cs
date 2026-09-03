using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

/// <summary>
/// 默认外观设置：仅作为新建便利贴/任务清单的默认值，不影响已创建的便签与清单。
/// 从原全局设置中心拆出（透明度/字号/背景色/文字按钮色），与全局设置分窗管理。
/// </summary>
public partial class DefaultAppearanceWindow : Window
{
    private readonly AppSettings _settings;
    // 必须初始为 true：XAML 里绑定的 ValueChanged 在 InitializeComponent() 解析时就已挂上，
    // Slider 的 Value(0) 会被 Minimum 钳制并立刻触发一次 ValueChanged，
    // 那时构造函数尚未执行完、Loaded 更未触发，若为 false 会把 0 直接写回配置。
    private bool _suppressSliderEvents = true;

    public DefaultAppearanceWindow()
    {
        _settings = App.Current.Settings;
        InitializeComponent();

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

        // 默认背景颜色：仅作为新建便签/任务清单的默认值，不影响已创建的。
        // 用局部函数自引用，重建色板时继续传入自身，保证之后点击其他颜色仍生效
        void SetDefaultColor(string hex)
        {
            _settings.DefaultColor = hex;
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, hex, SetDefaultColor);
        }
        // 统一的文字与按钮颜色：仅作为新建便签/任务清单的默认值，不影响已创建的
        void SetNoteTextColor(string hex)
        {
            _settings.NoteTextColor = hex;
            _settings.TitleTextColor = hex;
            _settings.ButtonColor = hex;
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(TextColorPanel, _settings, hex, SetNoteTextColor);
        }

        AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, _settings.DefaultColor, SetDefaultColor);
        AppearanceHelper.BuildColorSwatches(TextColorPanel, _settings, _settings.NoteTextColor, SetNoteTextColor);

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null || _suppressSliderEvents) return;
        var v = e.NewValue;
        _settings.WindowOpacity = v;
        _settings.Save();
        UpdateOpacityLabel(v);
    }

    // 初始字体大小：仅作为新建便签/任务清单的默认值，不影响已创建的
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
}
