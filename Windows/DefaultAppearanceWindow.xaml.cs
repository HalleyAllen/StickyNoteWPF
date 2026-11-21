using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

/// <summary>
/// 默认外观设置。按入口类型单独打开：
/// forTaskOnly=true  只调整新建任务清单的默认外观（TaskList*）；
/// forTaskOnly=false 只调整新建便利贴的默认外观（DefaultColor 等）。
/// 两套默认完全独立、互不影响，且仅影响之后新建的对应类型，不影响已创建的。
/// </summary>
public partial class DefaultAppearanceWindow : Window
{
    private readonly AppSettings _settings;
    // 必须初始为 true：XAML 里绑定的 ValueChanged 在 InitializeComponent() 解析时就已挂上，
    // Slider 的 Value(0) 会被 Minimum 钳制并立刻触发一次 ValueChanged，
    // 那时构造函数尚未执行完、Loaded 更未触发，若为 false 会把 0 直接写回配置。
    private bool _suppressSliderEvents = true;
    // true = 本窗口只设置任务清单默认；false = 本窗口只设置便利贴默认（打开后固定，不可切换）
    private readonly bool _isTask;

    // 读取本窗口对应类型的默认值（Load() 已完成旧数据迁移，字段不会为 null，常量仅兜底）
    private double CurrentOpacity => _isTask
        ? _settings.TaskListOpacity ?? AppSettings.TaskListDefaultOpacity
        : _settings.WindowOpacity;
    private double CurrentFontSize => _isTask
        ? _settings.TaskListFontSize ?? AppSettings.TaskListDefaultFontSize
        : _settings.DefaultFontSize;
    private string CurrentColorHex => _isTask
        ? _settings.TaskListColor ?? AppSettings.TaskListDefaultColorHex
        : _settings.DefaultColor;
    private string CurrentTextColorHex => _isTask
        ? _settings.TaskListTextColor ?? AppSettings.TaskListDefaultTextColorHex
        : _settings.NoteTextColor;

    public DefaultAppearanceWindow(bool forTaskOnly)
    {
        _settings = App.Current.Settings;
        _isTask = forTaskOnly;
        InitializeComponent();

        // 兼容旧数据：默认字体大小无效时回退，避免被 Slider 钳到最小值
        if (_settings.DefaultFontSize <= 0)
            _settings.DefaultFontSize = 14;

        // 只保留本窗口对应的类型标识，另一类型完全不显示，避免误改到另一套默认
        if (_isTask)
        {
            NoteRadio.Visibility = Visibility.Collapsed;
            TaskRadio.IsChecked = true;
        }
        else
        {
            TaskRadio.Visibility = Visibility.Collapsed;
        }

        // “是否显示已完成任务”仅对任务清单默认有意义，便利贴默认窗口不显示
        ShowCompletedCheck.Visibility = _isTask ? Visibility.Visible : Visibility.Collapsed;

        // 关键：Slider 必须在模板应用/布局完成后再赋值，
        // 否则构造函数中的赋值会被钳制回 Minimum，导致每次打开都显示最小值。
        Loaded += (_, _) =>
        {
            _suppressSliderEvents = true;
            OpacitySlider.Value = Math.Clamp(CurrentOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            FontSizeSlider.Value = Math.Clamp(CurrentFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            ShowCompletedCheck.IsChecked = _settings.TaskListShowCompleted;
            _suppressSliderEvents = false;

            UpdateOpacityLabel(OpacitySlider.Value);
            FontSizeValue.Text = $"{Math.Round(FontSizeSlider.Value)}";
        };

        RefreshTargetUi();

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
    }

    // 重建色板与说明文案（窗口作用类型在打开时已固定，无切换入口）
    private void RefreshTargetUi()
    {
        _suppressSliderEvents = true;
        if (OpacitySlider != null && FontSizeSlider != null)
        {
            OpacitySlider.Value = Math.Clamp(CurrentOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            FontSizeSlider.Value = Math.Clamp(CurrentFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
        }
        _suppressSliderEvents = false;

        UpdateOpacityLabel(CurrentOpacity);
        if (FontSizeValue != null)
            FontSizeValue.Text = $"{Math.Round(CurrentFontSize)}";

        if (HeaderText != null)
        {
            HeaderText.Text = _isTask
                ? "以下设置仅作为新建任务清单的默认外观，不影响便利贴与已创建的任务清单。"
                : "以下设置仅作为新建便利贴的默认外观，不影响任务清单与已创建的便利贴。";
        }

        // 重建色板（局部函数自引用，保证之后点击其他颜色仍生效）
        void OnPickColor(string hex)
        {
            if (_isTask) _settings.TaskListColor = hex;
            else _settings.DefaultColor = hex;
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, hex, OnPickColor);
        }
        void OnPickText(string hex)
        {
            if (_isTask)
            {
                _settings.TaskListTextColor = hex;
            }
            else
            {
                // 便签默认：文字/标题/按钮统一跟随（保持历史行为）
                _settings.NoteTextColor = hex;
                _settings.TitleTextColor = hex;
                _settings.ButtonColor = hex;
            }
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(TextColorPanel, _settings, hex, OnPickText);
        }

        if (ColorPanel != null)
            AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, CurrentColorHex, OnPickColor);
        if (TextColorPanel != null)
            AppearanceHelper.BuildColorSwatches(TextColorPanel, _settings, CurrentTextColorHex, OnPickText);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null || _suppressSliderEvents) return;
        var v = e.NewValue;
        if (_isTask) _settings.TaskListOpacity = v;
        else _settings.WindowOpacity = v;
        _settings.Save();
        UpdateOpacityLabel(v);
    }

    // 初始字体大小：仅作为新建对应类型的默认值，不影响已创建的
    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null || _suppressSliderEvents) return;
        var v = Math.Round(e.NewValue);
        if (_isTask) _settings.TaskListFontSize = v;
        else _settings.DefaultFontSize = v;
        _settings.Save();
        FontSizeValue.Text = $"{v}";
    }

    private void UpdateOpacityLabel(double v)
    {
        OpacityValue.Text = $"{Math.Round(v * 100)}%";
    }

    // “是否显示已完成任务”：仅写入任务清单的默认值（便利贴窗口无此开关）
    private void ShowCompletedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isTask || _suppressSliderEvents) return;
        _settings.TaskListShowCompleted = ShowCompletedCheck.IsChecked == true;
        _settings.Save();
    }
}
