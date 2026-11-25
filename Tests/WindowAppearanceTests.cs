using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class WindowAppearanceTests
{
    [Theory]
    [InlineData(0.25, true, 1)]
    [InlineData(0, true, 1)]
    [InlineData(0.25, false, 0.25)]
    [InlineData(-1, false, 0)]
    [InlineData(2, false, 1)]
    [InlineData(double.NaN, false, 1)]
    public void ForceShowOverridesRenderingWithoutChangingStoredPreference(double stored, bool force, double expected)
    {
        Assert.Equal(expected, WindowAppearance.EffectiveOpacity(stored, force));
        if (double.IsFinite(stored) && stored is >= 0 and <= 1)
            Assert.Equal(stored, WindowAppearance.EffectiveOpacity(stored, false));
    }
}
