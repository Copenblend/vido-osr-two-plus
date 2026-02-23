using System.ComponentModel;
using Moq;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Plugin;
using Xunit;

namespace Osr2PlusPlugin.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="VisualizerViewModel"/> — visualization mode,
/// window duration, time tracking, loaded axes, and settings persistence.
/// </summary>
public class VisualizerViewModelTests
{
    private readonly Mock<IPluginSettingsStore> _mockSettings;
    private readonly VisualizerViewModel _sut;

    public VisualizerViewModelTests()
    {
        _mockSettings = new Mock<IPluginSettingsStore>();
        _mockSettings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        _mockSettings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        _sut = new VisualizerViewModel(_mockSettings.Object);
    }

    // ═══════════════════════════════════════════════════════
    //  Default Values
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_DefaultSelectedMode_IsGraph()
    {
        Assert.Equal(VisualizationMode.Graph, _sut.SelectedMode);
    }

    [Fact]
    public void Constructor_DefaultWindowDurationSeconds_Is60()
    {
        Assert.Equal(60, _sut.WindowDurationSeconds);
    }

    [Fact]
    public void Constructor_DefaultCurrentTime_IsZero()
    {
        Assert.Equal(0.0, _sut.CurrentTime);
    }

    [Fact]
    public void Constructor_DefaultLoadedAxes_IsEmpty()
    {
        Assert.Empty(_sut.LoadedAxes);
    }

    [Fact]
    public void Constructor_DefaultHasScripts_IsFalse()
    {
        Assert.False(_sut.HasScripts);
    }

    [Fact]
    public void Constructor_DefaultTimeWindowRadius_Is30()
    {
        Assert.Equal(30.0, _sut.TimeWindowRadius);
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence — Loading
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_LoadsPersistedModeSetting()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Heatmap");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(VisualizationMode.Heatmap, vm.SelectedMode);
    }

    [Fact]
    public void Constructor_LoadsPersistedWindowDuration()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("120");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(120, vm.WindowDurationSeconds);
    }

    [Fact]
    public void Constructor_InvalidModeSetting_DefaultsToGraph()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("InvalidMode");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(VisualizationMode.Graph, vm.SelectedMode);
    }

    [Fact]
    public void Constructor_InvalidWindowDuration_DefaultsTo60()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("abc");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(60, vm.WindowDurationSeconds);
    }

    [Fact]
    public void Constructor_UnsupportedWindowDuration_DefaultsTo60()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("45");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(60, vm.WindowDurationSeconds);
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence — Saving
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SelectedMode_Setter_PersistsToSettings()
    {
        _sut.SelectedMode = VisualizationMode.Heatmap;

        _mockSettings.Verify(s => s.Set("visualizerMode", "Heatmap"), Times.Once);
    }

    [Fact]
    public void WindowDurationSeconds_Setter_PersistsToSettings()
    {
        _sut.WindowDurationSeconds = 300;

        _mockSettings.Verify(s => s.Set("visualizerWindowDuration", "300"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════
    //  SelectedMode Property
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SelectedMode_SetToHeatmap_RaisesPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualizerViewModel.SelectedMode))
                raised = true;
        };

        _sut.SelectedMode = VisualizationMode.Heatmap;

        Assert.True(raised);
    }

    [Fact]
    public void SelectedMode_SetToSameValue_DoesNotRaisePropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualizerViewModel.SelectedMode))
                raised = true;
        };

        _sut.SelectedMode = VisualizationMode.Graph; // same as default

        Assert.False(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  WindowDurationSeconds Property
    // ═══════════════════════════════════════════════════════

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(300)]
    public void WindowDurationSeconds_ValidValues_UpdatesProperty(int duration)
    {
        _sut.WindowDurationSeconds = duration;

        Assert.Equal(duration, _sut.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationSeconds_Changed_RaisesPropertyChanged()
    {
        var raised = new List<string>();
        _sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.WindowDurationSeconds = 120;

        Assert.Contains(nameof(VisualizerViewModel.WindowDurationSeconds), raised);
    }

    [Fact]
    public void WindowDurationSeconds_Changed_RaisesTimeWindowRadiusChanged()
    {
        var raised = new List<string>();
        _sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.WindowDurationSeconds = 120;

        Assert.Contains(nameof(VisualizerViewModel.TimeWindowRadius), raised);
    }

    [Fact]
    public void WindowDurationSeconds_Changed_RaisesWindowDurationIndexChanged()
    {
        var raised = new List<string>();
        _sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.WindowDurationSeconds = 120;

        Assert.Contains(nameof(VisualizerViewModel.WindowDurationIndex), raised);
    }

    // ═══════════════════════════════════════════════════════
    //  WindowDurationIndex Property
    // ═══════════════════════════════════════════════════════

    [Theory]
    [InlineData(30, 0)]
    [InlineData(60, 1)]
    [InlineData(120, 2)]
    [InlineData(300, 3)]
    public void WindowDurationIndex_ReflectsWindowDurationSeconds(int duration, int expectedIndex)
    {
        _sut.WindowDurationSeconds = duration;

        Assert.Equal(expectedIndex, _sut.WindowDurationIndex);
    }

    [Fact]
    public void WindowDurationIndex_DefaultIs1()
    {
        // Default WindowDurationSeconds is 60 → index 1
        Assert.Equal(1, _sut.WindowDurationIndex);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 300)]
    public void WindowDurationIndex_Setter_UpdatesWindowDurationSeconds(int index, int expectedDuration)
    {
        _sut.WindowDurationIndex = index;

        Assert.Equal(expectedDuration, _sut.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationIndex_Setter_PersistsToSettings()
    {
        _sut.WindowDurationIndex = 2; // 120s

        _mockSettings.Verify(s => s.Set("visualizerWindowDuration", "120"), Times.Once);
    }

    [Fact]
    public void WindowDurationIndex_NegativeValue_IsIgnored()
    {
        _sut.WindowDurationSeconds = 120;

        _sut.WindowDurationIndex = -1;

        Assert.Equal(120, _sut.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationIndex_OutOfRange_IsIgnored()
    {
        _sut.WindowDurationSeconds = 120;

        _sut.WindowDurationIndex = 99;

        Assert.Equal(120, _sut.WindowDurationSeconds);
    }

    [Fact]
    public void WindowDurationIndex_RoundTrip_AllValues()
    {
        for (int i = 0; i < VisualizerViewModel.AvailableWindowDurations.Length; i++)
        {
            _sut.WindowDurationIndex = i;
            Assert.Equal(i, _sut.WindowDurationIndex);
            Assert.Equal(VisualizerViewModel.AvailableWindowDurations[i], _sut.WindowDurationSeconds);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  TimeWindowRadius
    // ═══════════════════════════════════════════════════════

    [Theory]
    [InlineData(30, 15.0)]
    [InlineData(60, 30.0)]
    [InlineData(120, 60.0)]
    [InlineData(300, 150.0)]
    public void TimeWindowRadius_IsHalfOfWindowDuration(int duration, double expected)
    {
        _sut.WindowDurationSeconds = duration;

        Assert.Equal(expected, _sut.TimeWindowRadius);
    }

    // ═══════════════════════════════════════════════════════
    //  CurrentTime Property
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void CurrentTime_SetValue_UpdatesProperty()
    {
        _sut.CurrentTime = 42.5;

        Assert.Equal(42.5, _sut.CurrentTime);
    }

    [Fact]
    public void CurrentTime_Changed_RaisesPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualizerViewModel.CurrentTime))
                raised = true;
        };

        _sut.CurrentTime = 10.0;

        Assert.True(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  UpdateTime Method
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void UpdateTime_SetsCurrentTime()
    {
        _sut.UpdateTime(55.3);

        Assert.Equal(55.3, _sut.CurrentTime);
    }

    [Fact]
    public void UpdateTime_RaisesRepaintRequested()
    {
        var repainted = false;
        _sut.RepaintRequested += () => repainted = true;

        _sut.UpdateTime(1.0);

        Assert.True(repainted);
    }

    // ═══════════════════════════════════════════════════════
    //  LoadedAxes / SetLoadedAxes / HasScripts
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SetLoadedAxes_WithData_UpdatesLoadedAxes()
    {
        var axes = CreateTestAxes();

        _sut.SetLoadedAxes(axes);

        Assert.Same(axes, _sut.LoadedAxes);
    }

    [Fact]
    public void SetLoadedAxes_WithData_HasScriptsIsTrue()
    {
        _sut.SetLoadedAxes(CreateTestAxes());

        Assert.True(_sut.HasScripts);
    }

    [Fact]
    public void SetLoadedAxes_RaisesPropertyChanged_ForLoadedAxes()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualizerViewModel.LoadedAxes))
                raised = true;
        };

        _sut.SetLoadedAxes(CreateTestAxes());

        Assert.True(raised);
    }

    [Fact]
    public void SetLoadedAxes_RaisesPropertyChanged_ForHasScripts()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VisualizerViewModel.HasScripts))
                raised = true;
        };

        _sut.SetLoadedAxes(CreateTestAxes());

        Assert.True(raised);
    }

    [Fact]
    public void SetLoadedAxes_RaisesRepaintRequested()
    {
        var repainted = false;
        _sut.RepaintRequested += () => repainted = true;

        _sut.SetLoadedAxes(CreateTestAxes());

        Assert.True(repainted);
    }

    [Fact]
    public void SetLoadedAxes_NullInput_SetsEmptyDictionary()
    {
        _sut.SetLoadedAxes(CreateTestAxes());
        _sut.SetLoadedAxes(null!);

        Assert.Empty(_sut.LoadedAxes);
        Assert.False(_sut.HasScripts);
    }

    // ═══════════════════════════════════════════════════════
    //  ClearAxes
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ClearAxes_EmptiesLoadedAxes()
    {
        _sut.SetLoadedAxes(CreateTestAxes());

        _sut.ClearAxes();

        Assert.Empty(_sut.LoadedAxes);
    }

    [Fact]
    public void ClearAxes_HasScriptsIsFalse()
    {
        _sut.SetLoadedAxes(CreateTestAxes());

        _sut.ClearAxes();

        Assert.False(_sut.HasScripts);
    }

    [Fact]
    public void ClearAxes_RaisesRepaintRequested()
    {
        _sut.SetLoadedAxes(CreateTestAxes());
        var repainted = false;
        _sut.RepaintRequested += () => repainted = true;

        _sut.ClearAxes();

        Assert.True(repainted);
    }

    // ═══════════════════════════════════════════════════════
    //  Static Dictionaries
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void AxisColors_ContainsAllFourAxes()
    {
        Assert.Equal(4, VisualizerViewModel.AxisColors.Count);
        Assert.Equal("#007ACC", VisualizerViewModel.AxisColors["L0"]);
        Assert.Equal("#B800CC", VisualizerViewModel.AxisColors["R0"]);
        Assert.Equal("#CC5200", VisualizerViewModel.AxisColors["R1"]);
        Assert.Equal("#14CC00", VisualizerViewModel.AxisColors["R2"]);
    }

    [Fact]
    public void AxisNames_ContainsAllFourAxes()
    {
        Assert.Equal(4, VisualizerViewModel.AxisNames.Count);
        Assert.Equal("Stroke", VisualizerViewModel.AxisNames["L0"]);
        Assert.Equal("Twist", VisualizerViewModel.AxisNames["R0"]);
        Assert.Equal("Roll", VisualizerViewModel.AxisNames["R1"]);
        Assert.Equal("Pitch", VisualizerViewModel.AxisNames["R2"]);
    }

    [Fact]
    public void AvailableWindowDurations_HasCorrectValues()
    {
        Assert.Equal(new[] { 30, 60, 120, 300 }, VisualizerViewModel.AvailableWindowDurations);
    }

    [Fact]
    public void WindowDurationLabels_HasCorrectValues()
    {
        Assert.Equal(new[] { "30s", "1 min", "2 min", "5 min" }, VisualizerViewModel.WindowDurationLabels);
    }

    [Fact]
    public void AvailableWindowDurations_MatchLabelsCount()
    {
        Assert.Equal(
            VisualizerViewModel.AvailableWindowDurations.Length,
            VisualizerViewModel.WindowDurationLabels.Length);
    }

    // ═══════════════════════════════════════════════════════
    //  RepaintRequested event
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void RepaintRequested_FiredOnUpdateTime()
    {
        int count = 0;
        _sut.RepaintRequested += () => count++;

        _sut.UpdateTime(1.0);
        _sut.UpdateTime(2.0);
        _sut.UpdateTime(3.0);

        Assert.Equal(3, count);
    }

    [Fact]
    public void RepaintRequested_FiredOnSetLoadedAxes()
    {
        int count = 0;
        _sut.RepaintRequested += () => count++;

        _sut.SetLoadedAxes(CreateTestAxes());

        Assert.Equal(1, count);
    }

    [Fact]
    public void RepaintRequested_FiredOnClearAxes()
    {
        _sut.SetLoadedAxes(CreateTestAxes());
        int count = 0;
        _sut.RepaintRequested += () => count++;

        _sut.ClearAxes();

        Assert.Equal(1, count);
    }

    // ═══════════════════════════════════════════════════════
    //  Complete Workflow
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Workflow_LoadScripts_UpdateTime_Clear()
    {
        // Initial state
        Assert.False(_sut.HasScripts);
        Assert.Equal(0.0, _sut.CurrentTime);

        // Load scripts
        _sut.SetLoadedAxes(CreateTestAxes());
        Assert.True(_sut.HasScripts);
        Assert.Equal(2, _sut.LoadedAxes.Count);

        // Simulate playback
        _sut.UpdateTime(10.5);
        Assert.Equal(10.5, _sut.CurrentTime);

        // Change visualization mode
        _sut.SelectedMode = VisualizationMode.Heatmap;
        Assert.Equal(VisualizationMode.Heatmap, _sut.SelectedMode);

        // Change window duration via index (as ComboBox would)
        _sut.WindowDurationIndex = 2; // 120s
        Assert.Equal(120, _sut.WindowDurationSeconds);
        Assert.Equal(60.0, _sut.TimeWindowRadius);

        // Video unloaded
        _sut.ClearAxes();
        Assert.False(_sut.HasScripts);
        Assert.Empty(_sut.LoadedAxes);
    }

    // ═══════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════

    private static Dictionary<string, FunscriptData> CreateTestAxes()
    {
        return new Dictionary<string, FunscriptData>
        {
            ["L0"] = new FunscriptData
            {
                AxisId = "L0",
                FilePath = "test.funscript",
                Actions = new List<FunscriptAction>
                {
                    new(0, 0),
                    new(1000, 100),
                    new(2000, 0),
                }
            },
            ["R0"] = new FunscriptData
            {
                AxisId = "R0",
                FilePath = "test.twist.funscript",
                Actions = new List<FunscriptAction>
                {
                    new(0, 50),
                    new(1000, 80),
                    new(2000, 50),
                }
            },
        };
    }
}
