using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class BeatDetectionServiceTests
{
    private readonly BeatDetectionService _sut = new();

    // ── Helper ───────────────────────────────────────────────

    private static FunscriptData MakeScript(params (long atMs, int pos)[] points)
    {
        return new FunscriptData
        {
            AxisId = "L0",
            Actions = points.Select(p => new FunscriptAction(p.atMs, p.pos)).ToList()
        };
    }

    // ── Peak detection ───────────────────────────────────────

    [Fact]
    public void DetectBeats_OnPeak_FindsPeaks()
    {
        // Pattern: up-down-up-down
        var script = MakeScript(
            (0, 0), (100, 80), (200, 20), (300, 90), (400, 10));

        var result = _sut.DetectBeats(script, BeatBarMode.OnPeak);

        Assert.Equal(new double[] { 100, 300 }, result);
    }

    [Fact]
    public void DetectBeats_OnPeak_DoesNotIncludeValleys()
    {
        var script = MakeScript(
            (0, 50), (100, 80), (200, 20), (300, 90), (400, 10));

        var result = _sut.DetectBeats(script, BeatBarMode.OnPeak);

        Assert.DoesNotContain(200, result);
        Assert.DoesNotContain(400, result);
    }

    // ── Valley detection ─────────────────────────────────────

    [Fact]
    public void DetectBeats_OnValley_FindsValleys()
    {
        // Pattern: up-down-up-down-up
        var script = MakeScript(
            (0, 50), (100, 80), (200, 20), (300, 90), (400, 10), (500, 60));

        var result = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Equal(new double[] { 200, 400 }, result);
    }

    [Fact]
    public void DetectBeats_OnValley_DoesNotIncludePeaks()
    {
        var script = MakeScript(
            (0, 50), (100, 80), (200, 20), (300, 90), (400, 10), (500, 60));

        var result = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.DoesNotContain(100, result);
        Assert.DoesNotContain(300, result);
    }

    // ── Off mode ─────────────────────────────────────────────

    [Fact]
    public void DetectBeats_Off_ReturnsEmpty()
    {
        var script = MakeScript(
            (0, 0), (100, 80), (200, 20), (300, 90), (400, 10));

        var result = _sut.DetectBeats(script, BeatBarMode.Off);

        Assert.Empty(result);
    }

    // ── Null / empty input ───────────────────────────────────

    [Fact]
    public void DetectBeats_NullScript_ReturnsEmpty()
    {
        var result = _sut.DetectBeats(null, BeatBarMode.OnPeak);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DetectBeats_LessThan3Actions_ReturnsEmpty(int count)
    {
        var actions = Enumerable.Range(0, count)
            .Select(i => (atMs: (long)(i * 100), pos: i * 50))
            .ToArray();
        var script = MakeScript(actions);

        var peakResult = _sut.DetectBeats(script, BeatBarMode.OnPeak);
        var valleyResult = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Empty(peakResult);
        Assert.Empty(valleyResult);
    }

    // ── Monotonic sequences ──────────────────────────────────

    [Fact]
    public void DetectBeats_MonotonicIncreasing_ReturnsEmpty()
    {
        var script = MakeScript(
            (0, 0), (100, 25), (200, 50), (300, 75), (400, 100));

        var result = _sut.DetectBeats(script, BeatBarMode.OnPeak);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectBeats_MonotonicDecreasing_ReturnsEmpty()
    {
        var script = MakeScript(
            (0, 100), (100, 75), (200, 50), (300, 25), (400, 0));

        var result = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Empty(result);
    }

    // ── Plateau handling ─────────────────────────────────────

    [Fact]
    public void DetectBeats_Plateau_DetectsEntryPoint_Peak()
    {
        // Peak plateau: rises to 80, stays at 80, then drops
        var script = MakeScript(
            (0, 0), (100, 80), (200, 80), (300, 20));

        var result = _sut.DetectBeats(script, BeatBarMode.OnPeak);

        Assert.Equal(new double[] { 100 }, result);
    }

    [Fact]
    public void DetectBeats_Plateau_DetectsEntryPoint_Valley()
    {
        // Valley plateau: drops to 10, stays at 10, then rises
        var script = MakeScript(
            (0, 80), (100, 10), (200, 10), (300, 70));

        var result = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Equal(new double[] { 100 }, result);
    }

    // ── Result ordering ──────────────────────────────────────

    [Fact]
    public void DetectBeats_ResultIsSorted()
    {
        // Many peaks to verify sorted order
        var script = MakeScript(
            (0, 0), (100, 90), (200, 10), (300, 80),
            (400, 5), (500, 95), (600, 15), (700, 70));

        var result = _sut.DetectBeats(script, BeatBarMode.OnPeak);

        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i] > result[i - 1],
                $"Result not sorted at index {i}: {result[i - 1]} >= {result[i]}");
        }
    }

    // ── Realistic funscript ──────────────────────────────────

    [Fact]
    public void DetectBeats_RealisticFunscript_CorrectPeaks()
    {
        // Simulates a typical stroke pattern: 0→100→0→100→0 at ~250ms intervals
        var script = MakeScript(
            (0, 10),
            (250, 90),   // peak
            (500, 10),
            (750, 95),   // peak
            (1000, 5),
            (1250, 85),  // peak
            (1500, 15),
            (1750, 92),  // peak
            (2000, 8));

        var peaks = _sut.DetectBeats(script, BeatBarMode.OnPeak);
        var valleys = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Equal(new double[] { 250, 750, 1250, 1750 }, peaks);
        Assert.Equal(new double[] { 500, 1000, 1500 }, valleys);
    }

    [Fact]
    public void DetectBeats_RealisticFunscript_CorrectValleys()
    {
        // Same pattern, verifying valley count independently
        var script = MakeScript(
            (0, 50),
            (200, 95),
            (400, 5),    // valley
            (600, 100),
            (800, 0),    // valley
            (1000, 90),
            (1200, 10),  // valley
            (1400, 80));

        var result = _sut.DetectBeats(script, BeatBarMode.OnValley);

        Assert.Equal(new double[] { 400, 800, 1200 }, result);
    }
}
