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
        set { if (_text == value) return; _text = value; OnPropertyChanged(); }
    }

    public bool IsDone
    {
        get => _isDone;
        set { if (_isDone == value) return; _isDone = value; OnPropertyChanged(); }
    }

    // 创建顺序序号：用于排序，未完成区内序号最大（最新）的排在最前（置顶）
    public long Order
    {
        get => _order;
        set { if (_order == value) return; _order = value; OnPropertyChanged(); }
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
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
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
    private bool _showCompleted = true;
    private bool _isLocked;
    private bool _isOpen;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; OnPropertyChanged(); }
    }

    public string Color
    {
        get => _color;
        set { if (_color == value) return; _color = value; OnPropertyChanged(); }
    }
    public string TextColor
    {
        get => _textColor;
        set { if (_textColor == value) return; _textColor = value; OnPropertyChanged(); }
    }
    public string? BackgroundImagePath
    {
        get => _backgroundImagePath;
        set { if (_backgroundImagePath == value) return; _backgroundImagePath = value; OnPropertyChanged(); }
    }
    public string BackgroundImageMode
    {
        get => _backgroundImageMode;
        set { if (_backgroundImageMode == value) return; _backgroundImageMode = value; OnPropertyChanged(); }
    }
    public double Opacity
    {
        get => _opacity;
        set { if (_opacity == value) return; _opacity = value; OnPropertyChanged(); }
    }
    public double FontSize
    {
        get => _fontSize;
        set { if (_fontSize == value) return; _fontSize = value; OnPropertyChanged(); }
    }
    public string FontFamily
    {
        get => _fontFamily;
        set { if (_fontFamily == value) return; _fontFamily = value; OnPropertyChanged(); }
    }
    public bool ForceShow
    {
        get => _forceShow;
        set { if (_forceShow == value) return; _forceShow = value; OnPropertyChanged(); }
    }
    public bool HoverToShow
    {
        get => _hoverToShow;
        set { if (_hoverToShow == value) return; _hoverToShow = value; OnPropertyChanged(); }
    }

    // 是否显示已勾选完成的任务。新建清单的初始值来自「默认外观（任务清单）」的开关，
    // 创建后可在该清单的 ⚙ 设置中单独修改。旧数据缺省为 true（始终显示）。
    public bool ShowCompleted
    {
        get => _showCompleted;
        set { if (_showCompleted == value) return; _showCompleted = value; OnPropertyChanged(); }
    }

    public double Left { get; set; } = 200;
    public double Top { get; set; } = 150;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 360;

    public bool IsLocked
    {
        get => _isLocked;
        set { if (_isLocked == value) return; _isLocked = value; OnPropertyChanged(); }
    }
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen == value) return; _isOpen = value; OnPropertyChanged(); }
    }

    [JsonInclude]
    public ObservableCollection<TaskItem> Items { get; set; } = new();

    [JsonIgnore]
    public string ProgressText => Items.Count == 0 ? "暂无任务" : $"已完成 {Items.Count(i => i.IsDone)}/{Items.Count}";

    public void NotifyTaskChanges() => OnPropertyChanged(nameof(ProgressText));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
