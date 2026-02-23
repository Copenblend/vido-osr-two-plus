using System.IO;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class FunscriptMatcherTests : IDisposable
{
    private readonly FunscriptMatcher _sut = new();
    private readonly string _tempDir;

    public FunscriptMatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FunscriptMatcherTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateFile(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }

    // --- All 4 axis matches ---

    [Fact]
    public void FindMatchingScripts_AllAxesPresent_ReturnsAll4()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("Movie.funscript");
        CreateFile("Movie.twist.funscript");
        CreateFile("Movie.roll.funscript");
        CreateFile("Movie.pitch.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Equal(4, result.Count);
        Assert.EndsWith("Movie.funscript", result["L0"]);
        Assert.EndsWith("Movie.twist.funscript", result["R0"]);
        Assert.EndsWith("Movie.roll.funscript", result["R1"]);
        Assert.EndsWith("Movie.pitch.funscript", result["R2"]);
    }

    [Fact]
    public void FindMatchingScripts_OnlyStroke_ReturnsL0Only()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("Movie.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Single(result);
        Assert.True(result.ContainsKey("L0"));
    }

    [Fact]
    public void FindMatchingScripts_OnlyTwist_ReturnsR0Only()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("Movie.twist.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Single(result);
        Assert.True(result.ContainsKey("R0"));
    }

    // --- Missing files ---

    [Fact]
    public void FindMatchingScripts_NoFunscripts_ReturnsEmpty()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Empty(result);
    }

    // --- Case-insensitive matching ---

    [Fact]
    public void FindMatchingScripts_CaseInsensitive_MatchesUpperCase()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("MOVIE.FUNSCRIPT");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Single(result);
        Assert.True(result.ContainsKey("L0"));
    }

    [Fact]
    public void FindMatchingScripts_CaseInsensitive_MixedCase()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("movie.Twist.FUNSCRIPT");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Single(result);
        Assert.True(result.ContainsKey("R0"));
    }

    // --- No partial name matches ---

    [Fact]
    public void FindMatchingScripts_PartialName_DoesNotMatch()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        // "Movie_hard.funscript" should NOT match "Movie.mp4"
        CreateFile("Movie_hard.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Empty(result);
    }

    [Fact]
    public void FindMatchingScripts_SimilarPrefix_DoesNotMatch()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("MovieExtra.funscript");
        CreateFile("Movie2.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Empty(result);
    }

    // --- Invalid paths ---

    [Fact]
    public void FindMatchingScripts_NullPath_ReturnsEmpty()
    {
        var result = _sut.FindMatchingScripts(null!);

        Assert.Empty(result);
    }

    [Fact]
    public void FindMatchingScripts_EmptyPath_ReturnsEmpty()
    {
        var result = _sut.FindMatchingScripts("");

        Assert.Empty(result);
    }

    [Fact]
    public void FindMatchingScripts_NonExistentDirectory_ReturnsEmpty()
    {
        var result = _sut.FindMatchingScripts(@"C:\NonExistent\Path\Video.mp4");

        Assert.Empty(result);
    }

    // --- Video name with dots ---

    [Fact]
    public void FindMatchingScripts_VideoNameWithDots_MatchesCorrectly()
    {
        var videoPath = Path.Combine(_tempDir, "My.Movie.Name.mp4");
        CreateFile("My.Movie.Name.funscript");
        CreateFile("My.Movie.Name.twist.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("L0"));
        Assert.True(result.ContainsKey("R0"));
    }

    // --- Returns full paths ---

    [Fact]
    public void FindMatchingScripts_ReturnsFullPaths()
    {
        var videoPath = Path.Combine(_tempDir, "Movie.mp4");
        CreateFile("Movie.funscript");

        var result = _sut.FindMatchingScripts(videoPath);

        Assert.True(Path.IsPathRooted(result["L0"]));
        Assert.Equal(Path.Combine(_tempDir, "Movie.funscript"), result["L0"]);
    }
}
