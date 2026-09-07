using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

/// <summary>
/// 默认外观设置：分别提供「便利贴」与「任务清单」两套新建默认值，各自独立。
/// 便利贴默认 = DefaultColor/NoteTextColor/WindowOpacity/DefaultFontSize；
/// 任务清单默认 = TaskList*（null 表示未单独设置，此时跟随便利贴默认）。
/// 仅影响之后新建的对应类型，不影响已创建的对象。
/// </summary>
public partial class DefaultAppearanceWindow : Window
{
    private readonly AppSettings _settings;
    // 必须初始为 true：XAML 里绑定的 ValueChanged 在 InitializeComponent() 解析时就已挂上，
    // Slider 的 Value(0) 会被 Minimum 钳制并立刻触发一次 ValueChanged，
    // 那时构造函数尚未执行完、Loaded 更未触发，若为 false 会把 0 直接写回配置。
    private bool _suppressSliderEvents = true;
    // 当前调节的是哪一类默认外观（false=便利贴，true=任务清单）
    private bool _isTask;

    // 目标类型取当前生效的默认值（任务清单未单独设置时回退便利贴）
    private double CurrentOpacity => _isTask
        ? _settings.TaskListOpacity ?? _settings.WindowOpacity
        : _settings.WindowOpacity;
    private double CurrentFontSize => _isTask
        ? _settings.TaskListFontSize ?? _settings.DefaultFontSize
        : _settings.DefaultFontSize;
    private string CurrentColorHex => _isTask
        ? _settings.TaskListColor ?? _settings.DefaultColor
        : _settings.DefaultColor;
    private string CurrentTextColorHex => _isTask
        ? _settings.TaskListTextColor ?? _settings.NoteTextColor
        : _settings.NoteTextColor;

    // 任务清单是否已有任一维度被单独设置
    private bool AnyTaskOverridden =>
        _settings.TaskListColor != null
        || _settings.TaskListFontSize.HasValue
        || _settings.TaskListOpacity.HasValue
        || _settings.TaskListTextColor != null;

    public DefaultAppearanceWindow()
    {
        _settings = App.Current.Settings;
        InitializeComponent();

        // 兼容旧数据：默认字体大小无效时回退，避免被 Slider 钳到最小值
        if (_settings.DefaultFontSize <= 0)
            _settings.DefaultFontSize = 14;

        // 关键：Slider 必须在模板应用/布局完成后再赋值，
        // 否则构造函数中的赋值会被钳制回 Minimum，导致每次打开都显示最小值。
        Loaded += (_, _) =>
        {
            _suppressSliderEvents = true;
            OpacitySlider.Value = Math.Clamp(CurrentOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            FontSizeSlider.Value = Math.Clamp(CurrentFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
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

    // 切换「便利贴 / 任务清单」：按当前目标重载全部控件
    private void Target_Click(object sender, RoutedEventArgs e)
    {
        _isTask = ReferenceEquals(sender, TaskRadio);
        RefreshTargetUi();
    }

    // “跟随便利贴默认”：清除任务清单已单独设置的默认，恢复跟随便利贴
    private void FollowNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isTask) return;
        _settings.TaskListColor = null;
        _settings.TaskListFontSize = null;
        _settings.TaskListOpacity = null;
        _settings.TaskListTextColor = null;
        _settings.Save();
        RefreshTargetUi();
    }

    // 按当前目标重建色板、滑块与说明文案
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
            if (!_isTask)
                HeaderText.Text = "以下设置仅作为新建便利贴的默认外观，不影响已创建的便利贴与任务清单。";
            else if (!AnyTaskOverridden)
                HeaderText.Text = "新建任务清单的默认当前跟随便利贴。点击下方色块或拖动滑杆，即可单独设置新建任务清单的默认外观。";
            else
                HeaderText.Text = "以下设置仅作为新建任务清单的默认外观，不影响已创建的任务清单与便利贴。";
        }

        if (FollowNoteButton != null)
            FollowNoteButton.Visibility = _isTask && AnyTaskOverridden
                ? Visibility.Visible
                : Visibility.Collapsed;

        // 重建色板（局部函数自引用，保证之后点击其他颜色仍生效）
        void OnPickColor(string hex)
        {
            if (_isTask) _settings.TaskListColor = hex;
            else _settings.DefaultColor = hex;
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, hex, OnPickColor);
            FollowNoteButton.Visibility = _isTask && AnyTaskOverridden
                ? Visibility.Visible
                : Visibility.Collapsed;
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
            FollowNoteButton.Visibility = _isTask && AnyTaskOverridden
                ? Visibility.Visible
                : Visibility.Collapsed;
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
        UpdateFollowButtonVisibility();
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
        UpdateFollowButtonVisibility();
    }

    private void UpdateFollowButtonVisibility()
    {
        if (FollowNoteButton != null)
            FollowNoteButton.Visibility = _isTask && AnyTaskOverridden
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateOpacityLabel(double v)
    {
        OpacityValue.Text = $"{Math.Round(v * 100)}%";
    }
}
