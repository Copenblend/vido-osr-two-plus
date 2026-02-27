using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class PatternGeneratorTests
{
    private const double Tolerance = 0.001;

    // --- Triangle ---

    [Theory]
    [InlineData(0.0,  0.0)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5,  1.0)]
    [InlineData(0.75, 0.5)]
    [InlineData(1.0,  0.0)]  // wraps to 0
    public void Triangle_KeyPoints(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Triangle, t);
        Assert.Equal(expected, result, Tolerance);
    }

    // --- Sine ---

    [Theory]
    [InlineData(0.0,  0.0)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5,  1.0)]
    [InlineData(0.75, 0.5)]
    [InlineData(1.0,  0.0)]
    public void Sine_KeyPoints(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Sine, t);
        Assert.Equal(expected, result, Tolerance);
    }

    // --- Saw (smoothed: linear ramp then cosine drop) ---

    [Fact]
    public void Saw_StartsAtZero()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Saw, 0.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void Saw_ReachesOneBeforeDrop()
    {
        // At t=0.85 (end of linear ramp), should be ~1.0
        var result = PatternGenerator.Calculate(AxisFillMode.Saw, 0.85);
        Assert.Equal(1.0, result, 0.01);
    }

    [Fact]
    public void Saw_DropsSmoothlToZero()
    {
        // At t=0.925 (mid drop), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.Saw, 0.925);
        Assert.Equal(0.5, result, 0.1);
    }

    [Fact]
    public void Saw_WrapsBackToZero()
    {
        // At t=1.0 (wraps to 0), should be 0.0
        var result = PatternGenerator.Calculate(AxisFillMode.Saw, 1.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void Saw_LinearPhase_IsMonotonic()
    {
        double prev = 0;
        for (double t = 0; t <= 0.85; t += 0.01)
        {
            var val = PatternGenerator.Calculate(AxisFillMode.Saw, t);
            Assert.True(val >= prev - Tolerance, $"Saw not monotonic at t={t}: {val} < {prev}");
            prev = val;
        }
    }

    // --- SawtoothReverse (smoothed: cosine rise then linear ramp down) ---

    [Fact]
    public void SawtoothReverse_StartsNearZero()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, 0.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void SawtoothReverse_RiseSmoothlyToOne()
    {
        // At t=0.15 (end of cosine rise), should be ~1.0
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, 0.15);
        Assert.Equal(1.0, result, 0.01);
    }

    [Fact]
    public void SawtoothReverse_MidRise_IsAboutHalf()
    {
        // At t=0.075 (mid cosine rise), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, 0.075);
        Assert.Equal(0.5, result, 0.1);
    }

    [Fact]
    public void SawtoothReverse_LinearDown_Midpoint()
    {
        // At t≈0.575 (midpoint of linear down from 0.15 to 1.0), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, 0.575);
        Assert.Equal(0.5, result, 0.05);
    }

    [Fact]
    public void SawtoothReverse_WrapsToZero()
    {
        // At t=1.0 (wraps to 0), should be 0.0
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, 1.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    // --- Square (smoothed: cosine transitions with dwells) ---

    [Fact]
    public void Square_AtZero_IsZero()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Square, 0.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void Square_HighDwell_IsOne()
    {
        // Between riseEnd (0.10) and highEnd (0.50) should be 1.0
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Square, 0.15), Tolerance);
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Square, 0.3), Tolerance);
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Square, 0.49), Tolerance);
    }

    [Fact]
    public void Square_LowDwell_IsZero()
    {
        // Between fallEnd (0.60) and 1.0 should be 0.0
        Assert.Equal(0.0, PatternGenerator.Calculate(AxisFillMode.Square, 0.65), Tolerance);
        Assert.Equal(0.0, PatternGenerator.Calculate(AxisFillMode.Square, 0.9), Tolerance);
    }

    [Fact]
    public void Square_RiseMidpoint_IsAboutHalf()
    {
        // At t=0.05 (middle of rise), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.Square, 0.05);
        Assert.Equal(0.5, result, 0.05);
    }

    [Fact]
    public void Square_FallMidpoint_IsAboutHalf()
    {
        // At t=0.55 (middle of fall 0.5→0.6), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.Square, 0.55);
        Assert.Equal(0.5, result, 0.05);
    }

    [Fact]
    public void Square_WrapsToZero()
    {
        // At t=1.0 (wraps to 0), should be 0.0
        var result = PatternGenerator.Calculate(AxisFillMode.Square, 1.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    // --- Pulse ---

    [Fact]
    public void Pulse_AtZero_IsZero()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Pulse, 0.0);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void Pulse_HighDwell_IsOne()
    {
        // Between riseEnd (0.15) and highEnd (0.5) should be 1.0
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Pulse, 0.2), Tolerance);
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Pulse, 0.3), Tolerance);
        Assert.Equal(1.0, PatternGenerator.Calculate(AxisFillMode.Pulse, 0.49), Tolerance);
    }

    [Fact]
    public void Pulse_LowDwell_IsZero()
    {
        // Between fallEnd (0.65) and 1.0 should be 0.0
        Assert.Equal(0.0, PatternGenerator.Calculate(AxisFillMode.Pulse, 0.7), Tolerance);
        Assert.Equal(0.0, PatternGenerator.Calculate(AxisFillMode.Pulse, 0.9), Tolerance);
    }

    [Fact]
    public void Pulse_RiseMidpoint_IsAboutHalf()
    {
        // At t=0.075 (middle of rise), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.Pulse, 0.075);
        Assert.Equal(0.5, result, 0.05);
    }

    [Fact]
    public void Pulse_FallMidpoint_IsAboutHalf()
    {
        // At t=0.575 (middle of fall 0.5→0.65), should be ~0.5
        var result = PatternGenerator.Calculate(AxisFillMode.Pulse, 0.575);
        Assert.Equal(0.5, result, 0.05);
    }

    // --- EaseInOut ---

    [Theory]
    [InlineData(0.0,  0.0)]
    [InlineData(0.5,  1.0)]
    [InlineData(1.0,  0.0)]  // wraps
    public void EaseInOut_Extremes(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.EaseInOut, t);
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void EaseInOut_QuarterPoint_IsHalf()
    {
        // At t=0.25 the triangle phase is 0.5, cubic ease-in-out of 0.5 = 0.5
        var result = PatternGenerator.Calculate(AxisFillMode.EaseInOut, 0.25);
        Assert.Equal(0.5, result, Tolerance);
    }

    [Fact]
    public void EaseInOut_EighthPoint_LessThanTriangle()
    {
        // Cubic ease-in-out starts slower than linear — at t=0.125, phase=0.25
        // Triangle at 0.125 would give 0.25, EaseInOut should be less (cubic ease-in)
        var easeResult = PatternGenerator.Calculate(AxisFillMode.EaseInOut, 0.125);
        var triangleResult = PatternGenerator.Calculate(AxisFillMode.Triangle, 0.125);
        Assert.True(easeResult < triangleResult,
            $"EaseInOut at 0.125 ({easeResult}) should be < Triangle ({triangleResult})");
        Assert.True(easeResult > 0.0);
    }

    // --- Boundary behavior ---

    [Fact]
    public void Calculate_T_WrapsModulo()
    {
        // t=1.5 should behave like t=0.5
        var at05 = PatternGenerator.Calculate(AxisFillMode.Triangle, 0.5);
        var at15 = PatternGenerator.Calculate(AxisFillMode.Triangle, 1.5);
        Assert.Equal(at05, at15, Tolerance);
    }

    [Fact]
    public void Calculate_T_WrapsLargeValues()
    {
        var at025 = PatternGenerator.Calculate(AxisFillMode.Sine, 0.25);
        var at325 = PatternGenerator.Calculate(AxisFillMode.Sine, 3.25);
        Assert.Equal(at025, at325, Tolerance);
    }

    // --- Negative t ---

    [Fact]
    public void Calculate_NegativeT_WrapsCorrectly()
    {
        // t=-0.25 should wrap to t=0.75
        var atNeg = PatternGenerator.Calculate(AxisFillMode.Triangle, -0.25);
        var at075 = PatternGenerator.Calculate(AxisFillMode.Triangle, 0.75);
        Assert.Equal(at075, atNeg, Tolerance);
    }

    [Fact]
    public void Calculate_NegativeT_Sine()
    {
        var atNeg = PatternGenerator.Calculate(AxisFillMode.Sine, -0.5);
        var at05 = PatternGenerator.Calculate(AxisFillMode.Sine, 0.5);
        Assert.Equal(at05, atNeg, Tolerance);
    }

    // --- Unknown fill mode returns 0.5 ---

    [Fact]
    public void Calculate_NoneFillMode_Returns05()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.None, 0.25);
        Assert.Equal(0.5, result, Tolerance);
    }

    // --- All patterns return values in 0.0–1.0 range ---

    [Theory]
    [InlineData(AxisFillMode.Triangle)]
    [InlineData(AxisFillMode.Sine)]
    [InlineData(AxisFillMode.Saw)]
    [InlineData(AxisFillMode.SawtoothReverse)]
    [InlineData(AxisFillMode.Square)]
    [InlineData(AxisFillMode.Pulse)]
    [InlineData(AxisFillMode.EaseInOut)]
    public void Calculate_AllPatterns_InRange(AxisFillMode mode)
    {
        for (int i = 0; i <= 100; i++)
        {
            double t = i / 100.0;
            var result = PatternGenerator.Calculate(mode, t);
            Assert.True(result >= 0.0 && result <= 1.0,
                $"{mode} at t={t} returned {result}, expected 0.0–1.0");
        }
    }
}
