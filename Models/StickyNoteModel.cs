namespace StickyNoteWPF.Models;

public class StickyNoteModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty; // 便签标题，用于区分；空则用默认"便利贴"
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; } = 200;
    public double Top { get; set; } = 200;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 220;
    public string Color { get; set; } = "#FFF7A900"; // 默认黄色
    public string BackgroundImagePath { get; set; } = string.Empty; // 图片背景路径，空则用纯色
    public string BackgroundImageMode { get; set; } = "Stretch";   // 图片填充方式：Stretch / Tile
    public double FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FF222222"; // 便签文字颜色
    public double Opacity { get; set; } = 1.0;           // 便签窗口透明度
    public bool HoverToShow { get; set; } = false;       // 鼠标移到该便签区域才显示，移开隐藏
    public bool IsLocked { get; set; } = false;           // 锁定后便签内容只读，不可编辑
    public bool IsOpen { get; set; } = false;             // 是否处于打开（显示）状态，用于记住下次启动恢复
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
