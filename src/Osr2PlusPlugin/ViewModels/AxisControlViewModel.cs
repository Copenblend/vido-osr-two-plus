using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Vido.Core.Plugin;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for the axis control panel. Manages all four axis cards,
/// persists axis settings, and orchestrates funscript auto-loading.
/// </summary>
public class AxisControlViewModel : INotifyPropertyChanged
{
    private readonly TCodeService _tcode;
    private readonly IPluginSettingsStore _settings;
    private readonly FunscriptParser _parser;
    private readonly FunscriptMatcher _matcher;
    private readonly List<AxisConfig> _configs;
    private bool _isVideoPlaying;
    private bool _isDeviceConnected;
    private bool _isTesting;

    /// <summary>The four axis cards: L0, R0, R1, R2.</summary>
    public ObservableCollection<AxisCardViewModel> AxisCards { get; }

    /// <summary>
    /// Raised when loaded scripts change (load, clear, or manual override).
    /// Carries the current set of loaded scripts.
    /// </summary>
    public event Action<Dictionary<string, FunscriptData>>? ScriptsChanged;

    /// <summary>Whether test mode is currently active.</summary>
    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (_isTesting != value)
            {
                _isTesting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TestButtonText));
            }
        }
    }

    /// <summary>Test button display text.</summary>
    public string TestButtonText => IsTesting ? "Stop" : "Test";

    /// <summary>Whether the test button is enabled.</summary>
    public bool IsTestEnabled => _isDeviceConnected && !_isVideoPlaying;

    /// <summary>Toggles test mode for all configured axes.</summary>
    public ICommand TestCommand { get; }

    /// <summary>
    /// Injectable delegates for testing. Defaults use real implementations.
    /// </summary>
    internal Func<string, Dictionary<string, string>>? FindMatchingScriptsFunc { get; set; }
    internal Func<string, Dictionary<string, FunscriptData>?>? TryParseMultiAxisFunc { get; set; }
    internal Func<string, string, FunscriptData>? ParseFileFunc { get; set; }

    public AxisControlViewModel(
        TCodeService tcode,
        IPluginSettingsStore settings,
        FunscriptParser parser,
        FunscriptMatcher matcher)
    {
        _tcode = tcode;
        _settings = settings;
        _parser = parser;
        _matcher = matcher;

        // Set up default delegates (can be overridden for testing)
        FindMatchingScriptsFunc = _matcher.FindMatchingScripts;
        TryParseMultiAxisFunc = _parser.TryParseMultiAxis;
        ParseFileFunc = _parser.ParseFile;

        // Test command
        TestCommand = new RelayCommand(ExecuteTest);
        _tcode.AllTestsStopped += OnAllTestsStopped;

        // Create axis configs from defaults, then load persisted settings
        _configs = AxisConfig.CreateDefaults();
        LoadSettings();

        // Push configs to TCodeService
        _tcode.SetAxisConfigs(_configs);

        // Create axis card ViewModels
        AxisCards = new ObservableCollection<AxisCardViewModel>();
        foreach (var config in _configs)
        {
            var card = new AxisCardViewModel(config, _tcode);
            card.ParseFileFunc = ParseFileFunc;
            card.ConfigChanged += OnCardConfigChanged;
            AxisCards.Add(card);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Script Loading
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Orchestrates funscript auto-loading for a video file.
    /// 1. Tries multi-axis format on the base funscript.
    /// 2. Falls back to individual axis-tagged files via FunscriptMatcher.
    /// 3. Updates each card's script and pushes to TCodeService.
    /// </summary>
    public void LoadScriptsForVideo(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath))
            return;

        // Find matching individual scripts
        var matchedScripts = FindMatchingScriptsFunc!(videoPath);

        // Try multi-axis on the base funscript (L0) first
        Dictionary<string, FunscriptData>? multiAxisData = null;
        if (matchedScripts.TryGetValue("L0", out var basePath))
        {
            multiAxisData = TryParseMultiAxisFunc!(basePath);
        }

        var loadedScripts = new Dictionary<string, FunscriptData>();

        foreach (var card in AxisCards)
        {
            // Skip manual overrides
            if (card.IsScriptManual)
            {
                // Keep existing manual script in loadedScripts if present
                if (card.ScriptFileName != null)
                {
                    try
                    {
                        var data = ParseFileFunc!(card.ScriptFileName, card.AxisId);
                        loadedScripts[card.AxisId] = data;
                    }
                    catch { /* manual file missing — ignore */ }
                }
                continue;
            }

            // Try multi-axis data first
            if (multiAxisData != null && multiAxisData.TryGetValue(card.AxisId, out var multiData))
            {
                card.SetAutoLoadedScript(basePath);
                loadedScripts[card.AxisId] = multiData;
                continue;
            }

            // Fall back to individual axis file
            if (matchedScripts.TryGetValue(card.AxisId, out var axisPath))
            {
                try
                {
                    var data = ParseFileFunc!(axisPath, card.AxisId);
                    card.SetAutoLoadedScript(axisPath);
                    loadedScripts[card.AxisId] = data;
                }
                catch { /* parse error — skip axis */ }
            }
            else
            {
                card.ClearAutoLoadedScript();
            }
        }

        // Push all loaded scripts to TCodeService
        _tcode.SetScripts(loadedScripts);
        ScriptsChanged?.Invoke(loadedScripts);
    }

    /// <summary>
    /// Clears all auto-loaded scripts (respects manual overrides).
    /// </summary>
    public void ClearScripts()
    {
        var remaining = new Dictionary<string, FunscriptData>();

        foreach (var card in AxisCards)
        {
            if (card.IsScriptManual && card.ScriptFileName != null)
            {
                // Keep manual scripts
                try
                {
                    var data = ParseFileFunc!(card.ScriptFileName, card.AxisId);
                    remaining[card.AxisId] = data;
                }
                catch { /* manual file missing */ }
            }
            else
            {
                card.ClearAutoLoadedScript();
            }
        }

        _tcode.SetScripts(remaining);
        ScriptsChanged?.Invoke(remaining);
    }

    /// <summary>
    /// Force-clears ALL scripts including manual overrides.
    /// </summary>
    public void ClearAllScripts()
    {
        foreach (var card in AxisCards)
            card.ClearAllScripts();

        var empty = new Dictionary<string, FunscriptData>();
        _tcode.SetScripts(empty);
        ScriptsChanged?.Invoke(empty);
    }

    // ═══════════════════════════════════════════════════════
    //  State Updates (called by plugin entry point)
    // ═══════════════════════════════════════════════════════

    /// <summary>Updates video playing state. Disables test, stops test axes when playing.</summary>
    public void SetVideoPlaying(bool playing)
    {
        if (_isVideoPlaying != playing)
        {
            _isVideoPlaying = playing;
            OnPropertyChanged(nameof(IsTestEnabled));
        }

        // Stop all test axes when video starts playing
        if (playing)
        {
            _tcode.StopAllTestAxes();
            IsTesting = false;
        }
    }

    /// <summary>Updates device connection state.</summary>
    public void SetDeviceConnected(bool connected)
    {
        if (_isDeviceConnected != connected)
        {
            _isDeviceConnected = connected;
            OnPropertyChanged(nameof(IsTestEnabled));
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Settings Persistence
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Loads persisted axis settings from the settings store.
    /// Called during construction.
    /// </summary>
    internal void LoadSettings()
    {
        foreach (var config in _configs)
        {
            var prefix = $"axis_{config.Id}_";
            config.Min = _settings.Get($"{prefix}min", config.Min);
            config.Max = _settings.Get($"{prefix}max", config.Max);
            config.Enabled = _settings.Get($"{prefix}enabled", config.Enabled);

            var fillStr = _settings.Get($"{prefix}fillMode", config.FillMode.ToString());
            if (Enum.TryParse<AxisFillMode>(fillStr, out var fillMode))
                config.FillMode = fillMode;

            config.SyncWithStroke = _settings.Get($"{prefix}syncWithStroke", config.SyncWithStroke);
            config.FillSpeedHz = _settings.Get($"{prefix}fillSpeedHz", config.FillSpeedHz);
            config.PositionOffset = _settings.Get($"{prefix}positionOffset", config.PositionOffset);
        }
    }

    /// <summary>
    /// Saves all axis settings to the settings store.
    /// Called whenever a card's config changes.
    /// </summary>
    internal void SaveSettings()
    {
        foreach (var config in _configs)
        {
            var prefix = $"axis_{config.Id}_";
            _settings.Set($"{prefix}min", config.Min);
            _settings.Set($"{prefix}max", config.Max);
            _settings.Set($"{prefix}enabled", config.Enabled);
            _settings.Set($"{prefix}fillMode", config.FillMode.ToString());
            _settings.Set($"{prefix}syncWithStroke", config.SyncWithStroke);
            _settings.Set($"{prefix}fillSpeedHz", config.FillSpeedHz);
            _settings.Set($"{prefix}positionOffset", config.PositionOffset);
        }
    }

    /// <summary>
    /// Handles external setting changes (e.g. from the host Settings Panel).
    /// Re-reads changed axis keys and updates configs + cards without re-saving.
    /// </summary>
    internal void OnSettingChanged(string key)
    {
        if (!key.StartsWith("axis_")) return;

        // Parse "axis_{id}_{prop}" format
        var parts = key.Split('_', 3);
        if (parts.Length < 3) return;

        var axisId = parts[1];
        var prop = parts[2];
        var config = _configs.FirstOrDefault(c => c.Id == axisId);
        if (config == null) return;

        var prefix = $"axis_{axisId}_";

        switch (prop)
        {
            case "min":
                config.Min = _settings.Get($"{prefix}min", config.Min);
                break;
            case "max":
                config.Max = _settings.Get($"{prefix}max", config.Max);
                break;
            case "enabled":
                config.Enabled = _settings.Get($"{prefix}enabled", config.Enabled);
                break;
            case "fillMode":
                var fillStr = _settings.Get($"{prefix}fillMode", config.FillMode.ToString());
                if (Enum.TryParse<AxisFillMode>(fillStr, out var fillMode))
                    config.FillMode = fillMode;
                break;
            case "syncWithStroke":
                config.SyncWithStroke = _settings.Get($"{prefix}syncWithStroke", config.SyncWithStroke);
                break;
            case "fillSpeedHz":
                config.FillSpeedHz = _settings.Get($"{prefix}fillSpeedHz", config.FillSpeedHz);
                break;
            case "positionOffset":
                config.PositionOffset = _settings.Get($"{prefix}positionOffset", config.PositionOffset);
                break;
            default:
                return;
        }

        // Push updated configs to TCodeService
        _tcode.SetAxisConfigs(_configs);

        // Refresh the matching AxisCardViewModel
        var card = AxisCards.FirstOrDefault(c => c.AxisId == axisId);
        card?.RefreshFromConfig();
    }

    // ═══════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════

    private void OnCardConfigChanged()
    {
        // Re-push configs to TCodeService and persist
        _tcode.SetAxisConfigs(_configs);
        SaveSettings();
    }

    // ═══════════════════════════════════════════════════════
    //  Test Mode
    // ═══════════════════════════════════════════════════════

    private void ExecuteTest()
    {
        if (IsTesting)
        {
            _tcode.StopAllTestAxes();
            IsTesting = false;
        }
        else
        {
            if (!IsTestEnabled) return;

            // Start test on each enabled axis that is testable
            foreach (var config in _configs)
            {
                if (!config.Enabled) continue;

                // L0 always gets Triangle at its FillSpeedHz (default 1.0)
                if (config.IsStroke)
                {
                    _tcode.StartTestAxis(config.Id, config.FillSpeedHz);
                    continue;
                }

                // Non-stroke axes: only test if fill mode is set
                if (config.FillMode != AxisFillMode.None)
                {
                    _tcode.StartTestAxis(config.Id, config.FillSpeedHz);
                }
            }

            IsTesting = true;
        }
    }

    private void OnAllTestsStopped()
    {
        IsTesting = false;
    }

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
