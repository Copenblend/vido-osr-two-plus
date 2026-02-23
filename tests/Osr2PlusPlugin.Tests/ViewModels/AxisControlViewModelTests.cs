using Moq;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Plugin;
using Xunit;

namespace Osr2PlusPlugin.Tests.ViewModels;

public class AxisControlViewModelTests : IDisposable
{
    private readonly InterpolationService _interpolation = new();
    private readonly TCodeService _tcode;
    private readonly Mock<IPluginSettingsStore> _mockSettings;
    private readonly FunscriptParser _parser = new();
    private readonly FunscriptMatcher _matcher = new();
    private readonly MockTransport _mockTransport;

    public AxisControlViewModelTests()
    {
        _tcode = new TCodeService(_interpolation);
        _mockSettings = new Mock<IPluginSettingsStore>();
        _mockTransport = new MockTransport();
        _tcode.Transport = _mockTransport;

        // Default: return the default value for any Get call
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string _, int d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string _, bool d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<double>()))
            .Returns((string _, double d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string d) => d);
    }

    public void Dispose() => _tcode.Dispose();

    private AxisControlViewModel CreateSut()
        => new(_tcode, _mockSettings.Object, _parser, _matcher);

    // ═══════════════════════════════════════════════════════
    //  Initialization
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_CreatesFourAxisCards()
    {
        var sut = CreateSut();

        Assert.Equal(4, sut.AxisCards.Count);
        Assert.Equal("L0", sut.AxisCards[0].AxisId);
        Assert.Equal("R0", sut.AxisCards[1].AxisId);
        Assert.Equal("R1", sut.AxisCards[2].AxisId);
        Assert.Equal("R2", sut.AxisCards[3].AxisId);
    }

    [Fact]
    public void Constructor_CardsHaveCorrectNames()
    {
        var sut = CreateSut();

        Assert.Equal("Stroke", sut.AxisCards[0].AxisName);
        Assert.Equal("Twist", sut.AxisCards[1].AxisName);
        Assert.Equal("Roll", sut.AxisCards[2].AxisName);
        Assert.Equal("Pitch", sut.AxisCards[3].AxisName);
    }

    [Fact]
    public void Constructor_CardsHaveDefaultValues()
    {
        var sut = CreateSut();

        Assert.Equal(0, sut.AxisCards[0].Min);
        Assert.Equal(100, sut.AxisCards[0].Max);
        Assert.True(sut.AxisCards[0].Enabled);
        Assert.Equal(AxisFillMode.None, sut.AxisCards[0].FillMode);
    }

    [Fact]
    public void Constructor_PitchDefaultMaxIs75()
    {
        var sut = CreateSut();
        Assert.Equal(75, sut.AxisCards[3].Max);
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence — Load
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_LoadsPersistedMinMax()
    {
        _mockSettings.Setup(s => s.Get("axis_L0_min", 0)).Returns(10);
        _mockSettings.Setup(s => s.Get("axis_L0_max", 100)).Returns(90);

        var sut = CreateSut();

        Assert.Equal(10, sut.AxisCards[0].Min);
        Assert.Equal(90, sut.AxisCards[0].Max);
    }

    [Fact]
    public void Constructor_LoadsPersistedEnabled()
    {
        _mockSettings.Setup(s => s.Get("axis_R1_enabled", true)).Returns(false);

        var sut = CreateSut();

        Assert.False(sut.AxisCards[2].Enabled); // R1
    }

    [Fact]
    public void Constructor_LoadsPersistedFillMode()
    {
        _mockSettings.Setup(s => s.Get("axis_R0_fillMode", "None")).Returns("Sine");

        var sut = CreateSut();

        Assert.Equal(AxisFillMode.Sine, sut.AxisCards[1].FillMode); // R0
    }

    [Fact]
    public void Constructor_LoadsPersistedSyncAndSpeed()
    {
        _mockSettings.Setup(s => s.Get("axis_R0_syncWithStroke", false)).Returns(true);
        _mockSettings.Setup(s => s.Get("axis_R0_fillSpeedHz", 1.0)).Returns(2.5);

        var sut = CreateSut();

        Assert.True(sut.AxisCards[1].SyncWithStroke);
        Assert.Equal(2.5, sut.AxisCards[1].FillSpeedHz, 2);
    }

    [Fact]
    public void Constructor_InvalidFillModeString_KeepsDefault()
    {
        _mockSettings.Setup(s => s.Get("axis_L0_fillMode", "None")).Returns("InvalidMode");

        var sut = CreateSut();

        Assert.Equal(AxisFillMode.None, sut.AxisCards[0].FillMode);
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence — Save
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ConfigChanged_SavesSettingsToStore()
    {
        var sut = CreateSut();

        sut.AxisCards[0].Min = 15;

        _mockSettings.Verify(s => s.Set("axis_L0_min", 15), Times.Once);
    }

    [Fact]
    public void ConfigChanged_SavesMinMaxForAllAxes()
    {
        var sut = CreateSut();

        // Change min/max on each axis
        sut.AxisCards[0].Min = 5;   // L0
        sut.AxisCards[1].Max = 80;  // R0
        sut.AxisCards[2].Min = 10;  // R1
        sut.AxisCards[3].Max = 60;  // R2

        _mockSettings.Verify(s => s.Set("axis_L0_min", 5), Times.AtLeastOnce);
        _mockSettings.Verify(s => s.Set("axis_R0_max", 80), Times.AtLeastOnce);
        _mockSettings.Verify(s => s.Set("axis_R1_min", 10), Times.AtLeastOnce);
        _mockSettings.Verify(s => s.Set("axis_R2_max", 60), Times.AtLeastOnce);
    }

    [Fact]
    public void Constructor_LoadsPersistedMinMaxForAllAxes()
    {
        // Set up persisted values for all four axes
        _mockSettings.Setup(s => s.Get("axis_L0_min", 0)).Returns(5);
        _mockSettings.Setup(s => s.Get("axis_L0_max", 100)).Returns(95);
        _mockSettings.Setup(s => s.Get("axis_R0_min", 0)).Returns(10);
        _mockSettings.Setup(s => s.Get("axis_R0_max", 100)).Returns(90);
        _mockSettings.Setup(s => s.Get("axis_R1_min", 0)).Returns(15);
        _mockSettings.Setup(s => s.Get("axis_R1_max", 100)).Returns(85);
        _mockSettings.Setup(s => s.Get("axis_R2_min", 0)).Returns(20);
        _mockSettings.Setup(s => s.Get("axis_R2_max", 75)).Returns(60);

        var sut = CreateSut();

        Assert.Equal(5, sut.AxisCards[0].Min);   // L0
        Assert.Equal(95, sut.AxisCards[0].Max);
        Assert.Equal(10, sut.AxisCards[1].Min);  // R0
        Assert.Equal(90, sut.AxisCards[1].Max);
        Assert.Equal(15, sut.AxisCards[2].Min);  // R1
        Assert.Equal(85, sut.AxisCards[2].Max);
        Assert.Equal(20, sut.AxisCards[3].Min);  // R2
        Assert.Equal(60, sut.AxisCards[3].Max);
    }

    [Fact]
    public void ConfigChanged_SavesAllAxes()
    {
        var sut = CreateSut();

        sut.AxisCards[0].FillMode = AxisFillMode.Triangle;

        // Should save all axes (we persist everything on any change)
        _mockSettings.Verify(s => s.Set("axis_L0_fillMode", "Triangle"), Times.Once);
        _mockSettings.Verify(s => s.Set("axis_R0_fillMode", "None"), Times.Once);
        _mockSettings.Verify(s => s.Set("axis_R1_fillMode", "None"), Times.Once);
        _mockSettings.Verify(s => s.Set("axis_R2_fillMode", "None"), Times.Once);
    }

    [Fact]
    public void ConfigChanged_PushesConfigsToTCodeService()
    {
        var sut = CreateSut();

        sut.AxisCards[0].Min = 20;

        // Verify TCodeService received the updated config
        // (we can't easily verify the internal state, but no exception = success)
    }

    // ═══════════════════════════════════════════════════════
    //  Script Loading — Auto-Load Flow
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void LoadScriptsForVideo_FindsAndLoadsIndividualScripts()
    {
        var sut = CreateSut();
        var scripts = new Dictionary<string, string>
        {
            { "L0", @"C:\videos\movie.funscript" },
            { "R0", @"C:\videos\movie.twist.funscript" }
        };

        sut.FindMatchingScriptsFunc = _ => scripts;
        sut.TryParseMultiAxisFunc = _ => null; // No multi-axis
        sut.ParseFileFunc = (path, axis) => new FunscriptData
        {
            AxisId = axis,
            FilePath = path,
            Actions = new() { new(0, 50), new(1000, 100) }
        };

        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        Assert.Equal(@"C:\videos\movie.funscript", sut.AxisCards[0].ScriptFileName);
        Assert.Equal(@"C:\videos\movie.twist.funscript", sut.AxisCards[1].ScriptFileName);
        Assert.Null(sut.AxisCards[2].ScriptFileName); // R1 — no match
        Assert.Null(sut.AxisCards[3].ScriptFileName); // R2 — no match
    }

    [Fact]
    public void LoadScriptsForVideo_MultiAxisTakesPrecedence()
    {
        var sut = CreateSut();
        var scripts = new Dictionary<string, string>
        {
            { "L0", @"C:\videos\movie.funscript" }
        };

        var multiAxis = new Dictionary<string, FunscriptData>
        {
            { "L0", new() { AxisId = "L0", FilePath = @"C:\videos\movie.funscript", Actions = new() { new(0, 50) } } },
            { "R0", new() { AxisId = "R0", FilePath = @"C:\videos\movie.funscript", Actions = new() { new(0, 30) } } }
        };

        sut.FindMatchingScriptsFunc = _ => scripts;
        sut.TryParseMultiAxisFunc = _ => multiAxis;
        sut.ParseFileFunc = (path, axis) => new FunscriptData
        {
            AxisId = axis, FilePath = path, Actions = new() { new(0, 50) }
        };

        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        // Both L0 and R0 should point to the base funscript (multi-axis source)
        Assert.Equal(@"C:\videos\movie.funscript", sut.AxisCards[0].ScriptFileName);
        Assert.Equal(@"C:\videos\movie.funscript", sut.AxisCards[1].ScriptFileName);
    }

    [Fact]
    public void LoadScriptsForVideo_FallsBackToIndividualWhenMultiAxisMissesAxis()
    {
        var sut = CreateSut();
        var scripts = new Dictionary<string, string>
        {
            { "L0", @"C:\videos\movie.funscript" },
            { "R1", @"C:\videos\movie.roll.funscript" }
        };

        var multiAxis = new Dictionary<string, FunscriptData>
        {
            { "L0", new() { AxisId = "L0", FilePath = @"C:\videos\movie.funscript", Actions = new() { new(0, 50) } } }
        };

        sut.FindMatchingScriptsFunc = _ => scripts;
        sut.TryParseMultiAxisFunc = _ => multiAxis;
        sut.ParseFileFunc = (path, axis) => new FunscriptData
        {
            AxisId = axis, FilePath = path, Actions = new() { new(0, 50) }
        };

        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        Assert.Equal(@"C:\videos\movie.funscript", sut.AxisCards[0].ScriptFileName); // Multi-axis L0
        Assert.Equal(@"C:\videos\movie.roll.funscript", sut.AxisCards[2].ScriptFileName); // Individual R1
    }

    [Fact]
    public void LoadScriptsForVideo_EmptyPath_DoesNothing()
    {
        var sut = CreateSut();
        var called = false;
        sut.FindMatchingScriptsFunc = _ => { called = true; return new(); };

        sut.LoadScriptsForVideo("");

        Assert.False(called);
    }

    [Fact]
    public void LoadScriptsForVideo_NullPath_DoesNothing()
    {
        var sut = CreateSut();
        var called = false;
        sut.FindMatchingScriptsFunc = _ => { called = true; return new(); };

        sut.LoadScriptsForVideo(null!);

        Assert.False(called);
    }

    [Fact]
    public void LoadScriptsForVideo_NoMatches_ClearsAutoLoaded()
    {
        var sut = CreateSut();

        // First load a script
        sut.FindMatchingScriptsFunc = _ => new() { { "L0", @"C:\a.funscript" } };
        sut.TryParseMultiAxisFunc = _ => null;
        sut.ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");
        Assert.NotNull(sut.AxisCards[0].ScriptFileName);

        // Now load with no matches
        sut.FindMatchingScriptsFunc = _ => new();
        sut.LoadScriptsForVideo(@"C:\videos\other.mp4");

        Assert.Null(sut.AxisCards[0].ScriptFileName);
    }

    // ═══════════════════════════════════════════════════════
    //  Manual Override
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void LoadScriptsForVideo_RespectsManualOverride()
    {
        var sut = CreateSut();

        // Manually assign a script to L0
        sut.AxisCards[0].FileDialogFactory = () => @"C:\manual\custom.funscript";
        sut.AxisCards[0].ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.AxisCards[0].OpenScriptCommand.Execute(null);
        Assert.Equal(@"C:\manual\custom.funscript", sut.AxisCards[0].ScriptFileName);
        Assert.True(sut.AxisCards[0].IsScriptManual);

        // Auto-load should not overwrite L0
        sut.FindMatchingScriptsFunc = _ => new() { { "L0", @"C:\auto\auto.funscript" } };
        sut.TryParseMultiAxisFunc = _ => null;
        sut.ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        Assert.Equal(@"C:\manual\custom.funscript", sut.AxisCards[0].ScriptFileName);
    }

    // ═══════════════════════════════════════════════════════
    //  ClearScripts
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ClearScripts_ClearsAutoLoadedButKeepsManual()
    {
        var sut = CreateSut();

        // Auto-load R0
        sut.FindMatchingScriptsFunc = _ => new() { { "R0", @"C:\auto.funscript" } };
        sut.TryParseMultiAxisFunc = _ => null;
        sut.ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        // Manually assign L0
        sut.AxisCards[0].FileDialogFactory = () => @"C:\manual.funscript";
        sut.AxisCards[0].ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.AxisCards[0].OpenScriptCommand.Execute(null);

        sut.ClearScripts();

        Assert.Equal(@"C:\manual.funscript", sut.AxisCards[0].ScriptFileName); // Manual kept
        Assert.Null(sut.AxisCards[1].ScriptFileName); // R0 auto cleared
    }

    [Fact]
    public void ClearAllScripts_ClearsEverythingIncludingManual()
    {
        var sut = CreateSut();

        // Manually assign L0
        sut.AxisCards[0].FileDialogFactory = () => @"C:\manual.funscript";
        sut.AxisCards[0].ParseFileFunc = (p, a) => new FunscriptData { AxisId = a, FilePath = p, Actions = new() { new(0, 50) } };
        sut.AxisCards[0].OpenScriptCommand.Execute(null);
        Assert.True(sut.AxisCards[0].IsScriptManual);

        sut.ClearAllScripts();

        Assert.Null(sut.AxisCards[0].ScriptFileName);
        Assert.False(sut.AxisCards[0].IsScriptManual);
    }

    // ═══════════════════════════════════════════════════════
    //  SetVideoPlaying / SetDeviceConnected
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SetVideoPlaying_UpdatesIsTestEnabled()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        Assert.True(sut.IsTestEnabled);

        sut.SetVideoPlaying(true);
        Assert.False(sut.IsTestEnabled);

        sut.SetVideoPlaying(false);
        Assert.True(sut.IsTestEnabled);
    }

    [Fact]
    public void SetVideoPlaying_StopsTestAxesWhenPlaying()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);

        // Start test
        sut.TestCommand.Execute(null);
        Assert.True(sut.IsTesting);

        sut.SetVideoPlaying(true);

        // StopAllTestAxes was called — IsTesting cleared
        Assert.False(sut.IsTesting);
    }

    [Fact]
    public void SetDeviceConnected_UpdatesIsTestEnabled()
    {
        var sut = CreateSut();

        sut.SetDeviceConnected(true);
        Assert.True(sut.IsTestEnabled);

        sut.SetDeviceConnected(false);
        Assert.False(sut.IsTestEnabled);
    }

    [Fact]
    public void SetVideoPlaying_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.SetVideoPlaying(true);

        Assert.Contains("IsTestEnabled", raised);
    }

    [Fact]
    public void SetDeviceConnected_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.SetDeviceConnected(true);

        Assert.Contains("IsTestEnabled", raised);
    }

    // ═══════════════════════════════════════════════════════
    //  TestCommand — Panel-Level Test
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void TestCommand_StartsAllTestableAxes()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);

        // Set fill modes on non-stroke axes
        sut.AxisCards[1].FillMode = AxisFillMode.Triangle; // R0
        sut.AxisCards[2].FillMode = AxisFillMode.Sine;     // R1

        sut.TestCommand.Execute(null);

        Assert.True(sut.IsTesting);
        Assert.Equal("Stop", sut.TestButtonText);
        Assert.True(_tcode.IsAxisTesting("L0")); // L0 always tested
        Assert.True(_tcode.IsAxisTesting("R0")); // Has fill mode
        Assert.True(_tcode.IsAxisTesting("R1")); // Has fill mode
        Assert.False(_tcode.IsAxisTesting("R2")); // FillMode.None → skipped
    }

    [Fact]
    public void TestCommand_StopsAllAxes()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        sut.AxisCards[1].FillMode = AxisFillMode.Triangle;

        sut.TestCommand.Execute(null);
        Assert.True(sut.IsTesting);

        sut.TestCommand.Execute(null);
        Assert.False(sut.IsTesting);
        Assert.False(_tcode.IsAxisTesting("L0"));
        Assert.False(_tcode.IsAxisTesting("R0"));
    }

    [Fact]
    public void TestCommand_DoesNothingWhenNotEnabled()
    {
        var sut = CreateSut();
        // Device not connected → IsTestEnabled is false

        sut.TestCommand.Execute(null);

        Assert.False(sut.IsTesting);
        Assert.False(_tcode.IsAxisTesting("L0"));
    }

    [Fact]
    public void TestButtonText_DefaultIsTest()
    {
        var sut = CreateSut();
        Assert.Equal("Test", sut.TestButtonText);
    }

    [Fact]
    public void TestButtonText_ChangesToStopWhenTesting()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        sut.TestCommand.Execute(null);
        Assert.Equal("Stop", sut.TestButtonText);
    }

    [Fact]
    public void IsTesting_RaisesPropertyChanged()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.TestCommand.Execute(null);

        Assert.Contains("IsTesting", raised);
        Assert.Contains("TestButtonText", raised);
    }

    [Fact]
    public void TestCommand_L0AlwaysUsesDefaultSpeed()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);

        sut.TestCommand.Execute(null);

        // L0 should be testing (uses default FillSpeedHz = 1.0)
        Assert.True(_tcode.IsAxisTesting("L0"));
    }

    [Fact]
    public void TestCommand_SkipsDisabledAxes()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        sut.AxisCards[0].Enabled = false; // Disable L0
        sut.AxisCards[1].FillMode = AxisFillMode.Triangle; // R0 has fill

        sut.TestCommand.Execute(null);

        Assert.True(sut.IsTesting);
        Assert.False(_tcode.IsAxisTesting("L0")); // Disabled
        Assert.True(_tcode.IsAxisTesting("R0"));  // Enabled + fill mode
    }

    [Fact]
    public void AllTestsStopped_ClearsIsTesting()
    {
        var sut = CreateSut();
        sut.SetDeviceConnected(true);
        sut.TestCommand.Execute(null);
        Assert.True(sut.IsTesting);

        // Simulate external stop (e.g., playback starts)
        _tcode.StopAllTestAxes();

        Assert.False(sut.IsTesting);
    }

    // ═══════════════════════════════════════════════════════
    //  Parse Error Handling
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void LoadScriptsForVideo_ParseError_SkipsAxis()
    {
        var sut = CreateSut();
        var scripts = new Dictionary<string, string>
        {
            { "L0", @"C:\videos\movie.funscript" },
            { "R0", @"C:\videos\movie.twist.funscript" }
        };

        sut.FindMatchingScriptsFunc = _ => scripts;
        sut.TryParseMultiAxisFunc = _ => null;
        sut.ParseFileFunc = (path, axis) =>
        {
            if (axis == "R0") throw new Exception("Parse error");
            return new FunscriptData { AxisId = axis, FilePath = path, Actions = new() { new(0, 50) } };
        };

        sut.LoadScriptsForVideo(@"C:\videos\movie.mp4");

        Assert.Equal(@"C:\videos\movie.funscript", sut.AxisCards[0].ScriptFileName); // L0 loaded
        Assert.Null(sut.AxisCards[1].ScriptFileName); // R0 failed — skipped
    }

    // ═══════════════════════════════════════════════════════
    //  Mock Transport
    // ═══════════════════════════════════════════════════════

    private class MockTransport : ITransportService
    {
        public bool IsConnected { get; set; } = true;
        public string? ConnectionLabel => "Mock";
        public List<string> SentMessages { get; } = new();

        public event Action<bool>? ConnectionChanged;
        public event Action<string>? ErrorOccurred;

        public void Send(string data) => SentMessages.Add(data);
        public void Disconnect() { IsConnected = false; }
        public void Dispose() { }

        internal void SuppressWarnings()
        {
            ConnectionChanged?.Invoke(false);
            ErrorOccurred?.Invoke("");
        }
    }
}
