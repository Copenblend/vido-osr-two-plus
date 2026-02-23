using System.ComponentModel;
using System.Runtime.CompilerServices;
using Osr2PlusPlugin.Models;
using Vido.Core.Plugin;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for the funscript visualizer bottom panel.
/// Manages visualization mode (Graph/Heatmap), time window, loaded axes,
/// and current playback position. Persists settings via <see cref="IPluginSettingsStore"/>.
/// </summary>
public class VisualizerViewModel : INotifyPropertyChanged
{
    private readonly IPluginSettingsStore _settings;

    private VisualizationMode _selectedMode = VisualizationMode.Graph;
    private int _windowDurationSeconds = 60;
    private double _currentTime;
    private Dictionary<string, FunscriptData> _loadedAxes = new();

    // ── Static Dictionaries ──────────────────────────────────

    /// <summary>
    /// Axis color hex codes — consistent across all UI surfaces.
    /// </summary>
    public static readonly Dictionary<string, string> AxisColors = new()
    {
        { "L0", "#007ACC" },
        { "R0", "#B800CC" },
        { "R1", "#CC5200" },
        { "R2", "#14CC00" },
    };

    /// <summary>
    /// Human-readable axis names.
    /// </summary>
    public static readonly Dictionary<string, string> AxisNames = new()
    {
        { "L0", "Stroke" },
        { "R0", "Twist" },
        { "R1", "Roll" },
        { "R2", "Pitch" },
    };

    /// <summary>
    /// Available window duration values in seconds.
    /// </summary>
    public static readonly int[] AvailableWindowDurations = [30, 60, 120, 300];

    /// <summary>
    /// Display labels corresponding to <see cref="AvailableWindowDurations"/>.
    /// </summary>
    public static readonly string[] WindowDurationLabels = ["30s", "1 min", "2 min", "5 min"];

    // ── Properties ───────────────────────────────────────────

    /// <summary>
    /// The active visualization mode (Graph or Heatmap).
    /// Persisted to settings.
    /// </summary>
    public VisualizationMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (Set(ref _selectedMode, value))
                _settings.Set("visualizerMode", value.ToString());
        }
    }

    /// <summary>
    /// Duration of the visible time window in seconds (30, 60, 120, or 300).
    /// Persisted to settings.
    /// </summary>
    public int WindowDurationSeconds
    {
        get => _windowDurationSeconds;
        set
        {
            if (Set(ref _windowDurationSeconds, value))
            {
                OnPropertyChanged(nameof(TimeWindowRadius));
                _settings.Set("visualizerWindowDuration", value.ToString());
            }
        }
    }

    /// <summary>
    /// Current playback position in seconds. Updated from position events.
    /// </summary>
    public double CurrentTime
    {
        get => _currentTime;
        set => Set(ref _currentTime, value);
    }

    /// <summary>
    /// Loaded funscript data keyed by axis ID (e.g. "L0", "R0").
    /// </summary>
    public Dictionary<string, FunscriptData> LoadedAxes
    {
        get => _loadedAxes;
        set
        {
            if (Set(ref _loadedAxes, value))
                OnPropertyChanged(nameof(HasScripts));
        }
    }

    /// <summary>
    /// True when at least one axis has loaded funscript data.
    /// </summary>
    public bool HasScripts => _loadedAxes.Count > 0;

    /// <summary>
    /// Half the window duration — defines the visible range around <see cref="CurrentTime"/>.
    /// </summary>
    public double TimeWindowRadius => _windowDurationSeconds / 2.0;

    /// <summary>
    /// Raised to request the visualizer view to repaint (e.g. on time or data changes).
    /// </summary>
    public event Action? RepaintRequested;

    // ── Constructor ──────────────────────────────────────────

    public VisualizerViewModel(IPluginSettingsStore settings)
    {
        _settings = settings;
        LoadSettings();
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>
    /// Updates the current playback time and requests a repaint.
    /// Called from the plugin's position-changed event handler.
    /// </summary>
    public void UpdateTime(double timeSeconds)
    {
        CurrentTime = timeSeconds;
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Replaces the loaded axes with new data and requests a repaint.
    /// Called when scripts are loaded for a new video.
    /// </summary>
    public void SetLoadedAxes(Dictionary<string, FunscriptData> axes)
    {
        LoadedAxes = axes ?? new Dictionary<string, FunscriptData>();
        RepaintRequested?.Invoke();
    }

    /// <summary>
    /// Clears all loaded axes and requests a repaint.
    /// Called when a video is unloaded.
    /// </summary>
    public void ClearAxes()
    {
        LoadedAxes = new Dictionary<string, FunscriptData>();
        RepaintRequested?.Invoke();
    }

    // ── Settings Persistence ─────────────────────────────────

    private void LoadSettings()
    {
        var modeStr = _settings.Get("visualizerMode", "Graph");
        if (Enum.TryParse<VisualizationMode>(modeStr, out var mode))
            _selectedMode = mode;

        var durationStr = _settings.Get("visualizerWindowDuration", "60");
        if (int.TryParse(durationStr, out var duration) &&
            Array.IndexOf(AvailableWindowDurations, duration) >= 0)
        {
            _windowDurationSeconds = duration;
        }
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
