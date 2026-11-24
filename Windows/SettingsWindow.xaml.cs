using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public enum SettingsSection { General, NoteAppearance, TaskAppearance, ServerSync, PeerSync }

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _ready, _closing, _allowClose;
    private SyncPage? _syncPage;
    private PeerSyncPage? _peerPage;

    public SettingsWindow(AppSettings settings, SettingsSection section = SettingsSection.General)
    {
        _settings = settings;
        InitializeComponent();

        StartupCheck.IsChecked = AppSettings.IsStartupEnabled();
        TopmostCheck.IsChecked = _settings.GlobalTopmost;
        DarkModeCheck.IsChecked = _settings.DarkMode;
        DarkModeCheck.Checked += (_, _) => SetDarkMode(true);
        DarkModeCheck.Unchecked += (_, _) => SetDarkMode(false);

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
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
        _ready = true;
        SelectSection(section);
        Loaded += (_, _) =>
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        };
        Closing += SettingsWindow_Closing;
    }

    private void SetDarkMode(bool enabled)
    {
        _settings.DarkMode = enabled;
        ThemeManager.Apply(enabled);
        _settings.Save();
    }

    // ====== 全局快捷键设置 ======
    public void SelectSection(SettingsSection section)
    {
        SettingsTabs.SelectedIndex = (int)section;
        LoadSection();
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || e.Source != SettingsTabs) return;
        _capturingHotKey = false;
        EndHotKeyCapture();
        LoadSection();
    }

    private void LoadSection()
    {
        if (!_ready) return;
        switch ((SettingsSection)SettingsTabs.SelectedIndex)
        {
            case SettingsSection.NoteAppearance: NoteAppearanceHost.Content ??= new DefaultAppearancePage(false); break;
            case SettingsSection.TaskAppearance: TaskAppearanceHost.Content ??= new DefaultAppearancePage(true); break;
            case SettingsSection.ServerSync: ServerSyncHost.Content ??= _syncPage = new SyncPage(); break;
            case SettingsSection.PeerSync: PeerSyncHost.Content ??= _peerPage = new PeerSyncPage(); break;
        }
    }

    private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_syncPage?.IsBusy == true || _peerPage?.IsBusy == true)
        {
            e.Cancel = true;
            SettingsStatus.Text = "请等待当前同步操作结束后再关闭设置。";
            return;
        }
        if (_peerPage is null) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await _peerPage.ClosePageAsync();
                _allowClose = true;
                Close();
            }
            catch (Exception ex) { _closing = false; SettingsStatus.Text = "无法停止设备接收：" + ex.Message; }
        }));
    }

    private bool _capturingHotKey;

    private void BeginHotKeyCapture()
    {
        _capturingHotKey = true;
        HotKeyBox.SetResourceReference(Border.BorderBrushProperty, "UiAccent");
        HotKeyText.Text = "请按下新快捷键…（Esc 取消）";
        HotKeyText.SetResourceReference(TextBlock.ForegroundProperty, "UiAccent");
    }

    private void EndHotKeyCapture()
    {
        _capturingHotKey = false;
        HotKeyBox.SetResourceReference(Border.BorderBrushProperty, "UiLine");
        HotKeyText.SetResourceReference(TextBlock.ForegroundProperty, "UiMuted");
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
            HotKeyText.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
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
}
