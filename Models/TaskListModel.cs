using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace StickyNoteWPF.Models;

public class TaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;
    private long _order;
    private bool _isExpanded = true;

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

    // 创建顺序序号：用于排序，未完成区内序号最大（最新）的排在最前（置顶）
    public long Order
    {
        get => _order;
        set { _order = value; OnPropertyChanged(); }
    }

    // 子任务集合（可嵌套多层）
    [JsonInclude]
    public ObservableCollection<TaskItem> SubItems { get; set; } = new();

    // 是否有子任务（控制展开箭头显示）
    [JsonIgnore]
    public bool HasSubItems => SubItems.Count > 0;

    // 是否展开显示子任务（仅 UI 状态，不持久化）
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    // 子任务增减后调用，刷新 HasSubItems 相关绑定
    public void RefreshHasSubItems() => OnPropertyChanged(nameof(HasSubItems));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TaskListModel : INotifyPropertyChanged
{
    private string _title = "任务清单";
    private string _color = "#FFF7A900";
    private string _textColor = "#FF222222";
    private string? _backgroundImagePath;
    private string _backgroundImageMode = "Fill";
    private double _opacity = 1.0;
    private double _fontSize = 16;
    private string _fontFamily = "Microsoft YaHei";
    private bool _forceShow;
    private bool _hoverToShow;
    private bool _isLocked;
    private bool _isOpen;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }
    public string TextColor
    {
        get => _textColor;
        set { _textColor = value; OnPropertyChanged(); }
    }
    public string? BackgroundImagePath
    {
        get => _backgroundImagePath;
        set { _backgroundImagePath = value; OnPropertyChanged(); }
    }
    public string BackgroundImageMode
    {
        get => _backgroundImageMode;
        set { _backgroundImageMode = value; OnPropertyChanged(); }
    }
    public double Opacity
    {
        get => _opacity;
        set { _opacity = value; OnPropertyChanged(); }
    }
    public double FontSize
    {
        get => _fontSize;
        set { _fontSize = value; OnPropertyChanged(); }
    }
    public string FontFamily
    {
        get => _fontFamily;
        set { _fontFamily = value; OnPropertyChanged(); }
    }
    public bool ForceShow
    {
        get => _forceShow;
        set { _forceShow = value; OnPropertyChanged(); }
    }
    public bool HoverToShow
    {
        get => _hoverToShow;
        set { _hoverToShow = value; OnPropertyChanged(); }
    }

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;

    public bool IsLocked
    {
        get => _isLocked;
        set { _isLocked = value; OnPropertyChanged(); }
    }
    public bool IsOpen
    {
        get => _isOpen;
        set { _isOpen = value; OnPropertyChanged(); }
    }

    [JsonInclude]
    public ObservableCollection<TaskItem> Items { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
