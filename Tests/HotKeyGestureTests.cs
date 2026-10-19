using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class HotKeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", 3u, 0x48u)]
    [InlineData("Ctrl+Space", 2u, 0x20u)]
    [InlineData("Shift+OemPlus", 4u, 0xBBu)]
    [InlineData("Ctrl++", 2u, 0xBBu)]
    [InlineData("Ctrl+=", 2u, 0xBBu)]
    [InlineData("Alt+NumPad3", 1u, 0x63u)]
    [InlineData("Win+F12", 8u, 0x7Bu)]
    [InlineData(" ctrl + shift + 3 ", 6u, 0x33u)]
    public void SupportedCaptureNamesResolveToCorrectWindowsKeys(string text, uint modifiers, uint key)
    {
        Assert.True(HotKeyGesture.TryParse(text, out uint actualModifiers, out uint actualKey));
        Assert.Equal(modifiers, actualModifiers);
        Assert.Equal(key, actualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+H+J")]
    [InlineData("Ctrl+F13")]
    public void InvalidCombinationsAreRejected(string text)
        => Assert.False(HotKeyGesture.TryParse(text, out _, out _));
}
