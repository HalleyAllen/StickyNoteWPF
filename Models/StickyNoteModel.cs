namespace StickyNoteWPF.Models;

public class StickyNoteModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; } = 200;
    public double Top { get; set; } = 200;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 220;
    public string Color { get; set; } = "#FFF7A900"; // 默认黄色
    public double FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FF222222"; // 便签文字颜色
    public double Opacity { get; set; } = 1.0;           // 便签窗口透明度
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
