using System.Globalization;
using Osr2PlusPlugin.Converters;
using Osr2PlusPlugin.Models;
using Xunit;

namespace Osr2PlusPlugin.Tests.Converters;

public class BeatBarModeDisplayConverterTests
{
    private readonly BeatBarModeDisplayConverter _sut = new();

    [Theory]
    [InlineData(BeatBarMode.Off, "Off")]
    [InlineData(BeatBarMode.OnPeak, "On Peak")]
    [InlineData(BeatBarMode.OnValley, "On Valley")]
    public void Convert_ReturnsExpectedDisplayString(BeatBarMode mode, string expected)
    {
        var result = _sut.Convert(mode, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
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

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _sut.ConvertBack("Off", typeof(BeatBarMode), null!, CultureInfo.InvariantCulture));
    }
}
