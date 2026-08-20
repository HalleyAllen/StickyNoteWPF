using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class StickyNoteSettingsWindow : Window
{
    private readonly StickyNoteWindow _owner;
    private readonly StickyNoteModel _note;
    // 必须初始为 true：XAML 里绑定的 ValueChanged 在 InitializeComponent() 解析时就已挂上，
    // Slider 的 Value(0) 会被 Minimum(8) 强制钳成 8 并立刻触发一次 ValueChanged，
    // 那时构造函数尚未执行完、Loaded 更未触发，若为 false 会把 8 直接写回便签数据。
    private bool _suppressSliderEvents = true;

    public StickyNoteSettingsWindow(StickyNoteWindow owner)
    {
        _owner = owner;
        _note = owner.Note;
        InitializeComponent();

        // 恢复上次保存的便签设置窗口大小
        var s = App.Current.Settings;
        if (s.NoteSettingsWidth > 0 && s.NoteSettingsHeight > 0)
        {
            Width = s.NoteSettingsWidth;
            Height = s.NoteSettingsHeight;
        }

        // 颜色设置后用本地函数重绘，保证 onPick 始终有效（避免重绘传入 null 导致后续点击无反应）
        void SetBg(string hex)
        {
            _note.Color = hex;
            // 刷新便签窗口外观 + 保存 + 同步管理界面列表背景
            App.Current.RefreshNote(_note, true);
            AppearanceHelper.BuildColorSwatches(BgColorPanel, App.Current.Settings, hex, SetBg);
        }
        void SetText(string hex)
        {
            _note.TextColor = hex;
            App.Current.RefreshNote(_note, true);
            AppearanceHelper.BuildColorSwatches(TextColorPanel, App.Current.Settings, hex, SetText);
        }

        AppearanceHelper.BuildColorSwatches(BgColorPanel, App.Current.Settings, _note.Color, SetBg);
        AppearanceHelper.BuildColorSwatches(TextColorPanel, App.Current.Settings, _note.TextColor, SetText);

        // 兼容旧数据：字体大小无效（<=0）时回退默认，避免被 Slider 钳到最小值并误写回
        if (_note.FontSize <= 0)
            _note.FontSize = App.Current?.Settings.DefaultFontSize ?? 14;

        // 文本先显示真实值（不依赖 Slider）
        FontSizeValue.Text = $"{Math.Round(_note.FontSize)}";
        OpacityValue.Text = $"{Math.Round(_note.Opacity * 100)}%";

        // 关键：必须等模板应用/布局完成后再赋值滑块。
        // 在构造函数里直接赋值时 Slider 模板尚未应用，Value 会被钳制回 Minimum(8)，
        // 导致每次打开都显示 8（虽然 _suppressSliderEvents 阻止了写回磁盘，但显示仍是错的）。
        Loaded += (_, _) =>
        {
            _suppressSliderEvents = true;
            FontSizeSlider.Value = Math.Clamp(_note.FontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            OpacitySlider.Value = Math.Clamp(_note.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            _suppressSliderEvents = false;

            FontSizeValue.Text = $"{Math.Round(FontSizeSlider.Value)}";
            OpacityValue.Text = $"{Math.Round(OpacitySlider.Value * 100)}%";
        };

        TitleBox.Text = _note.Title;

        CloseButton.Click += (_, _) => Close();
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        Closing += (_, _) =>
        {
            var st = App.Current.Settings;
            st.NoteSettingsWidth = Width;
            st.NoteSettingsHeight = Height;
            st.Save();
        };
    }

    private void Commit()
    {
        // 先保存（含刚改的字体大小）到磁盘，再刷新便签窗口显示，避免刷新覆盖未保存的值
        App.Current.SaveAll();
        _owner.RefreshFromModel();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_note == null || _suppressSliderEvents) return;
        var v = e.NewValue;
        _note.FontSize = v;
        FontSizeValue.Text = $"{Math.Round(v)}";
        Commit();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_note == null || _suppressSliderEvents) return;
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

    private void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
            Title = "选择背景图片"
        };
        if (dlg.ShowDialog() == true)
        {
            _note.BackgroundImagePath = dlg.FileName;
            App.Current.RefreshNote(_note, true);
        }
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        _note.BackgroundImagePath = string.Empty;
        App.Current.RefreshNote(_note, true);
    }
}
