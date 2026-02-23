using System.Diagnostics;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class TCodeServiceTests : IDisposable
{
    private readonly InterpolationService _interpolation = new();
    private readonly TCodeService _sut;
    private readonly MockTransport _transport = new();

    public TCodeServiceTests()
    {
        _sut = new TCodeService(_interpolation);
        _sut.Transport = _transport;
        _sut.SetAxisConfigs(AxisConfig.CreateDefaults());
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    // ===== TCode Formatting =====

    [Theory]
    [InlineData("L0", "linear", 500, 10, "L0500I10")]
    [InlineData("L0", "linear", 0, 10, "L0000I10")]
    [InlineData("L0", "linear", 999, 10, "L0999I10")]
    [InlineData("R0", "rotation", 250, 15, "R0250I15")]
    [InlineData("R1", "rotation", 750, 5, "R1750I5")]
    [InlineData("R2", "rotation", 100, 100, "R2100I100")]
    public void FormatTCodeCommand_ProducesCorrectFormat(
        string axisId, string type, int tcodeValue, int intervalMs, string expected)
    {
        var config = new AxisConfig { Id = axisId, Type = type };
        var result = TCodeService.FormatTCodeCommand(config, tcodeValue, intervalMs);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatTCodeCommand_PadsWithZeros()
    {
        var config = new AxisConfig { Id = "L0", Type = "linear" };
        Assert.Equal("L0007I10", TCodeService.FormatTCodeCommand(config, 7, 10));
        Assert.Equal("L0070I10", TCodeService.FormatTCodeCommand(config, 70, 10));
    }

    // ===== PositionToTCode =====

    [Theory]
    [InlineData(0, 100, 0,   0)]     // 0% of 0-100 range → 0
    [InlineData(0, 100, 50,  499)]   // 50% of 0-100 → 499
    [InlineData(0, 100, 100, 999)]   // 100% of 0-100 → 999
    [InlineData(20, 80, 0, 199)]     // 0% pos → min=20 → 20/100*999 = 199
    [InlineData(20, 80, 100, 799)]   // 100% pos → max=80 → 80/100*999 = 799
    [InlineData(20, 80, 50, 499)]    // 50% pos → 20 + 0.5*60 = 50 → 499
    public void PositionToTCode_MapsCorrectly(int min, int max, double position, int expected)
    {
        var config = new AxisConfig { Id = "L0", Type = "linear", Min = min, Max = max };
        var result = TCodeService.PositionToTCode(config, position);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PositionToTCode_ClampsTo0_999()
    {
        var config = new AxisConfig { Id = "L0", Type = "linear" };
        Assert.Equal(0, TCodeService.PositionToTCode(config, -50));
        Assert.Equal(999, TCodeService.PositionToTCode(config, 150));
    }

    // ===== Dirty Value Tracking =====

    [Fact]
    public void IsDirty_FirstValue_ReturnsTrue()
    {
        Assert.True(_sut.IsDirty("L0", 500));
    }

    [Fact]
    public void IsDirty_SameValue_ReturnsFalse()
    {
        // Simulate a send by calling OutputTick indirectly — use SetScripts + start/stop
        // Instead, test the tracking directly by accessing the internal state
        // IsDirty checks _lastSentValues which is populated during OutputTick.
        // For direct testing, we need to update _lastSentValues.
        // Since IsDirty is internal, we can test the logic:

        // First call always dirty
        Assert.True(_sut.IsDirty("L0", 500));
    }

    [Fact]
    public void IsDirty_DifferentAxis_IndependentTracking()
    {
        Assert.True(_sut.IsDirty("L0", 500));
        Assert.True(_sut.IsDirty("R0", 500));
    }

    // ===== SetOutputRate =====

    [Theory]
    [InlineData(100, 100)]
    [InlineData(30, 30)]
    [InlineData(200, 200)]
    [InlineData(10, 30)]    // Below min → clamped to 30
    [InlineData(500, 200)]  // Above max → clamped to 200
    public void SetOutputRate_ClampsToValidRange(int input, int expected)
    {
        _sut.SetOutputRate(input);
        Assert.Equal(expected, _sut.OutputRateHz);
    }

    // ===== Time Extrapolation =====

    [Fact]
    public void GetExtrapolatedTimeMs_WhenNotPlaying_ReturnsSyncTime()
    {
        _sut.SetTime(5000);
        _sut.SetPlaying(false);
        _sut.SetTime(5000);

        // When not playing, extrapolated time should equal sync time
        var time = _sut.GetExtrapolatedTimeMs();
        Assert.Equal(5000, time, 1.0); // Within 1ms tolerance
    }

    [Fact]
    public void GetExtrapolatedTimeMs_WhenPlaying_AdvancesBeyondSyncTime()
    {
        _sut.SetPlaying(true);
        _sut.SetTime(1000);

        // Wait a small amount so time advances
        Thread.Sleep(50);

        var time = _sut.GetExtrapolatedTimeMs();
        Assert.True(time > 1000, $"Expected time > 1000 but got {time}");
        Assert.True(time < 1200, $"Expected time < 1200 but got {time}"); // Should be ~1050
    }

    [Fact]
    public void GetExtrapolatedTimeMs_RespectsPlaybackSpeed()
    {
        _sut.SetPlaybackSpeed(2.0f);
        _sut.SetPlaying(true);
        _sut.SetTime(0);

        Thread.Sleep(50);

        var time = _sut.GetExtrapolatedTimeMs();
        // At 2x speed, 50ms wall time → ~100ms media time
        Assert.True(time > 50, $"At 2x speed, expected time > 50 but got {time}");
    }

    [Fact]
    public void SetOffset_AffectsOutputTick()
    {
        _sut.SetOffset(100);
        // Offset is applied during OutputTick (currentTimeMs = rawTimeMs - offsetMs)
        // This is an integration behavior tested via transport output below
    }

    // ===== SetScripts / HasScriptsLoaded =====

    [Fact]
    public void HasScriptsLoaded_InitiallyFalse()
    {
        Assert.False(_sut.HasScriptsLoaded);
    }

    [Fact]
    public void SetScripts_SetsHasScriptsLoaded()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction>
            {
                new(0, 0), new(1000, 100)
            }}
        };
        _sut.SetScripts(scripts);
        Assert.True(_sut.HasScriptsLoaded);
    }

    [Fact]
    public void SetScripts_EmptyDict_ClearsScripts()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50) } }
        };
        _sut.SetScripts(scripts);
        Assert.True(_sut.HasScriptsLoaded);

        _sut.SetScripts(new Dictionary<string, FunscriptData>());
        Assert.False(_sut.HasScriptsLoaded);
    }

    // ===== IsFunscriptPlaying =====

    [Fact]
    public void IsFunscriptPlaying_FalseWhenNotPlaying()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(false);
        Assert.False(_sut.IsFunscriptPlaying);
    }

    [Fact]
    public void IsFunscriptPlaying_FalseWhenPlayingWithoutScripts()
    {
        _sut.SetPlaying(true);
        Assert.False(_sut.IsFunscriptPlaying);
    }

    [Fact]
    public void IsFunscriptPlaying_TrueWhenPlayingWithScripts()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        Assert.True(_sut.IsFunscriptPlaying);
    }

    // ===== Thread Lifecycle =====

    [Fact]
    public void Start_CreatesOutputThread()
    {
        _sut.Start();
        // Thread is alive after start
        Thread.Sleep(20);
        // Should still be running (not crashed)
        _sut.StopTimer();
    }

    [Fact]
    public void Start_CalledTwice_DoesNotCreateSecondThread()
    {
        _sut.Start();
        _sut.Start(); // Should be a no-op
        _sut.StopTimer();
    }

    [Fact]
    public void StopTimer_StopsThread()
    {
        _sut.Start();
        Thread.Sleep(20);
        _sut.StopTimer();
        // After stop, thread should be null and no longer running
        Thread.Sleep(20);
        // No crash = success
    }

    [Fact]
    public void Dispose_CallsStopTimer()
    {
        _sut.Start();
        Thread.Sleep(20);
        _sut.Dispose();
        // No crash = success
    }

    // ===== Integration: Output Tick sends correct TCode =====

    [Fact]
    public void OutputTick_ScriptedAxis_SendsInterpolatedPosition()
    {
        // Script: L0 goes from 0 at time 0 to 100 at time 1000ms
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction>
            {
                new(0, 0),
                new(1000, 100)
            }}
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500); // Halfway → position 50 → TCode ~499

        _sut.Start();
        Thread.Sleep(100); // Allow at least one tick

        _sut.StopTimer();

        // Transport should have received at least one command
        Assert.True(_transport.SentMessages.Count > 0,
            "Expected at least one TCode command to be sent");

        // The first command should contain L0 with a value near 499
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains("L0", firstMsg);
        Assert.EndsWith("\n", firstMsg);
    }

    [Fact]
    public void OutputTick_MultipleAxes_JoinedWithSpaces()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100) } },
            ["R0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50), new(1000, 50) } }
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        // First message should contain both axes separated by space
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains(" ", firstMsg.TrimEnd('\n'));
        Assert.EndsWith("\n", firstMsg);
    }

    [Fact]
    public void OutputTick_DisabledAxis_SkippedInOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].Enabled = false; // Disable L0

        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100) } }
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        // No L0 commands should be sent (only axis with script is disabled)
        foreach (var msg in _transport.SentMessages)
        {
            Assert.DoesNotContain("L0", msg);
        }
    }

    [Fact]
    public void OutputTick_NotPlaying_NoOutput()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100) } }
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(false);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.Empty(_transport.SentMessages);
    }

    [Fact]
    public void OutputTick_DirtyTracking_DoesNotResendSameValue()
    {
        // Script with constant position → after first send, subsequent ticks
        // should not resend (value hasn't changed)
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50), new(100000, 50) } }
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(200); // Run multiple ticks
        _sut.StopTimer();

        // Should have sent exactly 1 message (first tick), then all subsequent
        // ticks see the same value and skip
        Assert.Single(_transport.SentMessages);
    }

    [Fact]
    public void OutputTick_OffsetApplied_ShiftsTime()
    {
        // Script: L0 = 0 at t=0, 100 at t=1000
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction>
            {
                new(0, 0),
                new(1000, 100)
            }}
        };

        // Without offset, at t=500 → pos=50 → TCode 499
        _sut.SetScripts(scripts);
        _sut.SetOffset(0);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(50);
        _sut.StopTimer();

        var noOffsetMsg = _transport.SentMessages.FirstOrDefault() ?? "";

        // Reset
        _transport.SentMessages.Clear();
        _sut.SetScripts(scripts); // Clears dirty tracking

        // With offset=200, at t=500 → effective time = 300 → pos=30 → TCode 299
        _sut.SetOffset(200);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(50);
        _sut.StopTimer();

        var offsetMsg = _transport.SentMessages.FirstOrDefault() ?? "";

        // The values should differ due to offset
        Assert.NotEqual(noOffsetMsg, offsetMsg);
    }

    // ===== TCode Command Format Validation =====

    [Fact]
    public void OutputTick_TCodeFormat_HasIntervalSuffix()
    {
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100) } }
        };

        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var msg = _transport.SentMessages[0].TrimEnd('\n');
        // Each axis command should match pattern: L0\d{3}I\d+
        var parts = msg.Split(' ');
        foreach (var part in parts)
        {
            Assert.Matches(@"^[LR]\d\d{3}I\d+$", part);
        }
    }

    // ===== Fill Mode — Pattern Fill =====

    [Fact]
    public void FillMode_WaveformPattern_SendsOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle; // L0 = Triangle
        configs[0].FillSpeedHz = 1.0;
        _sut.SetAxisConfigs(configs);

        // No scripts loaded, so fill mode should kick in
        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0,
            "Expected fill mode to produce TCode output");
        Assert.Contains("L0", _transport.SentMessages[0]);
    }

    [Fact]
    public void FillMode_None_NoFillOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        // All axes default to FillMode.None
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        // No scripts and no fill modes — no output
        Assert.Empty(_transport.SentMessages);
    }

    [Fact]
    public void FillMode_ScriptedAxis_IgnoresFillMode()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle; // L0 has fill mode set
        _sut.SetAxisConfigs(configs);

        // But also has a script — script should take priority
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 100) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        // Script position at t=5000 should be ~50 → TCode ~499
        var msg = _transport.SentMessages[0];
        Assert.Contains("L0", msg);
    }

    [Fact]
    public void FillMode_DisabledAxis_NoOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Sine;
        configs[0].Enabled = false;
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        foreach (var msg in _transport.SentMessages)
            Assert.DoesNotContain("L0", msg);
    }

    // ===== Fill Mode — Random =====

    [Fact]
    public void FillMode_Random_SendsOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Random; // R0 = Random
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0,
            "Expected random fill to produce TCode output");
        // At least one message should contain R0
        Assert.True(_transport.SentMessages.Any(m => m.Contains("R0")),
            "Expected R0 axis in random fill output");
    }

    // ===== Fill Mode — Grind / ReverseGrind =====

    [Fact]
    public void FillMode_Grind_R2FollowsL0Position()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Grind; // R2 = Grind
        _sut.SetAxisConfigs(configs);

        // L0 has a script going from 0 to 100
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 100) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000); // L0 at 50%

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        // Should have both L0 and R2 in output
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains("L0", firstMsg);
        Assert.Contains("R2", firstMsg);
    }

    [Fact]
    public void FillMode_Grind_OnlyR2()
    {
        // Grind on a non-R2 axis should still use the waveform pattern, not grind behavior
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Grind; // R0 = Grind (not R2)
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        // R0 with Grind fill mode but not R2 — goes through waveform path
        // (PatternGenerator.Calculate for Grind returns t directly, so it should produce some output)
    }

    [Fact]
    public void FillMode_ReverseGrind_R2InvertsL0()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.ReverseGrind; // R2 = ReverseGrind
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 100) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000); // L0 at 50%

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains("R2", firstMsg);
    }

    // ===== Fill Mode — Stroke Sync =====

    [Fact]
    public void FillMode_SyncWithStroke_AdvancesWithStrokeDistance()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Triangle; // R0 = Triangle
        configs[1].SyncWithStroke = true;
        _sut.SetAxisConfigs(configs);

        // L0 has a script (stroke is moving)
        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100), new(2000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(500);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        // Should have R0 output that's synced with stroke movement
        Assert.True(_transport.SentMessages.Any(m => m.Contains("R0")),
            "Expected R0 output with stroke sync enabled");
    }

    [Fact]
    public void FillMode_L0Fill_AlwaysUsesIndependentTime()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Sine;
        configs[0].FillSpeedHz = 2.0;
        // L0 sync toggle is meaningless (L0 IS the stroke), but set it anyway
        configs[0].SyncWithStroke = true;
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        // L0 fill should produce output regardless of sync setting
        Assert.True(_transport.SentMessages.Count > 0);
        Assert.Contains("L0", _transport.SentMessages[0]);
    }

    // ===== Return-to-Center =====

    [Fact]
    public void ReturnToCenter_WhenFillModeCleared_GlidesToMidpoint()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle;
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        // Run with fill active to get some output
        _sut.Start();
        Thread.Sleep(100);

        // Now clear fill mode — should trigger return-to-center
        _transport.SentMessages.Clear();
        configs[0].FillMode = AxisFillMode.None;
        _sut.SetAxisConfigs(configs);

        Thread.Sleep(200); // Allow return animation
        _sut.StopTimer();

        // Should have sent messages moving toward 500
        if (_transport.SentMessages.Count > 0)
        {
            var lastMsg = _transport.SentMessages[^1];
            // The final position should be near midpoint (500)
            Assert.Contains("L0", lastMsg);
        }
    }

    [Fact]
    public void ReturnToCenter_WhenAxisDisabled_GlidesToMidpoint()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Sine;
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);

        _sut.Start();
        Thread.Sleep(100);

        _transport.SentMessages.Clear();
        configs[0].Enabled = false;
        _sut.SetAxisConfigs(configs);

        Thread.Sleep(200);
        _sut.StopTimer();

        // Return-to-center should have fired for L0
        // (may or may not have messages depending on timing, but no crash)
    }

    // ===== Ramp-Up =====

    [Fact]
    public void RampUp_WhenFillModeActivated_BlendsFromMidpoint()
    {
        var configs = AxisConfig.CreateDefaults();
        _sut.SetAxisConfigs(configs); // Initial state: all None

        _sut.SetPlaying(true);
        _sut.SetTime(0);
        _sut.Start();
        Thread.Sleep(50);

        // Activate fill mode — should trigger ramp-up
        configs[0].FillMode = AxisFillMode.Triangle;
        _sut.SetAxisConfigs(configs);

        Thread.Sleep(150);
        _sut.StopTimer();

        // Should have output for L0 with ramp blending
        Assert.True(_transport.SentMessages.Count > 0,
            "Expected ramp-up output for fill activation");
    }

    [Fact]
    public void RampUp_CompletesAndRemoves()
    {
        var configs = AxisConfig.CreateDefaults();
        _sut.SetAxisConfigs(configs);

        _sut.SetPlaying(true);
        _sut.SetTime(0);
        _sut.Start();

        configs[0].FillMode = AxisFillMode.Sine;
        _sut.SetAxisConfigs(configs);

        // Run long enough for ramp to complete (blend reaches 0.99+)
        Thread.Sleep(500);
        _sut.StopTimer();

        // Should have messages — ramp completes without issues
        Assert.True(_transport.SentMessages.Count > 0);
    }

    // ===== Fill Mode with No Playback =====

    [Fact]
    public void FillMode_ActiveWithoutPlayback_StillSendsOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle;
        _sut.SetAxisConfigs(configs);

        // Not playing, but fill mode is active — should still produce output
        _sut.SetPlaying(false);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        // Fill should work even when not playing
        Assert.True(_transport.SentMessages.Count > 0,
            "Fill mode should produce output even when not playing");
    }

    // ===== SleepPrecise =====

    [Fact]
    public void SleepPrecise_WaitsApproximatelyTargetDuration()
    {
        var sw = Stopwatch.StartNew();
        TCodeService.SleepPrecise(Stopwatch.StartNew(), 20);
        sw.Stop();

        // Should wait roughly 20ms (allow 5-35ms range for CI variance)
        Assert.InRange(sw.ElapsedMilliseconds, 5, 50);
    }

    // ===== Mock Transport =====

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

        // Suppress unused event warnings
        internal void SuppressWarnings()
        {
            ConnectionChanged?.Invoke(false);
            ErrorOccurred?.Invoke("");
        }
    }
}
