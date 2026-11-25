using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;

namespace StickyNoteWPF.Services;

public static class WindowPlacement
{
    public static void Restore(Window window, WindowBounds saved, double defaultWidth, double defaultHeight)
    {
        Apply(window, WindowGeometry.Constrain(saved, Array.Empty<WindowBounds>(), defaultWidth, defaultHeight,
            Math.Max(160, window.MinWidth), Math.Max(120, window.MinHeight)));
        window.SourceInitialized += Constrain;

        void Constrain(object? sender, EventArgs e)
        {
            window.SourceInitialized -= Constrain;
            var screens = Screen.AllScreens.Select(screen => GetWorkArea(window, screen)).ToArray();
            Apply(window, WindowGeometry.Constrain(new(window.Left, window.Top, window.Width, window.Height),
                screens, defaultWidth, defaultHeight, Math.Max(160, window.MinWidth), Math.Max(120, window.MinHeight)));
        }
    }

    public static void PlaceBeside(Window dialog, Window owner, double gap = 12)
    {
        var screen = GetWorkArea(owner, Screen.FromHandle(new WindowInteropHelper(owner).Handle));
        double left = owner.Left + owner.Width + gap;
        if (left + dialog.Width > screen.Left + screen.Width) left = owner.Left - dialog.Width - gap;
        Apply(dialog, WindowGeometry.Constrain(new(left, owner.Top, dialog.Width, dialog.Height),
            new[] { screen }, dialog.Width, dialog.Height, dialog.MinWidth, dialog.MinHeight));
    }

    private static WindowBounds GetWorkArea(Window window, Screen screen)
    {
        // WinForms reports physical pixels; WPF window placement uses device-independent units.
        var matrix = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var area = screen.WorkingArea;
        var origin = matrix.Transform(new System.Windows.Point(area.Left, area.Top));
        var size = matrix.Transform(new Vector(area.Width, area.Height));
        return new(origin.X, origin.Y, size.X, size.Y);
    }

    private static void Apply(Window window, WindowBounds bounds)
    {
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }
}
