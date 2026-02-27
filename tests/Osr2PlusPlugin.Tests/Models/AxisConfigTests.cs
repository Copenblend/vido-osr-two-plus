using Osr2PlusPlugin.Models;
using Xunit;

namespace Osr2PlusPlugin.Tests.Models;

public class AxisConfigTests
{
    // ── Min/Max Validation ───────────────────────────────────

    [Fact]
    public void Min_CannotBeSetGreaterOrEqualToMax()
    {
        var axis = new AxisConfig { Min = 0, Max = 50 };
        axis.Min = 60; // Should be rejected (60 >= Max of 50)
        Assert.Equal(0, axis.Min);
    }

    [Fact]
    public void Max_CannotBeSetLessOrEqualToMin()
    {
        var axis = new AxisConfig { Min = 30, Max = 100 };
        axis.Max = 20; // Should be rejected (20 <= Min of 30)
        Assert.Equal(100, axis.Max);
    }

    [Fact]
    public void Min_ClampedTo0_99()
    {
        var axis = new AxisConfig { Max = 100 };
        axis.Min = -100;
        Assert.Equal(0, axis.Min);

        axis.Min = 0;
        Assert.Equal(0, axis.Min);

        axis.Min = 99;
        Assert.Equal(99, axis.Min);
    }

    [Fact]
    public void Max_ClampedTo1_100()
    {
        var axis = new AxisConfig { Min = 0 };
        axis.Max = 200;
        Assert.Equal(100, axis.Max);

        axis.Max = 100;
        Assert.Equal(100, axis.Max);

        axis.Max = 1;
        Assert.Equal(1, axis.Max);
    }

    [Fact]
    public void Min_EqualToMax_Rejected()
    {
        var axis = new AxisConfig { Min = 0, Max = 50 };
        axis.Min = 50; // Equal to Max — rejected
        Assert.Equal(0, axis.Min);
    }

    [Fact]
    public void Max_EqualToMin_Rejected()
    {
        var axis = new AxisConfig { Min = 30, Max = 100 };
        axis.Max = 30; // Equal to Min — rejected
        Assert.Equal(100, axis.Max);
    }

    // ── FillSpeedHz Clamping ─────────────────────────────────

    [Fact]
    public void FillSpeedHz_ClampedToRange()
    {
        var axis = new AxisConfig();
        axis.FillSpeedHz = 0.01;
        Assert.Equal(0.1, axis.FillSpeedHz);

        axis.FillSpeedHz = 5.0;
        Assert.Equal(3.0, axis.FillSpeedHz);

        axis.FillSpeedHz = 2.0;
        Assert.Equal(2.0, axis.FillSpeedHz);
    }

    // ── RangeLabel ───────────────────────────────────────────

    [Fact]
    public void RangeLabel_ReflectsMinMax()
    {
        var axis = new AxisConfig { Min = 10, Max = 90 };
        Assert.Equal("10-90", axis.RangeLabel);
    }

    [Fact]
    public void RangeLabel_UpdatesWhenMinChanges()
    {
        var axis = new AxisConfig { Min = 0, Max = 100 };
        var changed = new List<string>();
        axis.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        axis.Min = 20;
        Assert.Contains("RangeLabel", changed);
    }

    [Fact]
    public void RangeLabel_UpdatesWhenMaxChanges()
    {
        var axis = new AxisConfig { Min = 0, Max = 100 };
        var changed = new List<string>();
        axis.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        axis.Max = 80;
        Assert.Contains("RangeLabel", changed);
    }

    // ── Derived Properties ───────────────────────────────────

    [Fact]
    public void Default_MinIs0_MaxIs100()
    {
        var defaults = AxisConfig.CreateDefaults();
        foreach (var config in defaults)
        {
            Assert.True(config.Min >= 0, $"{config.Id} Min should be >= 0");
            Assert.True(config.Max <= 100, $"{config.Id} Max should be <= 100");
        }
    }

    [Fact]
    public void IsStroke_TrueOnlyForL0()
    {
        Assert.True(new AxisConfig { Id = "L0" }.IsStroke);
        Assert.False(new AxisConfig { Id = "R0" }.IsStroke);
    }

    [Fact]
    public void IsPitch_TrueOnlyForR2()
    {
        Assert.True(new AxisConfig { Id = "R2" }.IsPitch);
        Assert.False(new AxisConfig { Id = "L0" }.IsPitch);
    }

    [Fact]
    public void HasPositionOffset_TrueForL0R0R1R2()
    {
        Assert.True(new AxisConfig { Id = "L0" }.HasPositionOffset);
        Assert.True(new AxisConfig { Id = "R0" }.HasPositionOffset);
        Assert.True(new AxisConfig { Id = "R1" }.HasPositionOffset);
        Assert.True(new AxisConfig { Id = "R2" }.HasPositionOffset);
        Assert.False(new AxisConfig { Id = "L1" }.HasPositionOffset);
        Assert.False(new AxisConfig { Id = "L2" }.HasPositionOffset);
    }

    // ── AvailableFillModes ───────────────────────────────────

    [Fact]
    public void AvailableFillModes_AllNonStrokeAxesHave9Modes()
    {
        foreach (var id in new[] { "R0", "R1", "R2" })
        {
            var axis = new AxisConfig { Id = id };
            Assert.Equal(9, axis.AvailableFillModes.Length);
        }
    }

    [Fact]
    public void AvailableFillModes_NonStrokeAxes_Include9CommonModes()
    {
        foreach (var id in new[] { "R0", "R1", "R2" })
        {
            var axis = new AxisConfig { Id = id };
            Assert.Contains(AxisFillMode.None, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Random, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Triangle, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Sine, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Saw, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.SawtoothReverse, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Square, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.Pulse, axis.AvailableFillModes);
            Assert.Contains(AxisFillMode.EaseInOut, axis.AvailableFillModes);
        }

        // L0 (Stroke) only has None
        var stroke = new AxisConfig { Id = "L0" };
        Assert.Single(stroke.AvailableFillModes);
        Assert.Contains(AxisFillMode.None, stroke.AvailableFillModes);
    }

    // ── ScriptFileName / HasScript ───────────────────────────

    [Fact]
    public void HasScript_FalseByDefault()
    {
        var axis = new AxisConfig();
        Assert.False(axis.HasScript);
    }

    [Fact]
    public void HasScript_TrueWhenScriptAssigned()
    {
        var axis = new AxisConfig { ScriptFileName = "test.funscript" };
        Assert.True(axis.HasScript);
    }

    [Fact]
    public void HasScript_FiresPropertyChangedWhenScriptChanges()
    {
        var axis = new AxisConfig();
        var changed = new List<string>();
        axis.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        axis.ScriptFileName = "test.funscript";
        Assert.Contains("HasScript", changed);
    }

    // ── CreateDefaults ───────────────────────────────────────

    [Fact]
    public void CreateDefaults_Returns4Axes()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal(4, axes.Count);
    }

    [Fact]
    public void CreateDefaults_CorrectIds()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal("L0", axes[0].Id);
        Assert.Equal("R0", axes[1].Id);
        Assert.Equal("R1", axes[2].Id);
        Assert.Equal("R2", axes[3].Id);
    }

    [Fact]
    public void CreateDefaults_CorrectNames()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal("Stroke", axes[0].Name);
        Assert.Equal("Twist", axes[1].Name);
        Assert.Equal("Roll", axes[2].Name);
        Assert.Equal("Pitch", axes[3].Name);
    }

    [Fact]
    public void CreateDefaults_L0IsLinear_OthersRotation()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal("linear", axes[0].Type);
        Assert.Equal("rotation", axes[1].Type);
        Assert.Equal("rotation", axes[2].Type);
        Assert.Equal("rotation", axes[3].Type);
    }

    [Fact]
    public void CreateDefaults_R2PitchMaxIs75()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal(75, axes[3].Max);
    }

    [Fact]
    public void CreateDefaults_CorrectColors()
    {
        var axes = AxisConfig.CreateDefaults();
        Assert.Equal("#007ACC", axes[0].Color);
        Assert.Equal("#B800CC", axes[1].Color);
        Assert.Equal("#CC5200", axes[2].Color);
        Assert.Equal("#14CC00", axes[3].Color);
    }

    // ── INotifyPropertyChanged ───────────────────────────────

    [Fact]
    public void PropertyChanged_NotFiredForSameValue()
    {
        var axis = new AxisConfig { Enabled = true };
        var fired = false;
        axis.PropertyChanged += (_, _) => fired = true;

        axis.Enabled = true; // Same value
        Assert.False(fired);
    }

    [Fact]
    public void PropertyChanged_FiredForNewValue()
    {
        var axis = new AxisConfig { Enabled = true };
        var fired = false;
        axis.PropertyChanged += (_, _) => fired = true;

        axis.Enabled = false;
        Assert.True(fired);
    }
}
