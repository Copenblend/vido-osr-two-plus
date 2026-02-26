using System.Globalization;
using Osr2PlusPlugin.Converters;
using Osr2PlusPlugin.Models;
using Xunit;

namespace Osr2PlusPlugin.Tests.Converters;

public class BeatBarModeDisplayConverterTests
{
    private readonly BeatBarModeDisplayConverter _sut = new();

    [Fact]
    public void Convert_Off_ReturnsNoBeatBar()
    {
        var result = _sut.Convert(BeatBarMode.Off, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("No Beat Bar", result);
    }

    [Fact]
    public void Convert_OnPeak_ReturnsOnPeak()
    {
        var result = _sut.Convert(BeatBarMode.OnPeak, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("On Peak", result);
    }

    [Fact]
    public void Convert_OnValley_ReturnsOnValley()
    {
        var result = _sut.Convert(BeatBarMode.OnValley, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("On Valley", result);
    }

    [Fact]
    public void Convert_ExternalMode_ReturnsDisplayName()
    {
        var mode = BeatBarMode.CreateExternal("pulse", "Pulse Beats");
        var result = _sut.Convert(mode, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("Pulse Beats", result);
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
