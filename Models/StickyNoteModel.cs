using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StickyNoteWPF.Models;

public class StickyNoteModel : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    } // 便签标题，用于区分；空则用默认"便利贴"
    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
    public double Left { get; set; } = 200;
    public double Top { get; set; } = 200;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 220;
    private string _color = "#FFF7A900";
    public string Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    } // 默认黄色
    public string BackgroundImagePath { get; set; } = string.Empty; // 图片背景路径，空则用纯色
    public string BackgroundImageMode { get; set; } = "Stretch";   // 图片填充方式：Stretch / Tile
    public double FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FF222222"; // 便签文字颜色
    public double Opacity { get; set; } = 1.0;           // 便签窗口透明度
    public bool HoverToShow { get; set; } = false;       // 鼠标移到该便签区域才显示，移开隐藏
    public bool IsLocked { get; set; } = false;           // 锁定后便签内容只读，不可编辑
    public bool IsOpen { get; set; } = false;             // 是否处于打开（显示）状态，用于记住下次启动恢复
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public event PropertyChangedEventHandler? PropertyChanged;
}
