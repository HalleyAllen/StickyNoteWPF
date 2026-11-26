using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class WindowAppearanceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.25)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 1)]
    public void RenderingUsesStoredOpacity(double stored, double expected)
    {
        Assert.Equal(expected, WindowAppearance.EffectiveOpacity(stored));
    }
}
