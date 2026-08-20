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

        // 默认背景颜色：仅作为新建便签的默认值，不影响已创建的便签。
        // 用局部函数自引用，重建色板时继续传入自身，保证之后点击其他颜色仍生效
        void SetDefaultColor(string hex)
        {
            _settings.DefaultColor = hex;
            _settings.Save();
            AppearanceHelper.BuildColorSwatches(ColorPanel, _settings, hex, SetDefaultColor);
        }
        // 统一的文字与按钮颜色：仅作为新建便签的默认值，不影响已创建的便签
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

        // 全局快捷键：隐藏/显示所有窗口（设置窗口除外）
        HotKeyText.Text = string.IsNullOrWhiteSpace(_settings.ToggleWindowsHotKey)
            ? "未设置"
            : _settings.ToggleWindowsHotKey;
        SetHotKeyButton.Click += (_, _) => BeginHotKeyCapture();
        ClearHotKeyButton.Click += (_, _) => ClearHotKey();
        PreviewKeyDown += HotKeyCapture_PreviewKeyDown;
    }

    // ====== 全局快捷键设置 ======

    private bool _capturingHotKey;

    private void BeginHotKeyCapture()
    {
        _capturingHotKey = true;
        HotKeyBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x8C, 0xFF));
        HotKeyText.Text = "请按下新快捷键…（Esc 取消）";
        HotKeyText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x8C, 0xFF));
    }

    private void EndHotKeyCapture()
    {
        _capturingHotKey = false;
        HotKeyBox.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        HotKeyText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
        HotKeyText.Text = string.IsNullOrWhiteSpace(_settings.ToggleWindowsHotKey)
            ? "未设置"
            : _settings.ToggleWindowsHotKey;
    }

    private void ClearHotKey()
    {
        _capturingHotKey = false;
        _settings.ToggleWindowsHotKey = string.Empty;
        _settings.Save();
        App.Current.UnregisterToggleHotKey();
        EndHotKeyCapture();
    }

    private void HotKeyCapture_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotKey) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndHotKeyCapture();
            return;
        }

        // 只按了修饰键（Ctrl/Alt/Shift/Win），继续等待主键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return; // 必须带修饰键，避免误设纯字母键

        var main = KeyToDisplay(e.Key);
        if (main == null) return;

        var combo = string.Empty;
        if (mods.HasFlag(ModifierKeys.Control)) combo += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) combo += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) combo += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) combo += "Win+";
        combo += main;

        // 注册全局热键；失败（组合被占用等）则提示并保持捕获状态
        if (App.Current.RegisterToggleHotKey(combo))
        {
            _settings.ToggleWindowsHotKey = combo;
            _settings.Save();
            _capturingHotKey = false;
            EndHotKeyCapture();
        }
        else
        {
            HotKeyText.Text = "该组合无法注册，请换一个";
            HotKeyText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
        }
    }

    // 将 WPF 的 Key 转为可读的按键名（与 HotKeyService 解析格式一致）
    private static string? KeyToDisplay(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key >= Key.F1 && key <= Key.F12) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            _ => null
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

}
