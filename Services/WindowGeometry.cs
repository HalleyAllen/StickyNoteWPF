namespace StickyNoteWPF.Services;

public readonly record struct WindowBounds(double Left, double Top, double Width, double Height);

public static class WindowGeometry
{
    public static WindowBounds Constrain(WindowBounds saved, IReadOnlyList<WindowBounds> screens,
        double defaultWidth, double defaultHeight, double minWidth = 160, double minHeight = 120)
    {
        double width = double.IsFinite(saved.Width) && saved.Width >= minWidth ? saved.Width : defaultWidth;
        double height = double.IsFinite(saved.Height) && saved.Height >= minHeight ? saved.Height : defaultHeight;
        double left = double.IsFinite(saved.Left) ? saved.Left : 200;
        double top = double.IsFinite(saved.Top) ? saved.Top : 150;
        if (screens.Count == 0) return new(left, top, width, height);

        var screen = screens.OrderByDescending(area =>
                Math.Max(0, Math.Min(left + width, area.Left + area.Width) - Math.Max(left, area.Left)) *
                Math.Max(0, Math.Min(top + height, area.Top + area.Height) - Math.Max(top, area.Top)))
            .ThenBy(area => Math.Pow(left - Math.Clamp(left, area.Left, area.Left + area.Width), 2) +
                Math.Pow(top - Math.Clamp(top, area.Top, area.Top + area.Height), 2))
            .First();
        width = Math.Max(minWidth, Math.Min(width, screen.Width));
        height = Math.Max(minHeight, Math.Min(height, screen.Height));
        left = Math.Clamp(left, screen.Left, Math.Max(screen.Left, screen.Left + screen.Width - width));
        top = Math.Clamp(top, screen.Top, Math.Max(screen.Top, screen.Top + screen.Height - height));
        return new(left, top, width, height);
    }
}
