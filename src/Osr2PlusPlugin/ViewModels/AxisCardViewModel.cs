using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for an individual axis card. Wraps <see cref="AxisConfig"/> and
/// exposes bindable properties, commands for test mode, fill mode logic,
/// position offset behaviour, and funscript loading.
/// </summary>
public class AxisCardViewModel : INotifyPropertyChanged
{
    private readonly AxisConfig _config;
    private readonly TCodeService _tcode;
    private bool _isVideoPlaying;
    private bool _isDeviceConnected;

    /// <summary>
    /// Factory delegate for opening a file dialog. Returns the selected file path or null.
    /// Injectable for testing.
    /// </summary>
    internal Func<string?>? FileDialogFactory { get; set; }

    /// <summary>
    /// Delegate for parsing a funscript file. Injectable for testing.
    /// </summary>
    internal Func<string, string, FunscriptData>? ParseFileFunc { get; set; }

    /// <summary>
    /// Raised when axis config changes and the caller should propagate configs to TCodeService.
    /// </summary>
    public event Action? ConfigChanged;

    // ═══════════════════════════════════════════════════════
    //  Identity (read-only from AxisConfig)
    // ═══════════════════════════════════════════════════════

    /// <summary>Axis identifier: "L0", "R0", "R1", "R2".</summary>
    public string AxisId => _config.Id;

    /// <summary>Display name: "Stroke", "Twist", "Roll", "Pitch".</summary>
    public string AxisName => _config.Name;

    /// <summary>Hex color for this axis (e.g. "#007ACC").</summary>
    public string AxisColor => _config.Color;

    /// <summary>Whether this is the stroke (L0) axis.</summary>
    public bool IsStroke => _config.IsStroke;

    /// <summary>Whether this is the pitch (R2) axis.</summary>
    public bool IsPitch => _config.IsPitch;

    // ═══════════════════════════════════════════════════════
    //  Persisted Config Properties
    // ═══════════════════════════════════════════════════════

    /// <summary>Minimum amplitude (0–99).</summary>
    public int Min
    {
        get => _config.Min;
        set
        {
            if (_config.Min != value)
            {
                _config.Min = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeLabel));
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Maximum amplitude (1–100).</summary>
    public int Max
    {
        get => _config.Max;
        set
        {
            if (_config.Max != value)
            {
                _config.Max = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RangeLabel));
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Whether this axis is enabled for TCode output.</summary>
    public bool Enabled
    {
        get => _config.Enabled;
        set
        {
            if (_config.Enabled != value)
            {
                _config.Enabled = value;
                OnPropertyChanged();
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Active fill mode.</summary>
    public AxisFillMode FillMode
    {
        get => _config.FillMode;
        set
        {
            if (_config.FillMode != value)
            {
                _config.FillMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowSyncToggle));
                OnPropertyChanged(nameof(ShowFillSpeedSlider));
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Whether fill pattern syncs with L0 stroke.</summary>
    public bool SyncWithStroke
    {
        get => _config.SyncWithStroke;
        set
        {
            if (_config.SyncWithStroke != value)
            {
                _config.SyncWithStroke = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowFillSpeedSlider));
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Independent fill speed in Hz (0.1–3.0).</summary>
    public double FillSpeedHz
    {
        get => _config.FillSpeedHz;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 3.0);
            if (Math.Abs(_config.FillSpeedHz - clamped) > 0.001)
            {
                _config.FillSpeedHz = clamped;
                OnPropertyChanged();
                RaiseConfigChanged();
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Ephemeral Properties
    // ═══════════════════════════════════════════════════════

    /// <summary>Position offset value. L0: -50 to +50 (%), R0: 0–359 (°). NOT persisted.</summary>
    public double PositionOffset
    {
        get => _config.PositionOffset;
        set
        {
            var clamped = Math.Clamp(value, PositionOffsetMin, PositionOffsetMax);
            if (Math.Abs(_config.PositionOffset - clamped) > 0.001)
            {
                _config.PositionOffset = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PositionOffsetLabel));
                RaiseConfigChanged();
            }
        }
    }

    /// <summary>Test speed in Hz (0.1–3.0).</summary>
    public double TestSpeedHz
    {
        get => _config.TestSpeedHz;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 3.0);
            if (Math.Abs(_config.TestSpeedHz - clamped) > 0.001)
            {
                _config.TestSpeedHz = clamped;
                OnPropertyChanged();
                if (_config.IsTesting)
                    _tcode.UpdateTestSpeed(AxisId, clamped);
            }
        }
    }

    /// <summary>Whether this axis is currently in test mode.</summary>
    public bool IsTesting
    {
        get => _config.IsTesting;
        private set
        {
            if (_config.IsTesting != value)
            {
                _config.IsTesting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TestButtonText));
            }
        }
    }

    /// <summary>Whether the card is expanded.</summary>
    public bool IsExpanded
    {
        get => _config.IsExpanded;
        set
        {
            if (_config.IsExpanded != value)
            {
                _config.IsExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Derived / Display Properties
    // ═══════════════════════════════════════════════════════

    /// <summary>Range display label: "0-100".</summary>
    public string RangeLabel => _config.RangeLabel;

    /// <summary>Available fill modes filtered by axis type.</summary>
    public AxisFillMode[] AvailableFillModes => _config.AvailableFillModes;

    /// <summary>Whether the SyncWithStroke toggle should be visible.</summary>
    public bool ShowSyncToggle => _config.Id != "L0"
        && _config.FillMode is not (AxisFillMode.None or AxisFillMode.Grind or AxisFillMode.ReverseGrind);

    /// <summary>Whether the position offset section should be visible (L0 and R0 only).</summary>
    public bool ShowPositionOffset => _config.HasPositionOffset;

    /// <summary>
    /// Whether the fill speed slider should be visible.
    /// Visible when fill != None AND (SyncWithStroke is false OR axis is L0).
    /// </summary>
    public bool ShowFillSpeedSlider =>
        _config.FillMode != AxisFillMode.None
        && (!_config.SyncWithStroke || _config.Id == "L0");

    /// <summary>Formatted position offset label. L0: "{value}%", R0: "{value}°".</summary>
    public string PositionOffsetLabel => _config.Id switch
    {
        "L0" => $"{_config.PositionOffset:0}%",
        "R0" => $"{_config.PositionOffset:0}°",
        _ => ""
    };

    /// <summary>Minimum position offset. L0: -50, R0: 0.</summary>
    public double PositionOffsetMin => _config.Id == "L0" ? -50.0 : 0.0;

    /// <summary>Maximum position offset. L0: +50, R0: 359.</summary>
    public double PositionOffsetMax => _config.Id == "L0" ? 50.0 : 359.0;

    /// <summary>Default position offset. L0: 0, R0: 0.</summary>
    public double PositionOffsetDefault => 0.0;

    /// <summary>Test button display text.</summary>
    public string TestButtonText => IsTesting ? "Stop" : "Test";

    /// <summary>Whether the test button is enabled.</summary>
    public bool IsTestEnabled => !_isVideoPlaying && _isDeviceConnected;

    /// <summary>Loaded funscript filename or null.</summary>
    public string? ScriptFileName
    {
        get => _config.ScriptFileName;
        private set
        {
            _config.ScriptFileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasScript));
            OnPropertyChanged(nameof(ScriptDisplayName));
        }
    }

    /// <summary>True when a funscript is loaded for this axis.</summary>
    public bool HasScript => _config.HasScript;

    /// <summary>Display name for the loaded script (filename only, or "None").</summary>
    public string ScriptDisplayName => _config.ScriptFileName != null
        ? System.IO.Path.GetFileName(_config.ScriptFileName)
        : "None";

    /// <summary>Whether the script was manually assigned (survives auto-load).</summary>
    public bool IsScriptManual
    {
        get => _config.IsScriptManual;
        set => _config.IsScriptManual = value;
    }

    // ═══════════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════════

    /// <summary>Toggles the expanded/collapsed state of the card.</summary>
    public ICommand ToggleExpandCommand { get; }

    /// <summary>Toggles test mode for this axis.</summary>
    public ICommand TestCommand { get; }

    /// <summary>Opens a file dialog to manually load a funscript.</summary>
    public ICommand OpenScriptCommand { get; }

    // ═══════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════

    public AxisCardViewModel(AxisConfig config, TCodeService tcode)
    {
        _config = config;
        _tcode = tcode;

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        TestCommand = new RelayCommand(ExecuteTest);
        OpenScriptCommand = new RelayCommand(ExecuteOpenScript);

        // Listen for test axis stopped (ramp-down complete)
        _tcode.TestAxisStopped += OnTestAxisStopped;
        _tcode.AllTestsStopped += OnAllTestsStopped;
    }

    // ═══════════════════════════════════════════════════════
    //  State Updates (called by parent ViewModel)
    // ═══════════════════════════════════════════════════════

    /// <summary>Updates the video playing state. Refreshes IsTestEnabled.</summary>
    internal void SetVideoPlaying(bool playing)
    {
        if (_isVideoPlaying != playing)
        {
            _isVideoPlaying = playing;
            OnPropertyChanged(nameof(IsTestEnabled));
        }
    }

    /// <summary>Updates the device connection state. Refreshes IsTestEnabled.</summary>
    internal void SetDeviceConnected(bool connected)
    {
        if (_isDeviceConnected != connected)
        {
            _isDeviceConnected = connected;
            OnPropertyChanged(nameof(IsTestEnabled));
        }
    }

    /// <summary>Sets a script loaded via auto-load (does not overwrite manual assignments).</summary>
    internal void SetAutoLoadedScript(string? filePath)
    {
        if (!IsScriptManual)
        {
            ScriptFileName = filePath;
        }
    }

    /// <summary>Clears the loaded script (unless manual).</summary>
    internal void ClearAutoLoadedScript()
    {
        if (!IsScriptManual)
        {
            ScriptFileName = null;
        }
    }

    /// <summary>Force-clears the script (including manual overrides).</summary>
    internal void ClearAllScripts()
    {
        _config.IsScriptManual = false;
        ScriptFileName = null;
    }

    // ═══════════════════════════════════════════════════════
    //  Command Implementations
    // ═══════════════════════════════════════════════════════

    private void ExecuteTest()
    {
        if (IsTesting)
        {
            _tcode.StopTestAxis(AxisId);
            // IsTesting will be cleared via TestAxisStopped event after ramp-down
        }
        else
        {
            if (!IsTestEnabled) return;
            IsTesting = true;
            _tcode.StartTestAxis(AxisId, _config.TestSpeedHz);
        }
    }

    private void ExecuteOpenScript()
    {
        var filePath = FileDialogFactory?.Invoke();
        if (string.IsNullOrEmpty(filePath)) return;

        var data = ParseFileFunc?.Invoke(filePath, AxisId);
        if (data == null) return;

        ScriptFileName = filePath;
        IsScriptManual = true;

        // Notify parent to update TCodeService scripts
        ConfigChanged?.Invoke();
    }

    // ═══════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════

    private void OnTestAxisStopped(string axisId)
    {
        if (axisId == AxisId)
        {
            IsTesting = false;
        }
    }

    private void OnAllTestsStopped()
    {
        if (IsTesting)
        {
            IsTesting = false;
        }
    }

    private void RaiseConfigChanged() => ConfigChanged?.Invoke();

    // ═══════════════════════════════════════════════════════
    //  INotifyPropertyChanged
    // ═══════════════════════════════════════════════════════

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ═══════════════════════════════════════════════════════
    //  Minimal ICommand
    // ═══════════════════════════════════════════════════════

    private class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        internal void SuppressWarning() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
