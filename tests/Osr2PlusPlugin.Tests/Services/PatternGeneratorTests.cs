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

    // --- Saw ---

    [Theory]
    [InlineData(0.0,  0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(0.5,  0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0,  0.0)]  // wraps
    public void Saw_KeyPoints(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Saw, t);
        Assert.Equal(expected, result, Tolerance);
    }

    // --- SawtoothReverse ---

    [Theory]
    [InlineData(0.0,  1.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(0.5,  0.5)]
    [InlineData(0.75, 0.25)]
    [InlineData(1.0,  1.0)]  // wraps to 0 → 1-0=1
    public void SawtoothReverse_KeyPoints(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.SawtoothReverse, t);
        Assert.Equal(expected, result, Tolerance);
    }

    // --- Square ---

    [Theory]
    [InlineData(0.0,  1.0)]
    [InlineData(0.25, 1.0)]
    [InlineData(0.49, 1.0)]
    [InlineData(0.5,  0.0)]
    [InlineData(0.75, 0.0)]
    [InlineData(1.0,  1.0)]  // wraps to 0—first half
    public void Square_KeyPoints(double t, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Square, t);
        Assert.Equal(expected, result, Tolerance);
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

    // --- Grind (position pass-through) ---

    [Theory]
    [InlineData(0.0,  0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(0.5,  0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0,  1.0)]
    public void Grind_PassesThroughPosition(double strokePos, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Grind, strokePos);
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void Grind_ClampsAbove1()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Grind, 1.5);
        Assert.Equal(1.0, result, Tolerance);
    }

    [Fact]
    public void Grind_ClampsBelow0()
    {
        var result = PatternGenerator.Calculate(AxisFillMode.Grind, -0.3);
        Assert.Equal(0.0, result, Tolerance);
    }

    // --- ReverseGrind (inverted position) ---

    [Theory]
    [InlineData(0.0,  1.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(0.5,  0.5)]
    [InlineData(0.75, 0.25)]
    [InlineData(1.0,  0.0)]
    public void ReverseGrind_InvertsPosition(double strokePos, double expected)
    {
        var result = PatternGenerator.Calculate(AxisFillMode.ReverseGrind, strokePos);
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void ReverseGrind_ClampsAbove1()
    {
        // 1.0 - 1.5 = -0.5 → clamped to 0.0
        var result = PatternGenerator.Calculate(AxisFillMode.ReverseGrind, 1.5);
        Assert.Equal(0.0, result, Tolerance);
    }

    [Fact]
    public void ReverseGrind_ClampsBelow0()
    {
        // 1.0 - (-0.3) = 1.3 → clamped to 1.0
        var result = PatternGenerator.Calculate(AxisFillMode.ReverseGrind, -0.3);
        Assert.Equal(1.0, result, Tolerance);
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
    [InlineData(AxisFillMode.Grind)]
    [InlineData(AxisFillMode.ReverseGrind)]
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
