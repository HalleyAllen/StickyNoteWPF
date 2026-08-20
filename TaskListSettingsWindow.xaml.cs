using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;

namespace StickyNoteWPF;

public partial class TaskListSettingsWindow : Window
{
    private readonly TaskListWindow _owner;
    private readonly TaskListModel _list;
    private bool _suppressSliderEvents = true;

    public TaskListSettingsWindow(TaskListWindow owner)
    {
        _owner = owner;
        _list = owner.List;
        InitializeComponent();

        var s = App.Current.Settings;
        if (s.NoteSettingsWidth > 0 && s.NoteSettingsHeight > 0)
        {
            Width = s.NoteSettingsWidth;
            Height = s.NoteSettingsHeight;
        }

        void SetBg(string hex)
        {
            _list.Color = hex;
            App.Current.RefreshTaskList(_list, true);
            AppearanceHelper.BuildColorSwatches(BgColorPanel, App.Current.Settings, hex, SetBg);
        }
        void SetText(string hex)
        {
            _list.TextColor = hex;
            App.Current.RefreshTaskList(_list, true);
            AppearanceHelper.BuildColorSwatches(TextColorPanel, App.Current.Settings, hex, SetText);
        }

        AppearanceHelper.BuildColorSwatches(BgColorPanel, App.Current.Settings, _list.Color, SetBg);
        AppearanceHelper.BuildColorSwatches(TextColorPanel, App.Current.Settings, _list.TextColor, SetText);

        if (_list.FontSize <= 0)
            _list.FontSize = App.Current?.Settings.DefaultFontSize ?? 16;

        FontSizeValue.Text = $"{Math.Round(_list.FontSize)}";
        OpacityValue.Text = $"{Math.Round(_list.Opacity * 100)}%";

        Loaded += (_, _) =>
        {
            _suppressSliderEvents = true;
            FontSizeSlider.Value = Math.Clamp(_list.FontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            OpacitySlider.Value = Math.Clamp(_list.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            _suppressSliderEvents = false;

            FontSizeValue.Text = $"{Math.Round(FontSizeSlider.Value)}";
            OpacityValue.Text = $"{Math.Round(OpacitySlider.Value * 100)}%";
        };

        TitleBox.Text = _list.Title;

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
        App.Current.SaveAll();
        _owner.RefreshFromModel();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvents) return;
        var v = e.NewValue;
        _list.FontSize = v;
        FontSizeValue.Text = $"{Math.Round(v)}";
        Commit();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvents) return;
        var v = e.NewValue;
        _list.Opacity = v;
        OpacityValue.Text = $"{Math.Round(v * 100)}%";
        Commit();
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _list.Title = string.Equals(TitleBox.Text.Trim(), "任务清单", System.StringComparison.Ordinal)
            ? string.Empty
            : TitleBox.Text;
        App.Current.RefreshTaskList(_list, true);
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
            _list.BackgroundImagePath = dlg.FileName;
            App.Current.RefreshTaskList(_list, true);
        }
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        _list.BackgroundImagePath = null;
        App.Current.RefreshTaskList(_list, true);
    }
}
