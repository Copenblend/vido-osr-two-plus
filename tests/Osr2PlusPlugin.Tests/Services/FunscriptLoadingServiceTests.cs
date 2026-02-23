using System.IO;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class FunscriptLoadingServiceTests : IDisposable
{
    private readonly FunscriptParser _parser = new();
    private readonly FunscriptMatcher _matcher = new();
    private readonly FunscriptLoadingService _sut;
    private readonly string _tempDir;

    public FunscriptLoadingServiceTests()
    {
        _sut = new FunscriptLoadingService(_parser, _matcher);
        _tempDir = Path.Combine(Path.GetTempPath(), $"FunscriptLoadingTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateFunscript(string fileName, string json)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private string VideoPath(string name = "Movie.mp4") => Path.Combine(_tempDir, name);

    private const string SingleAxisJson = """
    {
        "actions": [
            { "at": 1000, "pos": 0 },
            { "at": 2000, "pos": 100 }
        ]
    }
    """;

    private const string MultiAxisJson = """
    {
        "actions": [
            { "at": 100, "pos": 50 }
        ],
        "axes": [
            { "id": "R0", "actions": [{ "at": 200, "pos": 60 }] },
            { "id": "R1", "actions": [{ "at": 300, "pos": 70 }] }
        ]
    }
    """;

    // --- Auto-load flow: individual files ---

    [Fact]
    public void LoadScriptsForVideo_IndividualFiles_LoadsMatched()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);
        CreateFunscript("Movie.twist.funscript", SingleAxisJson);

        var logs = _sut.LoadScriptsForVideo(VideoPath());

        Assert.Equal(2, _sut.LoadedScripts.Count);
        Assert.True(_sut.LoadedScripts.ContainsKey("L0"));
        Assert.True(_sut.LoadedScripts.ContainsKey("R0"));
        Assert.Contains(logs, l => l.Contains("Auto-matched") && l.Contains("L0"));
        Assert.Contains(logs, l => l.Contains("Auto-matched") && l.Contains("R0"));
    }

    [Fact]
    public void LoadScriptsForVideo_NoFiles_ReturnsEmptyWithLog()
    {
        var logs = _sut.LoadScriptsForVideo(VideoPath());

        Assert.Empty(_sut.LoadedScripts);
        Assert.Contains(logs, l => l.Contains("No funscript files found"));
    }

    // --- Multi-axis priority ---

    [Fact]
    public void LoadScriptsForVideo_MultiAxisFormat_TakesPriorityOverIndividual()
    {
        // Create a multi-axis base file AND individual files
        CreateFunscript("Movie.funscript", MultiAxisJson);
        CreateFunscript("Movie.twist.funscript", SingleAxisJson); // Should be ignored

        var logs = _sut.LoadScriptsForVideo(VideoPath());

        // Multi-axis loaded L0, R0, R1 from the base file
        Assert.Equal(3, _sut.LoadedScripts.Count);
        Assert.True(_sut.LoadedScripts.ContainsKey("L0"));
        Assert.True(_sut.LoadedScripts.ContainsKey("R0"));
        Assert.True(_sut.LoadedScripts.ContainsKey("R1"));

        // All should say "Multi-axis", not "Auto-matched"
        Assert.Contains(logs, l => l.Contains("Multi-axis"));
        Assert.DoesNotContain(logs, l => l.Contains("Auto-matched"));
    }

    [Fact]
    public void LoadScriptsForVideo_SingleAxisBaseFile_FallsBackToMatching()
    {
        // Base file exists but has no "axes" array → not multi-axis
        CreateFunscript("Movie.funscript", SingleAxisJson);
        CreateFunscript("Movie.roll.funscript", SingleAxisJson);

        var logs = _sut.LoadScriptsForVideo(VideoPath());

        // Should fall back to individual matching
        Assert.Equal(2, _sut.LoadedScripts.Count);
        Assert.True(_sut.LoadedScripts.ContainsKey("L0"));
        Assert.True(_sut.LoadedScripts.ContainsKey("R1"));
        Assert.Contains(logs, l => l.Contains("Auto-matched"));
    }

    // --- Manual override persistence ---

    [Fact]
    public void ManualOverride_PersistsAcrossAutoLoads()
    {
        var overridePath = CreateFunscript("custom_twist.funscript", SingleAxisJson);
        _sut.SetManualOverride("R0", overridePath);

        // Now load a video — the manual override should take precedence for R0
        CreateFunscript("Movie.funscript", SingleAxisJson);
        CreateFunscript("Movie.twist.funscript", """{ "actions": [{ "at": 999, "pos": 99 }] }""");

        _sut.LoadScriptsForVideo(VideoPath());

        Assert.True(_sut.LoadedScripts.ContainsKey("R0"));
        // The override file has actions at 1000/2000, not 999
        Assert.Equal(overridePath, _sut.LoadedScripts["R0"].FilePath);
        Assert.Contains(_sut.LoadScriptsForVideo(VideoPath()), l => l.Contains("Manual override") && l.Contains("R0"));
    }

    [Fact]
    public void ManualOverride_AppliedImmediately_WhenVideoLoaded()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);
        _sut.LoadScriptsForVideo(VideoPath());

        bool eventFired = false;
        _sut.ScriptsChanged += _ => eventFired = true;

        var overridePath = CreateFunscript("custom.funscript", SingleAxisJson);
        _sut.SetManualOverride("R2", overridePath);

        Assert.True(eventFired);
        Assert.True(_sut.LoadedScripts.ContainsKey("R2"));
    }

    [Fact]
    public void ClearManualOverride_RemovesOverride()
    {
        var overridePath = CreateFunscript("custom.funscript", SingleAxisJson);
        _sut.SetManualOverride("R0", overridePath);

        _sut.ClearManualOverride("R0");

        Assert.False(_sut.HasManualOverrides);
        Assert.Empty(_sut.ManualOverrides);
    }

    [Fact]
    public void ClearAllManualOverrides_RemovesAll()
    {
        _sut.SetManualOverride("R0", "a.funscript");
        _sut.SetManualOverride("R1", "b.funscript");

        _sut.ClearAllManualOverrides();

        Assert.False(_sut.HasManualOverrides);
    }

    // --- Unload ---

    [Fact]
    public void ClearScripts_ClearsLoadedScripts()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);
        _sut.LoadScriptsForVideo(VideoPath());

        var logs = _sut.ClearScripts();

        Assert.Empty(_sut.LoadedScripts);
        Assert.Null(_sut.CurrentVideoPath);
        Assert.Contains(logs, l => l.Contains("Cleared"));
    }

    [Fact]
    public void ClearScripts_DoesNotClearManualOverrides()
    {
        _sut.SetManualOverride("R0", "custom.funscript");

        _sut.ClearScripts();

        Assert.True(_sut.HasManualOverrides);
    }

    [Fact]
    public void ClearScripts_WhenNothingLoaded_NoLogMessage()
    {
        var logs = _sut.ClearScripts();

        Assert.Empty(logs);
    }

    // --- ScriptsChanged event ---

    [Fact]
    public void LoadScriptsForVideo_FiresScriptsChanged()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);
        IReadOnlyDictionary<string, FunscriptData>? receivedScripts = null;
        _sut.ScriptsChanged += scripts => receivedScripts = scripts;

        _sut.LoadScriptsForVideo(VideoPath());

        Assert.NotNull(receivedScripts);
        Assert.Single(receivedScripts!);
    }

    [Fact]
    public void ClearScripts_FiresScriptsChanged()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);
        _sut.LoadScriptsForVideo(VideoPath());

        bool eventFired = false;
        _sut.ScriptsChanged += _ => eventFired = true;

        _sut.ClearScripts();

        Assert.True(eventFired);
    }

    // --- CurrentVideoPath ---

    [Fact]
    public void LoadScriptsForVideo_SetsCurrentVideoPath()
    {
        var video = VideoPath();

        _sut.LoadScriptsForVideo(video);

        Assert.Equal(video, _sut.CurrentVideoPath);
    }

    // --- Invalid input ---

    [Fact]
    public void LoadScriptsForVideo_NullPath_ReturnsEmptyGracefully()
    {
        var logs = _sut.LoadScriptsForVideo(null!);

        Assert.Empty(_sut.LoadedScripts);
        Assert.Contains(logs, l => l.Contains("No video path"));
    }

    [Fact]
    public void LoadScriptsForVideo_EmptyPath_ReturnsEmptyGracefully()
    {
        var logs = _sut.LoadScriptsForVideo("");

        Assert.Empty(_sut.LoadedScripts);
    }

    // --- Manual override with missing file ---

    [Fact]
    public void ManualOverride_MissingFile_LogsWarning()
    {
        _sut.SetManualOverride("R0", @"C:\nonexistent\file.funscript");

        var logs = _sut.LoadScriptsForVideo(VideoPath());

        Assert.Contains(logs, l => l.Contains("Manual override") && l.Contains("not found"));
    }

    // --- Logging ---

    [Fact]
    public void LoadScriptsForVideo_LogsActionCounts()
    {
        CreateFunscript("Movie.funscript", SingleAxisJson);

        var logs = _sut.LoadScriptsForVideo(VideoPath());

        Assert.Contains(logs, l => l.Contains("2 actions"));
    }
}
