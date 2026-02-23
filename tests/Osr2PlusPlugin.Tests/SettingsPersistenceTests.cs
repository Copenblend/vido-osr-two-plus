using Moq;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Plugin;
using Xunit;

namespace Osr2PlusPlugin.Tests;

/// <summary>
/// Tests for VOSR-033: Settings Persistence.
/// Covers save/load round-trip, default fallbacks, PositionOffset persistence,
/// and SettingChanged event propagation for all three ViewModels.
/// </summary>
public class SettingsPersistenceTests : IDisposable
{
    private readonly InterpolationService _interpolation = new();
    private readonly TCodeService _tcode;
    private readonly FunscriptParser _parser = new();
    private readonly FunscriptMatcher _matcher = new();

    public SettingsPersistenceTests()
    {
        _tcode = new TCodeService(_interpolation);
    }

    public void Dispose() => _tcode.Dispose();

    /// <summary>
    /// Creates a mock settings store that returns defaults for all Get calls.
    /// </summary>
    private static Mock<IPluginSettingsStore> CreateDefaultSettings()
    {
        var mock = new Mock<IPluginSettingsStore>();
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string _, int d) => d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string _, bool d) => d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<double>()))
            .Returns((string _, double d) => d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string d) => d);
        return mock;
    }

    /// <summary>
    /// Creates a dictionary-backed settings store for round-trip testing.
    /// </summary>
    private static Mock<IPluginSettingsStore> CreateDictionarySettings()
    {
        var store = new Dictionary<string, object>();
        var mock = new Mock<IPluginSettingsStore>();

        mock.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
            .Callback((string key, object value) => store[key] = value);

        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string key, int d) => store.TryGetValue(key, out var v) ? (int)v : d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool d) => store.TryGetValue(key, out var v) ? (bool)v : d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<double>()))
            .Returns((string key, double d) => store.TryGetValue(key, out var v) ? (double)v : d);
        mock.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string d) => store.TryGetValue(key, out var v) ? (string)v : d);

        return mock;
    }

    // ═══════════════════════════════════════════════════════
    //  Default Fallbacks
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SidebarVm_Defaults_WhenNoPersistedSettings()
    {
        var settings = CreateDefaultSettings();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        Assert.Equal(ConnectionMode.UDP, vm.SelectedMode);
        Assert.Equal(7777, vm.UdpPort);
        Assert.Equal(115200, vm.SelectedBaudRate);
        Assert.Equal(100, vm.OutputRateHz);
        Assert.Equal(0, vm.GlobalOffsetMs);
    }

    [Fact]
    public void AxisControlVm_Defaults_WhenNoPersistedSettings()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        // L0 defaults
        Assert.Equal(0, vm.AxisCards[0].Min);
        Assert.Equal(100, vm.AxisCards[0].Max);
        Assert.True(vm.AxisCards[0].Enabled);
        Assert.Equal(AxisFillMode.None, vm.AxisCards[0].FillMode);
        Assert.Equal(0.0, vm.AxisCards[0].PositionOffset);
    }

    [Fact]
    public void VisualizerVm_Defaults_WhenNoPersistedSettings()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);

        Assert.Equal(VisualizationMode.Graph, vm.SelectedMode);
        Assert.Equal(60, vm.WindowDurationSeconds);
    }

    // ═══════════════════════════════════════════════════════
    //  Save/Load Round-Trip — Sidebar
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SidebarVm_RoundTrip_ConnectionMode()
    {
        var settings = CreateDictionarySettings();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm1 = new SidebarViewModel(_tcode, settings.Object);
        vm1.SelectedMode = ConnectionMode.Serial;

        settings.Verify(s => s.Set("defaultConnectionMode", "Serial"), Times.Once);
    }

    [Fact]
    public void SidebarVm_RoundTrip_OutputRate()
    {
        var settings = CreateDictionarySettings();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);
        vm.OutputRateHz = 150;

        settings.Verify(s => s.Set("tcodeOutputRate", 150), Times.Once);
    }

    [Fact]
    public void SidebarVm_RoundTrip_GlobalOffset()
    {
        var settings = CreateDictionarySettings();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);
        vm.GlobalOffsetMs = -250;

        settings.Verify(s => s.Set("globalFunscriptOffset", -250), Times.Once);
    }

    // ═══════════════════════════════════════════════════════
    //  Save/Load Round-Trip — Axis Control
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void AxisControlVm_RoundTrip_PositionOffset()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        vm.AxisCards[0].PositionOffset = 25.0; // L0

        settings.Verify(s => s.Set("axis_L0_positionOffset", 25.0), Times.AtLeastOnce);
    }

    [Fact]
    public void AxisControlVm_LoadsPersistedPositionOffset()
    {
        var settings = CreateDefaultSettings();
        settings.Setup(s => s.Get("axis_L0_positionOffset", 0.0)).Returns(30.0);
        settings.Setup(s => s.Get("axis_R0_positionOffset", 0.0)).Returns(45.0);

        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        Assert.Equal(30.0, vm.AxisCards[0].PositionOffset, 1);
        Assert.Equal(45.0, vm.AxisCards[1].PositionOffset, 1);
    }

    [Fact]
    public void AxisControlVm_RoundTrip_FillModeAndSync()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        vm.AxisCards[1].FillMode = AxisFillMode.Sine;    // R0
        vm.AxisCards[1].SyncWithStroke = true;
        vm.AxisCards[1].FillSpeedHz = 2.0;

        settings.Verify(s => s.Set("axis_R0_fillMode", "Sine"), Times.AtLeastOnce);
        settings.Verify(s => s.Set("axis_R0_syncWithStroke", true), Times.AtLeastOnce);
        settings.Verify(s => s.Set("axis_R0_fillSpeedHz", 2.0), Times.AtLeastOnce);
    }

    // ═══════════════════════════════════════════════════════
    //  Save/Load Round-Trip — Visualizer
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void VisualizerVm_RoundTrip_Mode()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        vm.SelectedMode = VisualizationMode.Heatmap;

        settings.Verify(s => s.Set("visualizerMode", "Heatmap"), Times.Once);
    }

    [Fact]
    public void VisualizerVm_RoundTrip_WindowDuration()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        vm.WindowDurationSeconds = 120;

        settings.Verify(s => s.Set("visualizerWindowDuration", "120"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════
    //  SettingChanged — Sidebar
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SidebarVm_OnSettingChanged_UpdatesConnectionMode()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);
        Assert.Equal(ConnectionMode.UDP, vm.SelectedMode);

        // Simulate host changing the setting
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("Serial");
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("defaultConnectionMode");

        Assert.Equal(ConnectionMode.Serial, vm.SelectedMode);
        Assert.Contains("SelectedMode", changed);
        Assert.Contains("IsUdpMode", changed);
        Assert.Contains("IsSerialMode", changed);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_UpdatesOutputRate()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        // Simulate host changing output rate
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(150);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("tcodeOutputRate");

        Assert.Equal(150, vm.OutputRateHz);
        Assert.Contains("OutputRateHz", changed);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_UpdatesGlobalOffset()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(-300);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("globalFunscriptOffset");

        Assert.Equal(-300, vm.GlobalOffsetMs);
        Assert.Contains("GlobalOffsetMs", changed);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_ClampsOutputRate()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        // 999 should be clamped to 200
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(999);
        vm.OnSettingChanged("tcodeOutputRate");

        Assert.Equal(200, vm.OutputRateHz);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_IgnoresUnknownKey()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("unknownKey");

        Assert.Empty(changed);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_NoOpWhenValueUnchanged()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        // Re-fire with same value
        vm.OnSettingChanged("tcodeOutputRate");

        Assert.Empty(changed);
    }

    // ═══════════════════════════════════════════════════════
    //  SettingChanged — Axis Control
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesMin()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_L0_min", 0)).Returns(20);
        var changed = new List<string>();
        vm.AxisCards[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_L0_min");

        Assert.Equal(20, vm.AxisCards[0].Min);
        Assert.Contains("Min", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesMax()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_R0_max", 100)).Returns(80);
        var changed = new List<string>();
        vm.AxisCards[1].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_R0_max");

        Assert.Equal(80, vm.AxisCards[1].Max);
        Assert.Contains("Max", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesEnabled()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_R1_enabled", true)).Returns(false);
        var changed = new List<string>();
        vm.AxisCards[2].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_R1_enabled");

        Assert.False(vm.AxisCards[2].Enabled);
        Assert.Contains("Enabled", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesFillMode()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_R2_fillMode", "None")).Returns("Sine");
        var changed = new List<string>();
        vm.AxisCards[3].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_R2_fillMode");

        Assert.Equal(AxisFillMode.Sine, vm.AxisCards[3].FillMode);
        Assert.Contains("FillMode", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesPositionOffset()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_L0_positionOffset", 0.0)).Returns(25.0);
        var changed = new List<string>();
        vm.AxisCards[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_L0_positionOffset");

        Assert.Equal(25.0, vm.AxisCards[0].PositionOffset, 1);
        Assert.Contains("PositionOffset", changed);
        Assert.Contains("PositionOffsetLabel", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_IgnoresNonAxisKey()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);
        var changed = new List<string>();
        vm.AxisCards[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("tcodeOutputRate"); // not an axis key

        Assert.Empty(changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_IgnoresUnknownAxis()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        // R3 doesn't exist
        vm.OnSettingChanged("axis_R3_min"); // should not throw
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesSyncWithStroke()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_R0_syncWithStroke", false)).Returns(true);
        var changed = new List<string>();
        vm.AxisCards[1].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_R0_syncWithStroke");

        Assert.True(vm.AxisCards[1].SyncWithStroke);
        Assert.Contains("SyncWithStroke", changed);
    }

    [Fact]
    public void AxisControlVm_OnSettingChanged_UpdatesFillSpeedHz()
    {
        var settings = CreateDefaultSettings();
        var vm = new AxisControlViewModel(_tcode, settings.Object, _parser, _matcher);

        settings.Setup(s => s.Get("axis_R1_fillSpeedHz", 1.0)).Returns(3.0);
        var changed = new List<string>();
        vm.AxisCards[2].PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("axis_R1_fillSpeedHz");

        Assert.Equal(3.0, vm.AxisCards[2].FillSpeedHz, 1);
        Assert.Contains("FillSpeedHz", changed);
    }

    // ═══════════════════════════════════════════════════════
    //  SettingChanged — Visualizer
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void VisualizerVm_OnSettingChanged_UpdatesMode()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        Assert.Equal(VisualizationMode.Graph, vm.SelectedMode);

        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Heatmap");
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        var repaintFired = false;
        vm.RepaintRequested += () => repaintFired = true;

        vm.OnSettingChanged("visualizerMode");

        Assert.Equal(VisualizationMode.Heatmap, vm.SelectedMode);
        Assert.Contains("SelectedMode", changed);
        Assert.True(repaintFired);
    }

    [Fact]
    public void VisualizerVm_OnSettingChanged_UpdatesWindowDuration()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        Assert.Equal(60, vm.WindowDurationSeconds);

        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("120");
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("visualizerWindowDuration");

        Assert.Equal(120, vm.WindowDurationSeconds);
        Assert.Contains("WindowDurationSeconds", changed);
        Assert.Contains("TimeWindowRadius", changed);
    }

    [Fact]
    public void VisualizerVm_OnSettingChanged_IgnoresInvalidDuration()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);

        // 45 is not in AvailableWindowDurations [30, 60, 120, 300]
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("45");
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("visualizerWindowDuration");

        Assert.Equal(60, vm.WindowDurationSeconds); // unchanged
        Assert.DoesNotContain("WindowDurationSeconds", changed);
    }

    [Fact]
    public void VisualizerVm_OnSettingChanged_IgnoresUnknownKey()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("unknownKey");

        Assert.Empty(changed);
    }

    [Fact]
    public void VisualizerVm_OnSettingChanged_NoOpWhenValueUnchanged()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("visualizerMode", "Graph")).Returns("Graph");
        settings.Setup(s => s.Get("visualizerWindowDuration", "60")).Returns("60");

        var vm = new VisualizerViewModel(settings.Object);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        // Same value as current
        vm.OnSettingChanged("visualizerMode");

        Assert.Empty(changed);
    }

    // ═══════════════════════════════════════════════════════
    //  SettingChanged — Sidebar BaudRate and UdpPort
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SidebarVm_OnSettingChanged_UpdatesBaudRate()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(9600);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("defaultBaudRate");

        Assert.Equal(9600, vm.SelectedBaudRate);
        Assert.Contains("SelectedBaudRate", changed);
    }

    [Fact]
    public void SidebarVm_OnSettingChanged_UpdatesUdpPort()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(8888);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.OnSettingChanged("defaultUdpPort");

        Assert.Equal(8888, vm.UdpPort);
        Assert.Contains("UdpPort", changed);
    }
}
