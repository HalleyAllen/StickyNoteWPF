using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DpiChangedEventArgs = System.Windows.DpiChangedEventArgs;

namespace StickyNoteWPF.Services;

/// <summary>Updates a window background without decoding on its dispatcher or applying stale results.</summary>
public sealed class WindowBackgroundImage : IDisposable
{
    private readonly Window _window;
    private readonly Border _target;
    private readonly BackgroundImageLoader _loader = new();
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private CancellationTokenSource? _pending;
    private string? _path;
    private string _mode = "Fill";
    private double _opacity;
    private Brush _fallback = Brushes.White;
    private bool _disposed;
    public Task Loading { get; private set; } = Task.CompletedTask;

    public WindowBackgroundImage(Window window, Border target)
    {
        _window = window;
        _target = target;
        _window.SizeChanged += SizeChanged;
        _window.DpiChanged += DpiChanged;
        _resizeTimer.Tick += ResizeTick;
    }

    public void Apply(string? path, string mode, double opacity, Brush fallback)
    {
        if (_disposed) return;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        bool sameImage = string.Equals(path, _path, StringComparison.OrdinalIgnoreCase);
        _path = path;
        _mode = mode;
        _opacity = opacity;
        _fallback = fallback;
        if (string.IsNullOrWhiteSpace(path))
        {
            _resizeTimer.Stop();
            _loader.Clear();
            _target.Background = fallback;
            Loading = Task.CompletedTask;
            return;
        }
        if (sameImage && _target.Background is ImageBrush previous) Configure(previous);
        else _target.Background = fallback;

        var dpi = VisualTreeHelper.GetDpi(_window);
        int width = Pixels(_window.Width, dpi.DpiScaleX);
        int height = Pixels(_window.Height, dpi.DpiScaleY);
        // Center/tile use the image's native size. Preserve their existing scale semantics.
        if (mode is "Center" or "Tile") width = height = 0;
        _pending = new CancellationTokenSource();
        Loading = UpdateAsync(path, width, height, _pending.Token);
    }

    private async Task UpdateAsync(string path, int width, int height, CancellationToken token)
    {
        try
        {
            var image = await _loader.LoadAsync(path, width, height, token);
            if (_disposed || token.IsCancellationRequested) return;
            var brush = new ImageBrush(image);
            Configure(brush);
            _target.Background = brush;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_disposed || token.IsCancellationRequested) return;
            _target.Background = _fallback;
            Logger.LogException("WindowBackgroundImage.Load", ex);
        }
    }

    private void Configure(ImageBrush brush)
    {
        brush.Opacity = _opacity;
        brush.Stretch = _mode switch { "Center" or "Tile" => Stretch.None, "Stretch" => Stretch.Fill, _ => Stretch.UniformToFill };
        brush.TileMode = _mode == "Tile" ? TileMode.Tile : TileMode.None;
        brush.Viewport = new Rect(0, 0, _mode == "Tile" ? 0.25 : 1, _mode == "Tile" ? 0.25 : 1);
        brush.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
        brush.AlignmentX = AlignmentX.Center;
        brush.AlignmentY = AlignmentY.Center;
    }

    private static int Pixels(double size, double scale)
        => (int)(Math.Ceiling(Math.Clamp(double.IsFinite(size) ? size * scale : 512, 64, 4096) / 64) * 64);

    private void SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleResize();
    private void DpiChanged(object sender, DpiChangedEventArgs e) => ScheduleResize();
    private void ScheduleResize()
    {
        if (_disposed || string.IsNullOrWhiteSpace(_path)) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeTick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        Apply(_path, _mode, _opacity, _fallback);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resizeTimer.Stop();
        _resizeTimer.Tick -= ResizeTick;
        _window.SizeChanged -= SizeChanged;
        _window.DpiChanged -= DpiChanged;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        _loader.Clear();
    }
}
