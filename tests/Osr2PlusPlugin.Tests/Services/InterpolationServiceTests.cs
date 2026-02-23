using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class InterpolationServiceTests
{
    private readonly InterpolationService _sut = new();

    private static FunscriptData MakeScript(params (long at, int pos)[] points)
    {
        var data = new FunscriptData { AxisId = "L0" };
        foreach (var (at, pos) in points)
            data.Actions.Add(new FunscriptAction(at, pos));
        return data;
    }

    // --- Empty actions ---

    [Fact]
    public void GetPosition_EmptyActions_Returns50()
    {
        var script = MakeScript();

        var result = _sut.GetPosition(script, 1000, "L0");

        Assert.Equal(50.0, result);
    }

    // --- Single action ---

    [Fact]
    public void GetPosition_SingleAction_ReturnsItsPos()
    {
        var script = MakeScript((1000, 75));

        Assert.Equal(75.0, _sut.GetPosition(script, 500, "L0"));
        Assert.Equal(75.0, _sut.GetPosition(script, 1000, "L0"));
        Assert.Equal(75.0, _sut.GetPosition(script, 2000, "L0"));
    }

    // --- Before first action ---

    [Fact]
    public void GetPosition_BeforeFirstAction_ReturnsFirstPos()
    {
        var script = MakeScript((1000, 0), (2000, 100));

        var result = _sut.GetPosition(script, 500, "L0");

        Assert.Equal(0.0, result);
    }

    // --- After last action ---

    [Fact]
    public void GetPosition_AfterLastAction_ReturnsLastPos()
    {
        var script = MakeScript((1000, 0), (2000, 100));

        var result = _sut.GetPosition(script, 3000, "L0");

        Assert.Equal(100.0, result);
    }

    // --- Linear interpolation ---

    [Fact]
    public void GetPosition_Midpoint_InterpolatesLinearly()
    {
        var script = MakeScript((1000, 0), (2000, 100));

        var result = _sut.GetPosition(script, 1500, "L0");

        Assert.Equal(50.0, result);
    }

    [Fact]
    public void GetPosition_QuarterPoint_InterpolatesCorrectly()
    {
        var script = MakeScript((1000, 0), (2000, 100));

        var result = _sut.GetPosition(script, 1250, "L0");

        Assert.Equal(25.0, result);
    }

    [Fact]
    public void GetPosition_AtExactActionTime_ReturnsExactPos()
    {
        var script = MakeScript((1000, 0), (2000, 50), (3000, 100));

        Assert.Equal(0.0, _sut.GetPosition(script, 1000, "L0"));
        Assert.Equal(50.0, _sut.GetPosition(script, 2000, "L0"));
        Assert.Equal(100.0, _sut.GetPosition(script, 3000, "L0"));
    }

    // --- Sequential advance (O(1)) ---

    [Fact]
    public void GetPosition_SequentialCalls_AdvancesCorrectly()
    {
        var script = MakeScript(
            (0, 0), (1000, 100), (2000, 0), (3000, 100), (4000, 0));

        // Sequential playback — should use cached index advancement
        Assert.Equal(50.0, _sut.GetPosition(script, 500, "L0"));
        Assert.Equal(100.0, _sut.GetPosition(script, 1000, "L0"));
        Assert.Equal(50.0, _sut.GetPosition(script, 1500, "L0"));
        Assert.Equal(0.0, _sut.GetPosition(script, 2000, "L0"));
        Assert.Equal(50.0, _sut.GetPosition(script, 2500, "L0"));
        Assert.Equal(100.0, _sut.GetPosition(script, 3000, "L0"));
    }

    // --- Seek backward ---

    [Fact]
    public void GetPosition_SeekBackward_FallsBackToBinarySearch()
    {
        var script = MakeScript(
            (0, 0), (1000, 100), (2000, 0), (3000, 100));

        // Advance to near the end
        _sut.GetPosition(script, 2500, "L0");

        // Seek backward
        var result = _sut.GetPosition(script, 500, "L0");

        Assert.Equal(50.0, result);
    }

    // --- Seek forward ---

    [Fact]
    public void GetPosition_SeekForward_AdvancesCorrectly()
    {
        var script = MakeScript(
            (0, 0), (1000, 100), (2000, 0), (3000, 100));

        // Start at beginning
        _sut.GetPosition(script, 100, "L0");

        // Jump forward
        var result = _sut.GetPosition(script, 2500, "L0");

        Assert.Equal(50.0, result);
    }

    // --- Multiple axes use separate caches ---

    [Fact]
    public void GetPosition_DifferentAxes_IndependentCaches()
    {
        var scriptL0 = MakeScript((0, 0), (1000, 100));
        var scriptR0 = MakeScript((0, 100), (1000, 0));

        // Advance L0 cache
        _sut.GetPosition(scriptL0, 800, "L0");

        // R0 should start fresh, not affected by L0's cache
        var result = _sut.GetPosition(scriptR0, 500, "R0");

        Assert.Equal(50.0, result);
    }

    // --- ResetIndices ---

    [Fact]
    public void ResetIndices_ClearsCache()
    {
        var script = MakeScript(
            (0, 0), (1000, 100), (2000, 0), (3000, 100));

        // Build up cache
        _sut.GetPosition(script, 2500, "L0");

        // Reset
        _sut.ResetIndices();

        // Should still work correctly after reset (binary search fallback)
        var result = _sut.GetPosition(script, 500, "L0");
        Assert.Equal(50.0, result);
    }

    // --- Edge: same time for two consecutive actions ---

    [Fact]
    public void GetPosition_SameTimeActions_ReturnsFirstPos()
    {
        var script = MakeScript((1000, 0), (1000, 100), (2000, 50));

        var result = _sut.GetPosition(script, 1000, "L0");

        Assert.Equal(0.0, result);
    }

    // --- Edge: interpolation with descending values ---

    [Fact]
    public void GetPosition_DescendingValues_InterpolatesCorrectly()
    {
        var script = MakeScript((1000, 100), (2000, 0));

        var result = _sut.GetPosition(script, 1500, "L0");

        Assert.Equal(50.0, result);
    }

    // --- Many actions ---

    [Fact]
    public void GetPosition_ManyActions_SequentialPlayback()
    {
        var actions = new (long, int)[100];
        for (int i = 0; i < 100; i++)
            actions[i] = (i * 100, i % 2 == 0 ? 0 : 100);

        var script = MakeScript(actions);

        // Sequential playback through all actions
        for (int i = 0; i < 99; i++)
        {
            var time = i * 100 + 50;
            var result = _sut.GetPosition(script, time, "L0");
            // Midpoint between alternating 0 and 100
            Assert.Equal(50.0, result);
        }
    }
}
