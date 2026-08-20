using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace StickyNoteWPF.Models;

public class TaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public bool IsDone
    {
        get => _isDone;
        set { _isDone = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public class TaskListModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "任务清单";

    public string Color { get; set; } = "#FFF7A900";
    public string TextColor { get; set; } = "#FF222222";
    public string? BackgroundImagePath { get; set; }
    public string BackgroundImageMode { get; set; } = "Fill"; // Fill / Stretch / Tile / Center
    public double Opacity { get; set; } = 1.0;
    public double FontSize { get; set; } = 16;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public bool ForceShow { get; set; }
    public bool HoverToShow { get; set; }

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    public bool IsLocked { get; set; }
    public bool IsOpen { get; set; }

    [JsonInclude]
    public ObservableCollection<TaskItem> Items { get; set; } = new();
}
