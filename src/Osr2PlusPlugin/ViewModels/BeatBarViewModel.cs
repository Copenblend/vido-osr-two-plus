using System.ComponentModel;
using System.Runtime.CompilerServices;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Vido.Core.Plugin;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for the beat bar overlay and control bar ComboBox.
/// Manages mode selection (Off/OnPeak/OnValley), beat detection,
/// current playback time, and settings persistence.
/// </summary>
public class BeatBarViewModel : INotifyPropertyChanged
{
    private readonly IPluginSettingsStore _settings;
    private readonly BeatDetectionService _beatDetection;

    private BeatBarMode _mode = BeatBarMode.Off;
    private double _currentTimeMs;
    private List<double> _beats = new();
    private FunscriptData? _currentScript;

    // Suppress settings save when loading from store or external change
    private bool _suppressSave;

    // ── Properties ───────────────────────────────────────────

    /// <summary>
    /// The active beat bar mode. Bound to the control bar ComboBox.
    /// Persisted to settings.
    /// </summary>
    public BeatBarMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                if (!_suppressSave)
                    _settings.Set("beatBarMode", value.ToString());

                RedetectBeats();
                OnPropertyChanged(nameof(IsActive));
                ModeChanged?.Invoke(value);
                RepaintRequested?.Invoke();
            }
        }
    }

    /// <summary>
    /// True when the beat bar should be visible: mode is not Off and beats are loaded.
    /// </summary>
    public bool IsActive => _mode != BeatBarMode.Off && HasBeats;

    /// <summary>
    /// Current playback position in milliseconds. Updated at ~60Hz.
    /// </summary>
    public double CurrentTimeMs
    {
        get => _currentTimeMs;
        private set => Set(ref _currentTimeMs, value);
    }

    /// <summary>
    /// Sorted list of beat timestamps in milliseconds.
    /// </summary>
    public List<double> Beats
    {
        get => _beats;
        private set
        {
            if (Set(ref _beats, value))
                OnPropertyChanged(nameof(HasBeats));
        }
    }

    /// <summary>
    /// True when at least one beat has been detected.
    /// </summary>
    public bool HasBeats => _beats.Count > 0;

    // ── Events ───────────────────────────────────────────────

    /// <summary>
    /// Raised when the overlay should repaint (time update, mode change, beat data change).
    /// </summary>
    public event Action? RepaintRequested;

    /// <summary>
    /// Raised when the mode changes. Used by the plugin entry point to
    /// toggle overlay visibility.
    /// </summary>
    public event Action<BeatBarMode>? ModeChanged;

    // ── Constructor ──────────────────────────────────────────

    public BeatBarViewModel(IPluginSettingsStore settings, BeatDetectionService beatDetection)
    {
        _settings = settings;
        _beatDetection = beatDetection;
        LoadSettings();
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>
    /// Called when a new video is loaded and funscript data is available.
    /// Stores the script reference and detects beats based on the current mode.
    /// </summary>
    public void LoadBeats(FunscriptData? scriptData)
    {
        _currentScript = scriptData;
        RedetectBeats();
        OnPropertyChanged(nameof(IsActive));
    }

    /// <summary>
    /// Called when the video is unloaded. Clears all beat data and
    /// the stored script reference.
    /// </summary>
    public void ClearBeats()
    {
        _currentScript = null;
        Beats = new List<double>();
        OnPropertyChanged(nameof(IsActive));
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Called at ~60Hz during playback. Updates the current time and
    /// requests a repaint.
    /// </summary>
    public void UpdateTime(double positionMs)
    {
        CurrentTimeMs = positionMs;
        RepaintRequested?.Invoke();
    }

    // ── Settings Persistence ─────────────────────────────────

    private void LoadSettings()
    {
        var modeStr = _settings.Get("beatBarMode", "Off");
        if (Enum.TryParse<BeatBarMode>(modeStr, out var mode))
        {
            _suppressSave = true;
            _mode = mode;
            _suppressSave = false;
        }
    }

    /// <summary>
    /// Handles external setting changes (e.g. from the host Settings Panel).
    /// Re-reads the changed key and updates the backing field without re-saving.
    /// </summary>
    internal void OnSettingChanged(string key)
    {
        if (key != "beatBarMode") return;

        var modeStr = _settings.Get("beatBarMode", "Off");
        if (Enum.TryParse<BeatBarMode>(modeStr, out var mode) && mode != _mode)
        {
            _suppressSave = true;
            Mode = mode;
            _suppressSave = false;
        }
    }

    // ── Private Helpers ──────────────────────────────────────

    /// <summary>
    /// Re-runs beat detection using the current mode and stored script data.
    /// </summary>
    private void RedetectBeats()
    {
        Beats = _beatDetection.DetectBeats(_currentScript, _mode);
        RepaintRequested?.Invoke();
    }

    // ── INotifyPropertyChanged ───────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
