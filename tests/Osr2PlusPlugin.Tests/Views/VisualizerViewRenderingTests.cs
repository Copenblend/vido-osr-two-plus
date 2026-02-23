using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Views;
using SkiaSharp;
using Xunit;

namespace Osr2PlusPlugin.Tests.Views;

/// <summary>
/// Tests for the static/internal rendering helpers in <see cref="VisualizerView"/>:
/// binary search and heatmap color interpolation.
/// </summary>
public class VisualizerViewRenderingTests
{
    // ═══════════════════════════════════════════════════════
    //  BinarySearchStart
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void BinarySearchStart_EmptyList_ReturnsZero()
    {
        var result = VisualizerView.BinarySearchStart([], 1000);
        Assert.Equal(0, result);
    }

    [Fact]
    public void BinarySearchStart_TargetBeforeAll_ReturnsZero()
    {
        var actions = new List<FunscriptAction>
        {
            new(1000, 50),
            new(2000, 100),
            new(3000, 0),
        };

        Assert.Equal(0, VisualizerView.BinarySearchStart(actions, 500));
    }

    [Fact]
    public void BinarySearchStart_TargetAfterAll_ReturnsCount()
    {
        var actions = new List<FunscriptAction>
        {
            new(1000, 50),
            new(2000, 100),
            new(3000, 0),
        };

        Assert.Equal(3, VisualizerView.BinarySearchStart(actions, 5000));
    }

    [Fact]
    public void BinarySearchStart_ExactMatch_ReturnsIndex()
    {
        var actions = new List<FunscriptAction>
        {
            new(1000, 50),
            new(2000, 100),
            new(3000, 0),
        };

        Assert.Equal(1, VisualizerView.BinarySearchStart(actions, 2000));
    }

    [Fact]
    public void BinarySearchStart_BetweenValues_ReturnsNextIndex()
    {
        var actions = new List<FunscriptAction>
        {
            new(1000, 50),
            new(3000, 100),
            new(5000, 0),
        };

        // 2000 is between index 0 (1000) and index 1 (3000), should return 1
        Assert.Equal(1, VisualizerView.BinarySearchStart(actions, 2000));
    }

    [Fact]
    public void BinarySearchStart_SingleElement_TargetBefore()
    {
        var actions = new List<FunscriptAction> { new(5000, 50) };
        Assert.Equal(0, VisualizerView.BinarySearchStart(actions, 1000));
    }

    [Fact]
    public void BinarySearchStart_SingleElement_TargetAfter()
    {
        var actions = new List<FunscriptAction> { new(5000, 50) };
        Assert.Equal(1, VisualizerView.BinarySearchStart(actions, 6000));
    }

    [Fact]
    public void BinarySearchStart_SingleElement_ExactMatch()
    {
        var actions = new List<FunscriptAction> { new(5000, 50) };
        Assert.Equal(0, VisualizerView.BinarySearchStart(actions, 5000));
    }

    [Fact]
    public void BinarySearchStart_LargeList_FindsCorrectIndex()
    {
        // Create 1000 evenly spaced actions
        var actions = new List<FunscriptAction>();
        for (int i = 0; i < 1000; i++)
            actions.Add(new FunscriptAction(i * 100L, i % 101));

        // Target 55050 should find index 551 (55100 is at index 551)
        var result = VisualizerView.BinarySearchStart(actions, 55050);
        Assert.Equal(551, result);
        Assert.True(actions[result].AtMs >= 55050);
        if (result > 0)
            Assert.True(actions[result - 1].AtMs < 55050);
    }

    // ═══════════════════════════════════════════════════════
    //  SpeedToColor — Boundary Values
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SpeedToColor_ZeroSpeed_ReturnsDeepBlue()
    {
        var color = VisualizerView.SpeedToColor(0f);
        Assert.Equal(SKColor.Parse("#1B0A7A"), color);
    }

    [Fact]
    public void SpeedToColor_NegativeSpeed_ReturnsDeepBlue()
    {
        var color = VisualizerView.SpeedToColor(-10f);
        Assert.Equal(SKColor.Parse("#1B0A7A"), color);
    }

    [Fact]
    public void SpeedToColor_Speed100_ReturnsBlue()
    {
        var color = VisualizerView.SpeedToColor(100f);
        Assert.Equal(SKColor.Parse("#2989D8"), color);
    }

    [Fact]
    public void SpeedToColor_Speed200_ReturnsGreen()
    {
        var color = VisualizerView.SpeedToColor(200f);
        Assert.Equal(SKColor.Parse("#46B946"), color);
    }

    [Fact]
    public void SpeedToColor_Speed300_ReturnsYellow()
    {
        var color = VisualizerView.SpeedToColor(300f);
        Assert.Equal(SKColor.Parse("#F0C000"), color);
    }

    [Fact]
    public void SpeedToColor_Speed400_ReturnsOrangeRed()
    {
        var color = VisualizerView.SpeedToColor(400f);
        Assert.Equal(SKColor.Parse("#FF4500"), color);
    }

    [Fact]
    public void SpeedToColor_Speed500_ReturnsRed()
    {
        var color = VisualizerView.SpeedToColor(500f);
        Assert.Equal(SKColor.Parse("#FF0000"), color);
    }

    [Fact]
    public void SpeedToColor_SpeedAbove500_ReturnsRed()
    {
        var color = VisualizerView.SpeedToColor(1000f);
        Assert.Equal(SKColor.Parse("#FF0000"), color);
    }

    // ═══════════════════════════════════════════════════════
    //  SpeedToColor — Interpolation
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SpeedToColor_Speed50_InterpolatesBetweenFirstTwoStops()
    {
        var color = VisualizerView.SpeedToColor(50f);

        // Should be midpoint between #1B0A7A and #2989D8
        var c0 = SKColor.Parse("#1B0A7A");
        var c1 = SKColor.Parse("#2989D8");

        // Allow ±1 for rounding
        Assert.InRange(color.Red, (byte)((c0.Red + c1.Red) / 2 - 1), (byte)((c0.Red + c1.Red) / 2 + 1));
        Assert.InRange(color.Green, (byte)((c0.Green + c1.Green) / 2 - 1), (byte)((c0.Green + c1.Green) / 2 + 1));
        Assert.InRange(color.Blue, (byte)((c0.Blue + c1.Blue) / 2 - 1), (byte)((c0.Blue + c1.Blue) / 2 + 1));
    }

    [Fact]
    public void SpeedToColor_Speed250_InterpolatesBetweenGreenAndYellow()
    {
        var color = VisualizerView.SpeedToColor(250f);

        // Should be midpoint between #46B946 and #F0C000
        var c0 = SKColor.Parse("#46B946");
        var c1 = SKColor.Parse("#F0C000");

        Assert.InRange(color.Red, (byte)((c0.Red + c1.Red) / 2 - 1), (byte)((c0.Red + c1.Red) / 2 + 1));
        Assert.InRange(color.Green, (byte)((c0.Green + c1.Green) / 2 - 1), (byte)((c0.Green + c1.Green) / 2 + 1));
        Assert.InRange(color.Blue, (byte)((c0.Blue + c1.Blue) / 2 - 1), (byte)((c0.Blue + c1.Blue) / 2 + 1));
    }

    [Fact]
    public void SpeedToColor_MonotonicallyIncreasingSpeed_ShiftsTowardsRed()
    {
        // Verify colors get progressively "warmer" as speed increases
        var prev = VisualizerView.SpeedToColor(0f);
        for (float speed = 50; speed <= 500; speed += 50)
        {
            var curr = VisualizerView.SpeedToColor(speed);
            // At minimum, the color should be different from the previous
            Assert.NotEqual(prev, curr);
            prev = curr;
        }
    }
}
