using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StickyNoteWPF.Models;

namespace StickyNoteWPF;

public partial class TaskListSettingsWindow : Window
{
    private readonly TaskListWindow _owner;
    private readonly TaskListModel _list;
    private bool _suppressSliderEvents = true;

    private static readonly string[] Palette =
    {
        "#FFF7A900", "#FFFFF275", "#FFA0E7A0", "#FF9AD0EC",
        "#FFE6A8E0", "#FFFFB3A7", "#FFD6C8FF", "#FFC0C0C0"
    };

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
            BuildSwatches(BgColorPanel, hex, SetBg);
        }
        void SetText(string hex)
        {
            _list.TextColor = hex;
            App.Current.RefreshTaskList(_list, true);
            BuildSwatches(TextColorPanel, hex, SetText);
        }

        BgColorPanel.Children.Add(MakeCustomSwatch(SetBg));
        TextColorPanel.Children.Add(MakeCustomSwatch(SetText));
        BuildSwatches(BgColorPanel, _list.Color, SetBg);
        BuildSwatches(TextColorPanel, _list.TextColor, SetText);

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

    private Border MakeCustomSwatch(Action<string> onPicked)
    {
        var btn = new Border
        {
            Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Child = new TextBlock
        {
            Text = "✎", FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        btn.MouseLeftButtonDown += (_, _) => PickCustom(hex =>
        {
            onPicked?.Invoke(hex);
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
                    ? new SolidColorBrush(Colors.Black)
                    : new SolidColorBrush(Colors.Gray),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = hex;
            btn.MouseLeftButtonDown += (_, _) => onPick?.Invoke(captured);
            panel.Children.Add(btn);
        }
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
}
