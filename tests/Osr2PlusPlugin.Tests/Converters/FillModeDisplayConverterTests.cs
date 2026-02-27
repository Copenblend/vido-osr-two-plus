using System.Globalization;
using Osr2PlusPlugin.Converters;
using Osr2PlusPlugin.Models;
using Xunit;

namespace Osr2PlusPlugin.Tests.Converters;

public class FillModeDisplayConverterTests
{
    private readonly FillModeDisplayConverter _sut = new();

    [Theory]
    [InlineData(AxisFillMode.None, "None")]
    [InlineData(AxisFillMode.Random, "Random")]
    [InlineData(AxisFillMode.Triangle, "Triangle")]
    [InlineData(AxisFillMode.Sine, "Sine")]
    [InlineData(AxisFillMode.Saw, "Saw")]
    [InlineData(AxisFillMode.Square, "Square")]
    [InlineData(AxisFillMode.Pulse, "Pulse")]
    public void Convert_StandardModes_ReturnsEnumName(AxisFillMode mode, string expected)
    {
        var result = _sut.Convert(mode, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_SawtoothReverse_ReturnsReverseSaw()
    {
        var result = _sut.Convert(AxisFillMode.SawtoothReverse, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("Reverse Saw", result);
    }

    [Fact]
    public void Convert_EaseInOut_ReturnsEaseInSlashOut()
    {
        var result = _sut.Convert(AxisFillMode.EaseInOut, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("Ease In/Out", result);
    }

    [Fact]
    public void Convert_NonEnum_ReturnsToString()
    {
        var result = _sut.Convert("hello", typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Convert_Null_ReturnsEmptyString()
    {
        var result = _sut.Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("", result);
    }
}
