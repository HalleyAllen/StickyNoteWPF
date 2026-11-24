using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace StickyNoteWPF.Services;

internal sealed class NoteBackground : IDisposable
{
    private static readonly DropShadowEffect Shadow = CreateShadow();
    private readonly Border _surface;
    private readonly Grid _titleBar;
    private readonly DispatcherTimer _resizeTimer;
    private readonly Action _refresh;

    public NoteBackground(Border surface, Grid titleBar, Action refresh)
    {
        _surface = surface;
        _titleBar = titleBar;
        _refresh = refresh;
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _resizeTimer.Tick += ResizeTick;
        _surface.Effect = Shadow;
    }

    public void Apply(string? path, string color, string mode, double opacity, double width, double height)
    {
        _resizeTimer.Stop();
        opacity = Math.Clamp(opacity, 0, 1);
        System.Windows.Media.Brush? background = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(_surface);
                var image = BackgroundImageCache.Load(path, (int)Math.Ceiling(width * dpi.DpiScaleX),
                    (int)Math.Ceiling(height * dpi.DpiScaleY), mode is "Center" or "Tile", mode != "Stretch");
                var brush = _surface.Background as ImageBrush;
                if (brush is null || !ReferenceEquals(brush.ImageSource, image)) brush = new ImageBrush(image);
                brush.Stretch = mode is "Tile" or "Center" ? Stretch.None : mode == "Stretch" ? Stretch.Fill : Stretch.UniformToFill;
                brush.TileMode = mode == "Tile" ? TileMode.Tile : TileMode.None;
                brush.Viewport = mode == "Tile" ? new Rect(0, 0, 0.25, 0.25) : new Rect(0, 0, 1, 1);
                brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
                brush.AlignmentX = AlignmentX.Center;
                brush.AlignmentY = AlignmentY.Center;
                brush.Opacity = opacity;
                background = brush;
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.IO.FileFormatException)
            {
                // 缺失或损坏的图片按便签颜色显示，原路径仍保存在模型中。
            }
        }
        if (background is null)
        {
            var parsed = AppearanceHelper.ParseColor(color, Colors.White);
            parsed.A = (byte)(opacity * 255);
            background = FrozenBrush(parsed);
        }
        _surface.Background = background;
        _surface.BorderBrush = FrozenBrush(Color.FromArgb((byte)(opacity * 0x55), 0, 0, 0));
        _titleBar.Background = FrozenBrush(Color.FromArgb((byte)(opacity * 0x22), 0, 0, 0));
    }

    public void Resize()
    {
        if (_surface.Background is not ImageBrush) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }
    private void ResizeTick(object? sender, EventArgs e) { _resizeTimer.Stop(); _refresh(); }
    public void Dispose()
    {
        _resizeTimer.Stop();
        _resizeTimer.Tick -= ResizeTick;
        _surface.Background = null;
        _surface.Effect = null;
    }
    private static SolidColorBrush FrozenBrush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static DropShadowEffect CreateShadow()
    {
        var effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 3, Opacity = 0.3 };
        effect.Freeze(); return effect;
    }
}
