using Moq;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Plugin;
using Vido.Haptics;
using Xunit;

namespace Osr2PlusPlugin.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="BeatBarViewModel"/> — mode management, beat loading,
/// time updates, settings persistence, and event raising.
/// </summary>
public class BeatBarViewModelTests
{
    private readonly Mock<IPluginSettingsStore> _mockSettings;
    private readonly BeatDetectionService _beatDetection;
    private readonly BeatBarViewModel _sut;

    public BeatBarViewModelTests()
    {
        _mockSettings = new Mock<IPluginSettingsStore>();
        _mockSettings.Setup(s => s.Get("beatBarMode", "Off")).Returns("Off");

        _beatDetection = new BeatDetectionService();
        _sut = new BeatBarViewModel(_mockSettings.Object, _beatDetection);
    }

    // ── Helper ───────────────────────────────────────────────

    private static FunscriptData MakeScript(params (long atMs, int pos)[] points)
    {
        return new FunscriptData
        {
            AxisId = "L0",
            Actions = points.Select(p => new FunscriptAction(p.atMs, p.pos)).ToList()
        };
    }

    /// <summary>
    /// A script with clear peaks and valleys for testing.
    /// Peaks at 250, 750, 1250, 1750. Valleys at 500, 1000, 1500.
    /// </summary>
    private static FunscriptData TestScript => MakeScript(
        (0, 10), (250, 90), (500, 10), (750, 95),
        (1000, 5), (1250, 85), (1500, 15), (1750, 92), (2000, 8));

    // ═══════════════════════════════════════════════════════
    //  Default Values
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_DefaultMode_IsOff()
    {
        Assert.Equal(BeatBarMode.Off, _sut.Mode);
    }

    [Fact]
    public void Constructor_DefaultBeats_IsEmpty()
    {
        Assert.Empty(_sut.Beats);
    }

    [Fact]
    public void Constructor_DefaultIsActive_IsFalse()
    {
        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void Constructor_DefaultCurrentTimeMs_IsZero()
    {
        Assert.Equal(0.0, _sut.CurrentTimeMs);
    }

    [Fact]
    public void Constructor_DefaultHasBeats_IsFalse()
    {
        Assert.False(_sut.HasBeats);
    }

    // ═══════════════════════════════════════════════════════
    //  LoadBeats
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void LoadBeats_SetsBeatsFromScript()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        _sut.LoadBeats(TestScript);

        Assert.True(_sut.HasBeats);
        Assert.Equal(new double[] { 250, 750, 1250, 1750 }, _sut.Beats);
    }

    [Fact]
    public void LoadBeats_NullScript_ClearsBeats()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);
        Assert.True(_sut.HasBeats);

        _sut.LoadBeats(null);

        Assert.False(_sut.HasBeats);
        Assert.Empty(_sut.Beats);
    }

    [Fact]
    public void LoadBeats_OffMode_ClearsBeats()
    {
        _sut.Mode = BeatBarMode.Off;

        _sut.LoadBeats(TestScript);

        Assert.False(_sut.HasBeats);
        Assert.Empty(_sut.Beats);
    }

    [Fact]
    public void LoadBeats_OnValley_FindsValleys()
    {
        _sut.Mode = BeatBarMode.OnValley;

        _sut.LoadBeats(TestScript);

        Assert.Equal(new double[] { 500, 1000, 1500 }, _sut.Beats);
    }

    // ═══════════════════════════════════════════════════════
    //  ClearBeats
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ClearBeats_ResetsState()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);
        Assert.True(_sut.HasBeats);

        _sut.ClearBeats();

        Assert.False(_sut.HasBeats);
        Assert.Empty(_sut.Beats);
        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void ClearBeats_RaisesRepaintRequested()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);

        var raised = false;
        _sut.RepaintRequested += () => raised = true;
        _sut.ClearBeats();

        Assert.True(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  Mode Changes
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ModeChange_RedetectsBeats()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);
        var peakBeats = _sut.Beats.ToList();

        _sut.Mode = BeatBarMode.OnValley;

        Assert.NotEqual(peakBeats, _sut.Beats);
        Assert.Equal(new double[] { 500, 1000, 1500 }, _sut.Beats);
    }

    [Fact]
    public void ModeChange_PersistsToSettings()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        _mockSettings.Verify(s => s.Set("beatBarMode", "OnPeak"), Times.Once);
    }

    [Fact]
    public void ModeChange_PersistsOnValley()
    {
        _sut.Mode = BeatBarMode.OnValley;

        _mockSettings.Verify(s => s.Set("beatBarMode", "OnValley"), Times.Once);
    }

    [Fact]
    public void ModeChange_RaisesModeChangedEvent()
    {
        BeatBarMode? receivedMode = null;
        _sut.ModeChanged += mode => receivedMode = mode;

        _sut.Mode = BeatBarMode.OnPeak;

        Assert.Equal(BeatBarMode.OnPeak, receivedMode);
    }

    [Fact]
    public void ModeChange_RaisesRepaintRequested()
    {
        var raised = false;
        _sut.RepaintRequested += () => raised = true;

        _sut.Mode = BeatBarMode.OnPeak;

        Assert.True(raised);
    }

    [Fact]
    public void ModeChange_Off_ClearsActive()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);
        Assert.True(_sut.IsActive);

        _sut.Mode = BeatBarMode.Off;

        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void ModeChange_SameValue_NoEvent()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var raised = false;
        _sut.ModeChanged += _ => raised = true;
        _sut.Mode = BeatBarMode.OnPeak; // same value

        Assert.False(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  UpdateTime
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void UpdateTime_SetsCurrentTimeMs()
    {
        _sut.UpdateTime(500.0);

        Assert.Equal(500.0, _sut.CurrentTimeMs);
    }

    [Fact]
    public void UpdateTime_RaisesRepaintRequested()
    {
        var raised = false;
        _sut.RepaintRequested += () => raised = true;

        _sut.UpdateTime(100.0);

        Assert.True(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  IsActive
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void IsActive_OffMode_ReturnsFalse()
    {
        _sut.Mode = BeatBarMode.Off;
        _sut.LoadBeats(TestScript);

        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void IsActive_OnPeakWithBeats_ReturnsTrue()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);

        Assert.True(_sut.IsActive);
    }

    [Fact]
    public void IsActive_OnPeakWithoutBeats_ReturnsFalse()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        // No script loaded → no beats

        Assert.False(_sut.IsActive);
    }

    [Fact]
    public void IsActive_OnValleyWithBeats_ReturnsTrue()
    {
        _sut.Mode = BeatBarMode.OnValley;
        _sut.LoadBeats(TestScript);

        Assert.True(_sut.IsActive);
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence — Loading
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_LoadsSavedMode_OnPeak()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("OnPeak");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        Assert.Equal(BeatBarMode.OnPeak, vm.Mode);
    }

    [Fact]
    public void Constructor_LoadsSavedMode_OnValley()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("OnValley");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        Assert.Equal(BeatBarMode.OnValley, vm.Mode);
    }

    [Fact]
    public void Constructor_InvalidSetting_DefaultsToOff()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("InvalidMode");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        Assert.Equal(BeatBarMode.Off, vm.Mode);
    }

    [Fact]
    public void Constructor_DoesNotSaveOnLoad()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("OnPeak");

        _ = new BeatBarViewModel(settings.Object, _beatDetection);

        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════
    //  External Mode Persistence
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Constructor_SavedExternalMode_StaysOffUntilSourceRegisters()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("com.vido.pulse");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        // Before the external source registers, mode is Off
        Assert.Equal(BeatBarMode.Off, vm.Mode);
    }

    [Fact]
    public void SavedExternalMode_AutoSelectedWhenSourceRegisters()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("com.vido.pulse");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        vm.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object,
            IsRegistering = true
        });

        Assert.Equal("com.vido.pulse", vm.Mode.Id);
        Assert.True(vm.Mode.IsExternal);
    }

    [Fact]
    public void SavedExternalMode_DoesNotResaveOnAutoSelect()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("com.vido.pulse");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        vm.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object,
            IsRegistering = true
        });

        // The value is already persisted — auto-select should save (it's the normal Mode setter path)
        // but should NOT double-save from LoadSettings
        settings.Verify(s => s.Set("beatBarMode", "com.vido.pulse"), Times.Once);
    }

    [Fact]
    public void SavedExternalMode_OnlyClearedOnceAfterAutoSelect()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("com.vido.pulse");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        var reg = new ExternalBeatSourceRegistration
        {
            Source = source.Object,
            IsRegistering = true
        };

        vm.OnBeatSourceRegistration(reg);
        Assert.Equal("com.vido.pulse", vm.Mode.Id);

        // If user manually switches to Off, then source re-registers, mode should NOT auto-revert to Pulse
        vm.Mode = BeatBarMode.Off;
        vm.OnBeatSourceRegistration(reg);
        Assert.Equal(BeatBarMode.Off, vm.Mode);
    }

    [Fact]
    public void SavedExternalMode_UnrelatedSourceDoesNotTriggerAutoSelect()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("beatBarMode", "Off")).Returns("com.vido.pulse");

        var vm = new BeatBarViewModel(settings.Object, _beatDetection);

        var other = new Mock<IExternalBeatSource>();
        other.Setup(s => s.Id).Returns("com.other.plugin");
        other.Setup(s => s.DisplayName).Returns("Other");
        other.Setup(s => s.IsAvailable).Returns(true);

        vm.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = other.Object,
            IsRegistering = true
        });

        // "com.other.plugin" registered, but saved was "com.vido.pulse" — stay Off
        Assert.Equal(BeatBarMode.Off, vm.Mode);
    }

    // ═══════════════════════════════════════════════════════
    //  External Setting Changes
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ExternalSettingChange_UpdatesMode()
    {
        _mockSettings.Setup(s => s.Get("beatBarMode", "Off")).Returns("OnValley");

        _sut.OnSettingChanged("beatBarMode");

        Assert.Equal(BeatBarMode.OnValley, _sut.Mode);
    }

    [Fact]
    public void ExternalSettingChange_DoesNotResave()
    {
        _mockSettings.Setup(s => s.Get("beatBarMode", "Off")).Returns("OnPeak");

        _sut.OnSettingChanged("beatBarMode");

        // Should not call Set for the same key it just read
        _mockSettings.Verify(s => s.Set("beatBarMode", "OnPeak"), Times.Never);
    }

    [Fact]
    public void ExternalSettingChange_IgnoresUnrelatedKey()
    {
        _sut.Mode = BeatBarMode.Off;

        _sut.OnSettingChanged("someOtherKey");

        Assert.Equal(BeatBarMode.Off, _sut.Mode);
    }

    [Fact]
    public void ExternalSettingChange_SameValue_NoEvent()
    {
        _mockSettings.Setup(s => s.Get("beatBarMode", "Off")).Returns("Off");

        var raised = false;
        _sut.ModeChanged += _ => raised = true;
        _sut.OnSettingChanged("beatBarMode");

        Assert.False(raised);
    }

    // ═══════════════════════════════════════════════════════
    //  PropertyChanged
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ModeChange_RaisesPropertyChanged()
    {
        var changedProps = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        _sut.Mode = BeatBarMode.OnPeak;

        Assert.Contains("Mode", changedProps);
        Assert.Contains("IsActive", changedProps);
    }

    [Fact]
    public void LoadBeats_RaisesIsActivePropertyChanged()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var changedProps = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);
        _sut.LoadBeats(TestScript);

        Assert.Contains("IsActive", changedProps);
    }

    [Fact]
    public void ClearBeats_RaisesIsActivePropertyChanged()
    {
        _sut.Mode = BeatBarMode.OnPeak;
        _sut.LoadBeats(TestScript);

        var changedProps = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);
        _sut.ClearBeats();

        Assert.Contains("IsActive", changedProps);
    }

    // ═══════════════════════════════════════════════════════
    //  vido-007: Mode instance preserved across RebuildAvailableModes
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void RebuildAvailableModes_PreservesSelectedMode_WithNewInstance()
    {
        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        // Register & select the external mode
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Track PropertyChanged during rebuild
        var changedProps = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        // Re-register same source → triggers RebuildAvailableModes internally
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });

        Assert.Equal("com.vido.pulse", _sut.Mode.Id);
        Assert.Contains("Mode", changedProps);
    }

    [Fact]
    public void RebuildAvailableModes_ModeInstanceMatchesCollectionItem()
    {
        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Re-register → rebuild
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });

        var collectionItem = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");
        Assert.True(ReferenceEquals(_sut.Mode, collectionItem),
            "Mode must be the exact same instance as the item in AvailableModes");
    }

    [Fact]
    public void RebuildAvailableModes_ExternalSource_AfterAnalysis_PulseStillSelected()
    {
        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Register, select Pulse
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Simulate analysis completing (re-register with same source → rebuild)
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
        {
            Source = source.Object, IsRegistering = true
        });

        Assert.Equal("com.vido.pulse", _sut.Mode.Id);
        Assert.True(_sut.Mode.IsExternal);
        Assert.True(ReferenceEquals(_sut.Mode,
            _sut.AvailableModes.First(m => m.Id == "com.vido.pulse")));
    }

    // ═══════════════════════════════════════════════════════
    //  Mode persistence across Pulse ↔ Funscript switching
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void PulseDisabled_RestoresOnPeak()
    {
        // User selects OnPeak for funscript mode
        _sut.Mode = BeatBarMode.OnPeak;

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Pulse activates → user switches to Pulse
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Pulse deactivates → source unregisters
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });

        // Should restore OnPeak
        Assert.Equal(BeatBarMode.OnPeak, _sut.Mode);
    }

    [Fact]
    public void PulseDisabled_RestoresOnValley()
    {
        _sut.Mode = BeatBarMode.OnValley;

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });

        Assert.Equal(BeatBarMode.OnValley, _sut.Mode);
    }

    [Fact]
    public void PulseDisabled_RestoresOff_WhenPreviousWasOff()
    {
        // Mode is Off (default)
        Assert.Equal(BeatBarMode.Off, _sut.Mode);

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);

        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });

        Assert.Equal(BeatBarMode.Off, _sut.Mode);
    }

    [Fact]
    public void PulseEnabled_ThenDisabled_ThenReEnabled_RestoresCorrectly()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Enable Pulse
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Disable Pulse → restores OnPeak
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });
        Assert.Equal(BeatBarMode.OnPeak, _sut.Mode);

        // Change to OnValley while in funscript mode
        _sut.Mode = BeatBarMode.OnValley;

        // Re-enable Pulse — should auto-select Pulse
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        Assert.Equal("com.vido.pulse", _sut.Mode.Id);

        // Disable Pulse → should now restore OnValley
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });
        Assert.Equal(BeatBarMode.OnValley, _sut.Mode);
    }

    [Fact]
    public void PulseReRegistration_DoesNotLoseSavedMode()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Enable Pulse, select it
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");

        // Re-register (e.g. after analysis) — should stay on Pulse, not lose saved mode
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        Assert.Equal("com.vido.pulse", _sut.Mode.Id);

        // Now disable — should still restore OnPeak
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });
        Assert.Equal(BeatBarMode.OnPeak, _sut.Mode);
    }

    [Fact]
    public void PulseOffThenOn_AutoSelectsPulse()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var source = new Mock<IExternalBeatSource>();
        source.Setup(s => s.Id).Returns("com.vido.pulse");
        source.Setup(s => s.DisplayName).Returns("Pulse");
        source.Setup(s => s.IsAvailable).Returns(true);
        source.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Enable Pulse — auto-selects
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        Assert.Equal("com.vido.pulse", _sut.Mode.Id);

        // Disable Pulse → restores OnPeak
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = false });
        Assert.Equal(BeatBarMode.OnPeak, _sut.Mode);

        // Re-enable Pulse — should auto-select Pulse again, not show blank
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = source.Object, IsRegistering = true });
        Assert.Equal("com.vido.pulse", _sut.Mode.Id);
        Assert.True(ReferenceEquals(_sut.Mode,
            _sut.AvailableModes.First(m => m.Id == "com.vido.pulse")));
    }

    // ═══════════════════════════════════════════════════════
    //  Multi-source: remembers specific external mode
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void MultiSource_RestoresSpecificExternalMode_AfterToggle()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var pulse = new Mock<IExternalBeatSource>();
        pulse.Setup(s => s.Id).Returns("com.vido.pulse");
        pulse.Setup(s => s.DisplayName).Returns("Pulse");
        pulse.Setup(s => s.IsAvailable).Returns(true);
        pulse.Setup(s => s.HidesBuiltInModes).Returns(true);

        var custom = new Mock<IExternalBeatSource>();
        custom.Setup(s => s.Id).Returns("com.vido.custom");
        custom.Setup(s => s.DisplayName).Returns("Custom Beat");
        custom.Setup(s => s.IsAvailable).Returns(true);
        custom.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Register both sources
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = true });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = true });

        // User explicitly selects the second source (not the first)
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.custom");
        Assert.Equal("com.vido.custom", _sut.Mode.Id);

        // Both sources unregister
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = false });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = false });
        Assert.Equal(BeatBarMode.OnPeak, _sut.Mode);

        // Both sources re-register
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = true });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = true });

        // Should restore the specific mode the user selected, NOT the first external
        Assert.Equal("com.vido.custom", _sut.Mode.Id);
    }

    [Fact]
    public void MultiSource_FallsBackToFirstExternal_WhenNoSavedId()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var pulse = new Mock<IExternalBeatSource>();
        pulse.Setup(s => s.Id).Returns("com.vido.pulse");
        pulse.Setup(s => s.DisplayName).Returns("Pulse");
        pulse.Setup(s => s.IsAvailable).Returns(true);
        pulse.Setup(s => s.HidesBuiltInModes).Returns(true);

        var custom = new Mock<IExternalBeatSource>();
        custom.Setup(s => s.Id).Returns("com.vido.custom");
        custom.Setup(s => s.DisplayName).Returns("Custom Beat");
        custom.Setup(s => s.IsAvailable).Returns(true);
        custom.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Register both sources — first time, no saved external mode ID
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = true });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = true });

        // Should auto-select some external mode (the first one registered)
        Assert.True(_sut.Mode.IsExternal);
    }

    [Fact]
    public void MultiSource_SwitchingBetweenExternals_TracksLastSelected()
    {
        _sut.Mode = BeatBarMode.OnPeak;

        var pulse = new Mock<IExternalBeatSource>();
        pulse.Setup(s => s.Id).Returns("com.vido.pulse");
        pulse.Setup(s => s.DisplayName).Returns("Pulse");
        pulse.Setup(s => s.IsAvailable).Returns(true);
        pulse.Setup(s => s.HidesBuiltInModes).Returns(true);

        var custom = new Mock<IExternalBeatSource>();
        custom.Setup(s => s.Id).Returns("com.vido.custom");
        custom.Setup(s => s.DisplayName).Returns("Custom Beat");
        custom.Setup(s => s.IsAvailable).Returns(true);
        custom.Setup(s => s.HidesBuiltInModes).Returns(true);

        // Register both
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = true });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = true });

        // Select Pulse first, then switch to Custom
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.pulse");
        Assert.Equal("com.vido.pulse", _sut.Mode.Id);
        _sut.Mode = _sut.AvailableModes.First(m => m.Id == "com.vido.custom");
        Assert.Equal("com.vido.custom", _sut.Mode.Id);

        // Unregister both
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = false });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = false });

        // Re-register both
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = pulse.Object, IsRegistering = true });
        _sut.OnBeatSourceRegistration(new ExternalBeatSourceRegistration
            { Source = custom.Object, IsRegistering = true });

        // Should restore Custom (the last one the user selected), not Pulse
        Assert.Equal("com.vido.custom", _sut.Mode.Id);
    }
}
