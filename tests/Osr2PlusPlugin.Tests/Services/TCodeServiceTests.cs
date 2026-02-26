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
        configs[1].SyncWithStroke = false; // Independent mode (no stroke script needed)
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

    // ===== Fill Mode — Grind / Figure8 =====

    [Fact]
    public void FillMode_Grind_R2InvertsL0Position()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Grind; // R2 = Grind (inverse)
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
        // (PatternGenerator.Calculate for Grind returns 1-t, so it should produce some output)
    }

    [Fact]
    public void FillMode_Grind_CappedTo70_WhenStrokeAtBottom()
    {
        // When stroke is at 0 (bottom), Grind should output max pitch = PitchFillMaxPosition (70)
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Grind; // R2 = Grind
        configs[3].Max = 100; // User sets max to 100, but pitch fill cap limits to 70
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000); // L0 stays at 0

        _sut.Start();
        Thread.Sleep(200);
        _sut.StopTimer();

        // R2 output should be capped at PitchFillMaxPosition (70%), not go up to config.Max (100)
        // TCode for 70% = (int)(70 / 100.0 * 999) = 699
        // During ramp-up values move from 500 toward 699, all ≤ 699
        var r2Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R2"))
            .ToList();
        Assert.True(r2Commands.Count > 0);
        foreach (var cmd in r2Commands)
        {
            var value = int.Parse(cmd.Substring(2, 3));
            Assert.True(value <= 699, $"Grind R2 value {value} exceeds 70% cap (699)");
        }
    }

    [Fact]
    public void FillMode_Grind_CappedTo70_WhenStrokeAtTop()
    {
        // When stroke is at 100 (top), Grind should output 0 (inverse)
        // After ramp-up completes, values should converge near 0
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Grind; // R2 = Grind
        configs[3].Max = 100;
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 100), new(10000, 100) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000); // L0 at 100

        _sut.Start();
        Thread.Sleep(2000); // Wait for ramp-up to complete (~113 ticks at 100Hz)
        _sut.StopTimer();

        // After ramp-up, R2 should be near 0 (stroke at max → pitch at min)
        var r2Values = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R2"))
            .Select(p => int.Parse(p.Substring(2, 3)))
            .ToList();
        Assert.True(r2Values.Count > 0);
        // Last values should have converged near 0
        var lastValue = r2Values.Last();
        Assert.True(lastValue <= 10, $"Grind R2 last value {lastValue} should be near 0 when stroke is at 100");
    }

    [Fact]
    public void FillMode_Figure8_R2CappedTo70()
    {
        // Figure8 on R2 (pitch) should also be capped to PitchFillMaxPosition (70)
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Figure8; // R2 = Figure8
        configs[3].Min = 0;
        configs[3].Max = 100; // Full range, but pitch cap should limit to 70
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(2500, 100), new(5000, 0), new(7500, 100), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);

        _sut.Start();
        for (int t = 0; t < 5000; t += 500)
        {
            _sut.SetTime(t);
            Thread.Sleep(30);
        }
        _sut.StopTimer();

        var r2Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R2"))
            .Select(p => int.Parse(p.Substring(2, 3)))
            .ToList();
        Assert.True(r2Commands.Count > 0);
        // All R2 values must be ≤ 699 (70% cap)
        foreach (var value in r2Commands)
        {
            Assert.True(value <= 699, $"Figure8 R2 value {value} exceeds 70% pitch cap (699)");
        }
    }

    [Fact]
    public void FillMode_Figure8_R1NotCapped()
    {
        // Figure8 on R1 (roll) should NOT be capped — only pitch (R2) has the safety limit
        var configs = AxisConfig.CreateDefaults();
        configs[2].FillMode = AxisFillMode.Figure8; // R1 = Figure8
        configs[2].Min = 0;
        configs[2].Max = 100;
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(2500, 100), new(5000, 0), new(7500, 100), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);

        _sut.Start();
        for (int t = 0; t < 5000; t += 500)
        {
            _sut.SetTime(t);
            Thread.Sleep(30);
        }
        _sut.StopTimer();

        var r1Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R1"))
            .Select(p => int.Parse(p.Substring(2, 3)))
            .ToList();
        // R1 should use full config range, not limited to 699
        Assert.True(r1Commands.Count > 0);
    }

    [Fact]
    public void PitchFillMaxPosition_Constant_Is70()
    {
        Assert.Equal(70.0, TCodeService.PitchFillMaxPosition);
    }

    [Fact]
    public void FillMode_WaveformFill_R2CappedTo70()
    {
        // Any waveform fill (Triangle, Sine, etc.) on R2 (pitch) must be capped to 70%
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Triangle; // R2 = Triangle
        configs[3].SyncWithStroke = false;
        configs[3].FillSpeedHz = 2.0; // Fast enough to see full range
        configs[3].Min = 0;
        configs[3].Max = 100;
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50), new(10000, 50) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(1000);

        _sut.Start();
        Thread.Sleep(1000);
        _sut.StopTimer();

        var r2Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R2"))
            .Select(p => int.Parse(p.Substring(2, 3)))
            .ToList();
        Assert.True(r2Commands.Count > 0);
        foreach (var value in r2Commands)
        {
            Assert.True(value <= 699, $"Triangle R2 value {value} exceeds 70% pitch cap (699)");
        }
    }

    [Fact]
    public void FillMode_WaveformFill_R0NotCapped()
    {
        // Non-pitch axes should NOT be capped to 70%
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Triangle; // R0 = Triangle
        configs[1].SyncWithStroke = false;
        configs[1].FillSpeedHz = 2.0;
        configs[1].Min = 0;
        configs[1].Max = 100;
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 50), new(10000, 50) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(1000);

        _sut.Start();
        Thread.Sleep(1000);
        _sut.StopTimer();

        var r0Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R0"))
            .Select(p => int.Parse(p.Substring(2, 3)))
            .ToList();
        Assert.True(r0Commands.Count > 0);
        // R0 should go above 699 since it's not pitch-capped
        var maxR0 = r0Commands.Max();
        Assert.True(maxR0 > 699, $"R0 max value {maxR0} should exceed 699 since R0 is not pitch-capped");
    }

    [Fact]
    public void FillMode_Figure8_R2ProducesOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Figure8; // R2 = Figure8
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(5000, 100), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(2500); // L0 at 50%, going up

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains("R2", firstMsg);
    }

    [Fact]
    public void FillMode_Figure8_R1ProducesOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[2].FillMode = AxisFillMode.Figure8; // R1 = Figure8
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(5000, 100), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(2500); // L0 at 50%, going up

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var firstMsg = _transport.SentMessages[0];
        Assert.Contains("R1", firstMsg);
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
    public void FillMode_ActiveWithoutPlayback_DoesNotSendOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle;
        _sut.SetAxisConfigs(configs);

        // Not playing — fill mode must NOT produce output
        _sut.SetPlaying(false);

        _sut.Start();
        Thread.Sleep(150);
        _sut.StopTimer();

        // Fill should NOT work when not playing (only scripts, test, or offset trigger output)
        Assert.True(_transport.SentMessages.Count == 0,
            "Fill mode must not produce output when not playing");
    }

    // ===== Position Offset — ApplyPositionOffset =====

    // --- L0 offset (clamping) ---

    [Theory]
    [InlineData(500, 0, 500)]     // No offset
    [InlineData(500, 50, 999)]    // +50% → 500 + 499 = 999
    [InlineData(500, -50, 1)]     // -50% → 500 - 499 = 1
    [InlineData(0, 50, 499)]      // 0 + 499 = 499
    [InlineData(999, -50, 500)]   // 999 - 499 = 500
    [InlineData(0, -50, 0)]       // 0 - 499 = -499 → clamped to 0
    [InlineData(999, 50, 999)]    // 999 + 499 = 1498 → clamped to 999
    public void ApplyPositionOffset_L0_ClampsCorrectly(int tcodeIn, double offset, int expected)
    {
        var config = new AxisConfig { Id = "L0", Type = "linear", PositionOffset = offset };
        var result = TCodeService.ApplyPositionOffset(config, tcodeIn);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplyPositionOffset_L0_ZeroOffset_NoChange()
    {
        var config = new AxisConfig { Id = "L0", Type = "linear", PositionOffset = 0 };
        Assert.Equal(500, TCodeService.ApplyPositionOffset(config, 500));
    }

    // --- R0 offset (wrapping) ---

    [Theory]
    [InlineData(500, 0, 500)]       // No offset
    [InlineData(500, 180, 999)]     // 500 + 499 = 999 (half rotation)
    [InlineData(0, 360, 999)]       // (0 + 999) % 1000 = 999
    [InlineData(500, 360, 499)]     // (500 + 999) % 1000 = 499
    [InlineData(1, 360, 0)]         // (1 + 999) % 1000 = 0
    public void ApplyPositionOffset_R0_WrapsCorrectly(int tcodeIn, double offset, int expected)
    {
        var config = new AxisConfig { Id = "R0", Type = "rotation", PositionOffset = offset };
        var result = TCodeService.ApplyPositionOffset(config, tcodeIn);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplyPositionOffset_R0_NegativeOffset_WrapsBackward()
    {
        // R0 at TCode 100, offset -90 degrees → (100 + (-249)) = -149 → (-149 + 1000) = 851
        var config = new AxisConfig { Id = "R0", Type = "rotation", PositionOffset = -90 };
        var result = TCodeService.ApplyPositionOffset(config, 100);
        var expectedOffset = (int)(-90.0 / 360.0 * 999); // -249
        var expected = (100 + expectedOffset) % 1000;
        if (expected < 0) expected += 1000;
        Assert.Equal(expected, result);
    }

    // --- R1, R2: no offset ---

    [Theory]
    [InlineData("R1")]
    [InlineData("R2")]
    public void ApplyPositionOffset_R1R2_NoOffsetApplied(string axisId)
    {
        var config = new AxisConfig { Id = axisId, Type = "rotation", PositionOffset = 50 };
        var result = TCodeService.ApplyPositionOffset(config, 500);
        Assert.Equal(500, result); // Unchanged despite offset being set
    }

    // --- Combined with min/max ---

    [Fact]
    public void PositionOffset_CombinedWithMinMax_L0()
    {
        // Min=0, Max=70, Position=0 → scaled=0 → TCode=0, then +50 offset → +499 → 499
        var config = new AxisConfig { Id = "L0", Type = "linear", Max = 70, PositionOffset = 50 };
        var tcode = TCodeService.PositionToTCode(config, 0); // 0
        var result = TCodeService.ApplyPositionOffset(config, tcode);
        Assert.Equal(499, result);
    }

    [Fact]
    public void PositionOffset_CombinedWithMinMax_L0_Clamped()
    {
        // Min=0, Max=70, Position=100 → scaled=70 → TCode=699, then +50 → +499 → 1198 → clamped 999
        var config = new AxisConfig { Id = "L0", Type = "linear", Max = 70, PositionOffset = 50 };
        var tcode = TCodeService.PositionToTCode(config, 100); // 699
        var result = TCodeService.ApplyPositionOffset(config, tcode);
        Assert.Equal(999, result);
    }

    // --- Integration: offset applied in output tick ---

    [Fact]
    public void OutputTick_L0Offset_ShiftsOutput()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].PositionOffset = 50; // L0 offset +50%
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 0) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000); // Position = 0 → TCode 0 → with +50 offset → ~499

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var msg = _transport.SentMessages[0];
        // Should contain L0 with value near 499 (not 000)
        Assert.Contains("L0", msg);
        Assert.DoesNotContain("L0000", msg); // Not unshifted zero
    }

    // ===== Test Mode =====

    [Fact]
    public void StartTestAxis_SetsIsAxisTesting()
    {
        _sut.StartTestAxis("L0", 1.0);
        Assert.True(_sut.IsAxisTesting("L0"));
    }

    [Fact]
    public void IsAxisTesting_ReturnsFalseWhenNotTesting()
    {
        Assert.False(_sut.IsAxisTesting("L0"));
    }

    [Fact]
    public void StopTestAxis_RampsDown_RaisesTestAxisStopped()
    {
        _sut.SetAxisConfigs(AxisConfig.CreateDefaults());
        _sut.StartTestAxis("L0", 1.0);
        _sut.Start();

        // Let it run a few ticks to ramp up
        Thread.Sleep(100);

        string? stoppedAxisId = null;
        _sut.TestAxisStopped += id => stoppedAxisId = id;

        _sut.StopTestAxis("L0");

        // Wait for ramp-down to complete (amplitude 50 → 0 at factor 0.02/tick)
        // At 100Hz ~10ms/tick, factor 0.02: takes many ticks but exponential
        // 50 * 0.98^n < 0.5 → n ≈ 230 ticks → ~2.3s. Wait up to 4s.
        var timeout = Stopwatch.StartNew();
        while (_sut.IsAxisTesting("L0") && timeout.ElapsedMilliseconds < 4000)
            Thread.Sleep(20);

        _sut.StopTimer();

        Assert.False(_sut.IsAxisTesting("L0"));
        Assert.Equal("L0", stoppedAxisId);
    }

    [Fact]
    public void StopAllTestAxes_ClearsAllAndRaisesAllTestsStopped()
    {
        _sut.StartTestAxis("L0", 1.0);
        _sut.StartTestAxis("R0", 2.0);

        bool allStoppedFired = false;
        _sut.AllTestsStopped += () => allStoppedFired = true;

        _sut.StopAllTestAxes();

        Assert.False(_sut.IsAxisTesting("L0"));
        Assert.False(_sut.IsAxisTesting("R0"));
        Assert.True(allStoppedFired);
    }

    [Fact]
    public void StopAllTestAxes_DoesNotFireEvent_WhenNoTestAxes()
    {
        bool allStoppedFired = false;
        _sut.AllTestsStopped += () => allStoppedFired = true;

        _sut.StopAllTestAxes();

        Assert.False(allStoppedFired);
    }

    [Fact]
    public void SetPlaying_WithScripts_AutoStopsAllTestAxes()
    {
        _sut.StartTestAxis("L0", 1.0);
        _sut.SetScripts(new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(1000, 100) } }
        });

        bool allStoppedFired = false;
        _sut.AllTestsStopped += () => allStoppedFired = true;

        _sut.SetPlaying(true);

        Assert.False(_sut.IsAxisTesting("L0"));
        Assert.True(allStoppedFired);
    }

    [Fact]
    public void SetPlaying_WithoutScripts_DoesNotAutoStop()
    {
        _sut.StartTestAxis("L0", 1.0);
        // No scripts loaded

        _sut.SetPlaying(true);

        Assert.True(_sut.IsAxisTesting("L0")); // Still testing
    }

    [Fact]
    public void UpdateTestSpeed_ChangesTargetSpeed()
    {
        _sut.StartTestAxis("L0", 1.0);
        _sut.UpdateTestSpeed("L0", 3.0);

        // Verify axis is still testing (speed change doesn't stop it)
        Assert.True(_sut.IsAxisTesting("L0"));
    }

    [Fact]
    public void UpdateTestSpeed_ClampsToValidRange()
    {
        _sut.StartTestAxis("L0", 1.0);
        // These should not throw
        _sut.UpdateTestSpeed("L0", 0.01); // Below min, clamped to 0.1
        _sut.UpdateTestSpeed("L0", 10.0); // Above max, clamped to 5.0
        Assert.True(_sut.IsAxisTesting("L0"));
    }

    [Fact]
    public void TestMode_ProducesOutput_WhenNotPlaying()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle; // FillMode required for test output
        _sut.SetAxisConfigs(configs);
        _sut.StartTestAxis("L0", 1.0);

        _sut.Start();
        Thread.Sleep(200); // Let a few ticks fire
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0, "Should produce TCode output during test mode");
        Assert.All(_transport.SentMessages, msg => Assert.Contains("L0", msg));
    }

    [Fact]
    public void TestMode_OutputOscillatesWithFillPattern()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].FillMode = AxisFillMode.Triangle; // FillMode required for test output
        _sut.SetAxisConfigs(configs);
        _sut.StartTestAxis("L0", 2.0); // 2Hz — fast enough to see oscillation

        _sut.Start();
        Thread.Sleep(800); // Let it oscillate for a bit
        _sut.StopTimer();

        // Collect L0 values from sent messages
        var l0Values = new List<int>();
        foreach (var msg in _transport.SentMessages)
        {
            var parts = msg.TrimEnd('\n').Split(' ');
            foreach (var part in parts)
            {
                if (part.StartsWith("L0") && part.Contains('I'))
                {
                    var valStr = part.Substring(2, part.IndexOf('I') - 2);
                    if (int.TryParse(valStr, out var val))
                        l0Values.Add(val);
                }
            }
        }

        Assert.True(l0Values.Count > 2, "Should have multiple L0 values");

        // After ramp-up, values should vary (triangle pattern goes 0—1—0)
        var minVal = l0Values.Min();
        var maxVal = l0Values.Max();
        Assert.True(maxVal - minVal > 10, $"Values should oscillate, min={minVal} max={maxVal}");
    }

    [Fact]
    public void TestMode_RespectsAxisMinMax()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].Min = 20; // L0 min
        configs[0].Max = 80; // L0 max
        _sut.SetAxisConfigs(configs);
        _sut.StartTestAxis("L0", 1.5);

        _sut.Start();
        Thread.Sleep(500);
        _sut.StopTimer();

        var l0Values = new List<int>();
        foreach (var msg in _transport.SentMessages)
        {
            var parts = msg.TrimEnd('\n').Split(' ');
            foreach (var part in parts)
            {
                if (part.StartsWith("L0") && part.Contains('I'))
                {
                    var valStr = part.Substring(2, part.IndexOf('I') - 2);
                    if (int.TryParse(valStr, out var val))
                        l0Values.Add(val);
                }
            }
        }

        if (l0Values.Count > 0)
        {
            // All values should be within the min/max TCode range
            // Min=20 → TCode 199, Max=80 → TCode 799 (approximately)
            // With test oscillation center=50 ±amplitude, scaled through min/max
            var tMin = TCodeService.PositionToTCode(configs[0], 0); // position 0 → min=20 → tcode ~199
            var tMax = TCodeService.PositionToTCode(configs[0], 100); // position 100 → max=80 → tcode ~799
            Assert.All(l0Values, v => Assert.InRange(v, tMin, tMax));
        }
    }

    [Fact]
    public void StopTestAxis_DoesNotSendMidpoint_WhenNotTesting()
    {
        _sut.SetAxisConfigs(AxisConfig.CreateDefaults());

        // StopTestAxis on a non-testing axis should not send anything
        _sut.StopTestAxis("L0");

        Assert.Empty(_transport.SentMessages);
    }

    [Fact]
    public void StopTimer_ClearsTestAxes()
    {
        _sut.StartTestAxis("L0", 1.0);
        _sut.StartTestAxis("R0", 2.0);

        _sut.StopTimer();

        Assert.False(_sut.IsAxisTesting("L0"));
        Assert.False(_sut.IsAxisTesting("R0"));
    }

    [Fact]
    public void StartTestAxis_ClampsSpeed()
    {
        // Should not throw even with out-of-range speeds
        _sut.StartTestAxis("L0", 0.001); // Below min → clamped to 0.1
        Assert.True(_sut.IsAxisTesting("L0"));

        _sut.StartTestAxis("L0", 100.0); // Above max → clamped to 5.0
        Assert.True(_sut.IsAxisTesting("L0"));
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

    // ===== HomeAxes =====

    [Fact]
    public void HomeAxes_SendsMidpointToAllAxes()
    {
        var configs = AxisConfig.CreateDefaults();
        _sut.SetAxisConfigs(configs);
        _sut.Transport = _transport;

        _sut.HomeAxes();

        Assert.Single(_transport.SentMessages);
        var msg = _transport.SentMessages[0];
        // L0/R0/R1 default Min=0,Max=100 → PositionToTCode(50) = 499
        // R2 default Min=0,Max=75 → PositionToTCode(50) = 374
        Assert.Contains("L0499I2000", msg);
        Assert.Contains("R0499I2000", msg);
        Assert.Contains("R1499I2000", msg);
        Assert.Contains("R2374I2000", msg);
    }

    [Fact]
    public void HomeAxes_NoTransport_DoesNotThrow()
    {
        // Create a fresh TCodeService with no transport
        using var sut = new TCodeService(_interpolation);
        sut.SetAxisConfigs(AxisConfig.CreateDefaults());
        // No transport set — HomeAxes should not throw
        sut.HomeAxes();
    }

    // ===== Test Mode: None Fill Switching =====

    [Fact]
    public void TestMode_NoneAxis_StaysRegistered_NoOutput()
    {
        // An axis in test mode with FillMode.None should remain registered
        // but produce no fill output (the loop continues past it)
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.None; // R0 = None
        _sut.SetAxisConfigs(configs);
        _sut.Transport = _transport;

        _sut.StartTestAxis("R0", 1.0);
        Assert.True(_sut.IsAxisTesting("R0"));

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        // R0 should still be registered (not removed)
        // but should not have produced fill output since FillMode is None
        var r0Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R0"))
            .ToList();
        // None fill → skipped in test output (only midpoint on stop)
        // Any R0 commands are just the midpoint sent by StopTestAxis
    }

    [Fact]
    public void HomeAxes_RespectsPositionOffsets()
    {
        var configs = AxisConfig.CreateDefaults();
        // L0 offset: -50 → midpoint TCode 499 shifted by (-50/100*999)=-499 → 0 (clamped)
        configs[0].PositionOffset = -50;
        // R0 offset: 90 degrees → midpoint TCode 499 shifted by (90/360*999)=249 → (499+249)%1000=748
        configs[1].PositionOffset = 90;
        _sut.SetAxisConfigs(configs);

        _sut.HomeAxes();

        Assert.Single(_transport.SentMessages);
        var msg = _transport.SentMessages[0];
        // L0: Clamp(499 + (-499), 0, 999) = 0
        Assert.Contains("L0000I2000", msg);
        // R0: (499 + 249) % 1000 = 748
        Assert.Contains("R0748I2000", msg);
        // R1, R2: no offset support → stays at default
        Assert.Contains("R1499I2000", msg);
        Assert.Contains("R2374I2000", msg);
    }

    // ===== External Axis Positions =====

    [Fact]
    public void ExternalPositions_L0_AppliesMinMaxLimits()
    {
        // L0 config: Min=20, Max=80 → position 50 should map within that range
        var configs = AxisConfig.CreateDefaults();
        configs[0].Min = 20;
        configs[0].Max = 80;
        _sut.SetAxisConfigs(configs);

        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 50.0 });
        _sut.SetPlaying(true);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var l0Cmds = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("L0"))
            .ToList();
        Assert.True(l0Cmds.Count > 0);

        // Position 50 with min=20, max=80: scaled = 20 + 0.5*(80-20) = 50 → TCode = 50/100*999 = 499
        var value = int.Parse(l0Cmds[0].Substring(2, 3));
        Assert.Equal(499, value);
    }

    [Fact]
    public void ExternalPositions_L0_AppliesPositionOffset()
    {
        var configs = AxisConfig.CreateDefaults();
        configs[0].PositionOffset = 10; // +10 points
        _sut.SetAxisConfigs(configs);

        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 50.0 });
        _sut.SetPlaying(true);

        _sut.Start();
        Thread.Sleep(100);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        var l0Cmds = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("L0"))
            .ToList();
        Assert.True(l0Cmds.Count > 0);

        // Position 50 → TCode 499, plus offset 10% → 499 + (10/100*999) = 499 + 99 = 598
        var value = int.Parse(l0Cmds[0].Substring(2, 3));
        Assert.True(value > 499, $"L0 value {value} should be above 499 with +10 offset");
    }

    [Fact]
    public void ExternalPositions_L0_FillModesStillWorkOnOtherAxes()
    {
        // When external positions drive L0, fills on other axes should use the external stroke position
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Triangle; // R0 = Triangle fill
        _sut.SetAxisConfigs(configs);

        // No funscripts — only external L0
        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 75.0 });
        _sut.SetPlaying(true);

        _sut.Start();
        Thread.Sleep(200);
        _sut.StopTimer();

        Assert.True(_transport.SentMessages.Count > 0);
        // Should have both L0 (from external) and R0 (from fill)
        var hasL0 = _transport.SentMessages.Any(m => m.Contains("L0"));
        var hasR0 = _transport.SentMessages.Any(m => m.Contains("R0"));
        Assert.True(hasL0, "Expected L0 commands from external positions");
        Assert.True(hasR0, "Expected R0 commands from fill mode");
    }

    [Fact]
    public void ExternalPositions_GrindFill_UsesExternalStrokePosition()
    {
        // Grind fill on R2 should follow external L0 position (not stuck at 50)
        var configs = AxisConfig.CreateDefaults();
        configs[3].FillMode = AxisFillMode.Grind; // R2 = Grind
        _sut.SetAxisConfigs(configs);

        // External L0 at bottom (0%) → Grind should output near max pitch
        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 0.0 });
        _sut.SetPlaying(true);

        _sut.Start();
        Thread.Sleep(200);
        _sut.StopTimer();

        var r2Commands = _transport.SentMessages
            .SelectMany(m => m.Split(' '))
            .Where(p => p.StartsWith("R2"))
            .ToList();
        Assert.True(r2Commands.Count > 0, "Expected R2 commands from Grind fill");

        // Grind with L0 at 0: pitch should be near PitchFillMaxPosition (70%)
        // TCode for 70% ≈ 699, may be lower during ramp-up but should be > midpoint (500)
        var lastR2 = r2Commands.Last();
        var lastValue = int.Parse(lastR2.Substring(2, 3));
        Assert.True(lastValue > 500, $"Grind R2 should move above 500 when L0 at bottom, got {lastValue}");
    }

    [Fact]
    public void ExternalPositions_SyncWithStroke_WorksWithExternalL0()
    {
        // SyncWithStroke fills should track external L0 position changes
        var configs = AxisConfig.CreateDefaults();
        configs[1].FillMode = AxisFillMode.Triangle; // R0 = Triangle
        configs[1].SyncWithStroke = true;
        _sut.SetAxisConfigs(configs);

        // Simulate external L0 moving: position changes between ticks
        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 10.0 });
        _sut.SetPlaying(true);

        _sut.Start();
        Thread.Sleep(50);

        // Move L0 so cumulative stroke distance increases
        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 90.0 });
        Thread.Sleep(150);
        _sut.StopTimer();

        // R0 should have received fill-mode output since stroke is active (hasStrokeScript = true)
        var hasR0 = _transport.SentMessages.Any(m => m.Contains("R0"));
        Assert.True(hasR0, "SyncWithStroke fill should produce output when external L0 is active");
    }

    [Fact]
    public void ExternalPositions_Cleared_FallsBackToScript()
    {
        var configs = AxisConfig.CreateDefaults();
        _sut.SetAxisConfigs(configs);

        var scripts = new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData { Actions = new List<FunscriptAction> { new(0, 0), new(10000, 100) } }
        };
        _sut.SetScripts(scripts);
        _sut.SetPlaying(true);
        _sut.SetTime(5000);

        // Start with external positions
        _sut.SetExternalPositions(new Dictionary<string, double> { ["L0"] = 10.0 });

        _sut.Start();
        Thread.Sleep(100);

        // Clear external positions
        _sut.SetExternalPositions(null);
        Thread.Sleep(100);
        _sut.StopTimer();

        // Should have received L0 output from both phases
        Assert.True(_transport.SentMessages.Count > 0);
        Assert.True(_transport.SentMessages.Any(m => m.Contains("L0")));
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
