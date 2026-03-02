using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Vido.Core.Plugin;
using Vido.Haptics;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for the beat bar overlay and control bar ComboBox.
/// Manages mode selection (Off/OnPeak/OnValley + external sources),
/// beat detection, current playback time, and settings persistence.
/// Supports dynamic registration of external beat sources via
/// <see cref="ExternalBeatSourceRegistration"/> events.
/// </summary>
public class BeatBarViewModel : INotifyPropertyChanged
{
    private readonly IPluginSettingsStore _settings;
    private readonly BeatDetectionService _beatDetection;

    private BeatBarMode _mode = BeatBarMode.Off;
    private double _currentTimeMs;
    private List<double> _beats = new();
    private FunscriptData? _currentScript;

    // External beat sources (registered by plugins via IEventBus)
    private readonly List<IExternalBeatSource> _externalSources = [];

    // External beats provided by plugins (keyed by source ID)
    private readonly List<double> _externalBeats = new();

    // Suppress settings save when loading from store or external change
    private bool _suppressSave;

    // Deferred external mode: the saved mode ID that wasn't resolvable at startup
    // because external sources register after the ViewModel is constructed.
    private string? _pendingExternalModeId;

    // Saved built-in mode before switching to an external source, so we can
    // restore it when the external source goes away (e.g. Pulse disabled).
    private BeatBarMode? _preExternalMode;

    // Saved external mode ID so we can restore the correct external mode
    // when external sources re-register (e.g. Pulse toggled off then on).
    private string? _savedExternalModeId;

    // ── Properties ───────────────────────────────────────────

    /// <summary>
    /// Available modes for the ComboBox. Rebuilt when external sources register/unregister.
    /// </summary>
    public ObservableCollection<BeatBarMode> AvailableModes { get; } = new(BeatBarMode.BuiltInModes);

    /// <summary>
    /// The active beat bar mode. Bound to the control bar ComboBox.
    /// Persisted to settings.
    /// </summary>
    public BeatBarMode Mode
    {
        get => _mode;
        set
        {
            if (value == null!) return;
            var previousMode = _mode;
            if (Set(ref _mode, value))
            {
                if (!_suppressSave)
                    _settings.Set("beatBarMode", value.ToString());

                // Save the built-in mode before switching to an external source
                // so we can restore it when the external source goes away.
                // Don't overwrite if already saved (e.g. from RebuildAvailableModes
                // hiding built-in modes before the external mode is selected).
                if (value.IsExternal && !previousMode.IsExternal && _preExternalMode == null)
                    _preExternalMode = previousMode;
                else if (!value.IsExternal)
                    _preExternalMode = null;

                // Track the last-selected external mode so we restore the
                // correct one when external sources re-register.
                // Clear when the user explicitly switches to Off from an
                // external mode — they don't want auto-reselection.
                if (value.IsExternal)
                    _savedExternalModeId = value.Id;
                else if (previousMode.IsExternal && value == BeatBarMode.Off)
                    _savedExternalModeId = null;

                if (!value.IsExternal)
                    RedetectBeats();

                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsExternalMode));
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
    /// True when the current mode is an external source mode.
    /// </summary>
    public bool IsExternalMode => _mode.IsExternal;

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
    /// For external modes, these are the externally provided beats.
    /// For built-in modes, these are detected from the funscript.
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

    /// <summary>
    /// Returns the registered external beat source matching the current mode, or null.
    /// Used by the overlay to delegate rendering.
    /// </summary>
    public IExternalBeatSource? ActiveExternalSource
    {
        get
        {
            if (!_mode.IsExternal) return null;
            return _externalSources.FirstOrDefault(s => s.Id == _mode.Id);
        }
    }

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
        if (!_mode.IsExternal)
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
        _externalBeats.Clear();
        Beats = _externalBeats;
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

    // ── External Beat Source Management ──────────────────────

    /// <summary>
    /// Handles <see cref="ExternalBeatSourceRegistration"/> events from the event bus.
    /// Adds or removes external beat sources and rebuilds the available modes list.
    /// </summary>
    public void OnBeatSourceRegistration(ExternalBeatSourceRegistration registration)
    {
        if (registration.Source is not { } source)
            return;

        if (registration.IsRegistering)
        {
            // Remove any existing source with the same ID first, then add
            _externalSources.RemoveAll(s => s.Id == source.Id);
            _externalSources.Add(source);
        }
        else
        {
            _externalSources.RemoveAll(s => s.Id == source.Id);
        }

        RebuildAvailableModes();
    }

    /// <summary>
    /// Handles <see cref="ExternalBeatEvent"/> from the event bus.
    /// Updates the beat list when the current mode is an external source matching the event.
    /// </summary>
    public void OnExternalBeatEvent(ExternalBeatEvent beatEvent)
    {
        if (_mode.IsExternal && _mode.Id == beatEvent.SourceId)
        {
            CopyBeatTimesToExternalBeats(beatEvent.BeatTimesMs.Span);
            Beats = _externalBeats;
            OnPropertyChanged(nameof(IsActive));
            RepaintRequested?.Invoke();
        }
        else
        {
            // Cache external beats even if not the current mode, so when user switches
            // to the external mode, beats are immediately available
            if (HasExternalSource(beatEvent.SourceId))
            {
                CopyBeatTimesToExternalBeats(beatEvent.BeatTimesMs.Span);
            }
        }
    }

    /// <summary>
    /// Rebuilds the <see cref="AvailableModes"/> collection based on current external sources.
    /// Hides built-in modes when an active external source requests it.
    /// </summary>
    internal void RebuildAvailableModes()
    {
        AvailableModes.Clear();
        AvailableModes.Add(BeatBarMode.Off);

        var hideBuiltIn = _externalSources.Any(s => s.IsAvailable && s.HidesBuiltInModes);

        if (!hideBuiltIn)
        {
            AvailableModes.Add(BeatBarMode.OnPeak);
            AvailableModes.Add(BeatBarMode.OnValley);
        }

        foreach (var source in _externalSources.Where(s => s.IsAvailable))
            AvailableModes.Add(BeatBarMode.CreateExternal(source.Id, source.DisplayName));

        // If current mode was removed, restore the pre-external built-in mode
        // (or fall back to Off). Otherwise, replace _mode with the new instance
        // from the rebuilt collection so the WPF ComboBox's SelectedItem matches.
        var matchingMode = AvailableModes.FirstOrDefault(m => m == _mode);
        if (matchingMode is null)
        {
            // If the current mode is a built-in mode being hidden because an
            // external source is taking over, save it so we can restore later.
            if (!_mode.IsExternal && _mode != BeatBarMode.Off && _preExternalMode == null)
                _preExternalMode = _mode;

            // Try restoring the saved built-in mode from before external activation
            var restore = _preExternalMode != null
                ? AvailableModes.FirstOrDefault(m => m == _preExternalMode)
                : null;
            if (restore != null)
            {
                _preExternalMode = null;
                Mode = restore;
            }
            else
            {
                // Fall back to Off without clearing _preExternalMode —
                // the saved mode should persist until the external source
                // goes away and built-in modes are available again.
                _mode = BeatBarMode.Off;
                _settings.Set("beatBarMode", _mode.ToString());
                RedetectBeats();
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsExternalMode));
                ModeChanged?.Invoke(_mode);
                RepaintRequested?.Invoke();

                // When an external source hides built-in modes, auto-select
                // the previously-selected external mode (or the first available).
                if (hideBuiltIn)
                {
                    var autoSelect = _savedExternalModeId != null
                        ? AvailableModes.FirstOrDefault(m => m.Id == _savedExternalModeId)
                        : null;
                    if (autoSelect != null)
                    {
                        Mode = autoSelect;
                    }
                    else
                    {
                        // Preferred mode not available yet; fall back to first
                        // external but preserve the saved ID for later.
                        var savedId = _savedExternalModeId;
                        autoSelect = AvailableModes.FirstOrDefault(m => m.IsExternal);
                        if (autoSelect != null)
                            Mode = autoSelect;
                        _savedExternalModeId = savedId;
                    }
                }
            }
        }
        else
        {
            _mode = matchingMode;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsExternalMode));

            // If an external source hides built-in modes and a preferred
            // external mode just became available (but we're on a different
            // external from a fallback auto-select), switch to the preferred one.
            if (hideBuiltIn && _savedExternalModeId != null && _mode.Id != _savedExternalModeId)
            {
                var preferred = AvailableModes.FirstOrDefault(m => m.Id == _savedExternalModeId);
                if (preferred != null)
                    Mode = preferred;
            }
        }

        // Auto-select a deferred external mode that was persisted from a previous session
        if (_pendingExternalModeId != null)
        {
            var pending = AvailableModes.FirstOrDefault(m => m.Id == _pendingExternalModeId);
            if (pending != null)
            {
                _pendingExternalModeId = null;
                Mode = pending;
            }
        }
    }

    // ── Settings Persistence ─────────────────────────────────

    private void LoadSettings()
    {
        var modeStr = _settings.Get("beatBarMode", "Off");
        var resolved = BeatBarMode.BuiltInModes.FirstOrDefault(m => m.Id == modeStr);
        if (resolved != null)
        {
            _suppressSave = true;
            _mode = resolved;
            _suppressSave = false;
        }
        else
        {
            // Not a built-in mode — remember it so we can auto-select
            // when the external source registers later.
            _pendingExternalModeId = modeStr;
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
        var resolved = AvailableModes.FirstOrDefault(m => m.Id == modeStr);
        if (resolved != null && resolved != _mode)
        {
            _suppressSave = true;
            Mode = resolved;
            _suppressSave = false;
        }
    }

    // ── Private Helpers ──────────────────────────────────────

    /// <summary>
    /// Re-runs beat detection using the current mode and stored script data.
    /// Only called for built-in modes (not external).
    /// </summary>
    private void RedetectBeats()
    {
        if (_mode == BeatBarMode.Off)
        {
            Beats = new List<double>();
        }
        else if (_mode == BeatBarMode.OnPeak)
        {
            Beats = _beatDetection.DetectBeats(_currentScript, BeatDetectionMode.OnPeak);
        }
        else if (_mode == BeatBarMode.OnValley)
        {
            Beats = _beatDetection.DetectBeats(_currentScript, BeatDetectionMode.OnValley);
        }
        RepaintRequested?.Invoke();
    }

    private void CopyBeatTimesToExternalBeats(ReadOnlySpan<double> beatTimesMs)
    {
        _externalBeats.Clear();
        if (_externalBeats.Capacity < beatTimesMs.Length)
            _externalBeats.Capacity = beatTimesMs.Length;

        for (int index = 0; index < beatTimesMs.Length; index++)
            _externalBeats.Add(beatTimesMs[index]);
    }

    private bool HasExternalSource(string sourceId)
    {
        for (int index = 0; index < _externalSources.Count; index++)
        {
            if (_externalSources[index].Id == sourceId)
                return true;
        }

        return false;
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
