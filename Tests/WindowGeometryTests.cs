using System.Text.Json;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class WindowGeometryTests
{
    [Fact]
    public void PreservesPlacementOnMonitorWithNegativeCoordinates()
    {
        var saved = new WindowBounds(-1400, 100, 320, 360);
        var screens = new[] { new WindowBounds(0, 0, 1920, 1040), new WindowBounds(-1600, 0, 1600, 900) };
        Assert.Equal(saved, WindowGeometry.Constrain(saved, screens, 320, 360));
    }

    [Fact]
    public void RemovedMonitorPlacementReturnsToVisibleWorkArea()
    {
        var result = WindowGeometry.Constrain(new(-1800, -100, 320, 360),
            new[] { new WindowBounds(0, 0, 1920, 1040) }, 320, 360);
        Assert.Equal(new WindowBounds(0, 0, 320, 360), result);
    }

    [Fact]
    public void InvalidOrOversizedBoundsAreNormalized()
    {
        var result = WindowGeometry.Constrain(new(double.NaN, double.PositiveInfinity, -1, 9000),
            new[] { new WindowBounds(0, 0, 1280, 720) }, 320, 360);
        Assert.Equal(new WindowBounds(200, 0, 320, 720), result);
    }

    [Fact]
    public void LegacyTaskListsGetFiniteDefaultsAndSizesRoundTrip()
    {
        var old = JsonSerializer.Deserialize<TaskListModel>("{\"Title\":\"legacy\"}")!;
        Assert.Equal(320, old.Width);
        old.Width = 480;
        old.Height = 640;
        var restored = JsonSerializer.Deserialize<TaskListModel>(JsonSerializer.Serialize(old))!;
        Assert.Equal(480, restored.Width);
        Assert.Equal(640, restored.Height);
    }
}
