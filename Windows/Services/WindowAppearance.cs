namespace StickyNoteWPF.Services;

public static class WindowAppearance
{
    public static double EffectiveOpacity(double storedOpacity)
        => !double.IsFinite(storedOpacity) ? 1 : Math.Clamp(storedOpacity, 0, 1);
}
