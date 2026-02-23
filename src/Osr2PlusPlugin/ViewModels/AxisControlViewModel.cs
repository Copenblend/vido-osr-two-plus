using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    /// <summary>The four axis cards: L0, R0, R1, R2.</summary>
    public ObservableCollection<AxisCardViewModel> AxisCards { get; }

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
    }

    /// <summary>
    /// Force-clears ALL scripts including manual overrides.
    /// </summary>
    public void ClearAllScripts()
    {
        foreach (var card in AxisCards)
            card.ClearAllScripts();

        _tcode.SetScripts(new Dictionary<string, FunscriptData>());
    }

    // ═══════════════════════════════════════════════════════
    //  State Updates (called by plugin entry point)
    // ═══════════════════════════════════════════════════════

    /// <summary>Updates video playing state for all cards (disables test buttons).</summary>
    public void SetVideoPlaying(bool playing)
    {
        foreach (var card in AxisCards)
            card.SetVideoPlaying(playing);

        // Stop all test axes when video starts playing
        if (playing)
            _tcode.StopAllTestAxes();
    }

    /// <summary>Updates device connection state for all cards.</summary>
    public void SetDeviceConnected(bool connected)
    {
        foreach (var card in AxisCards)
            card.SetDeviceConnected(connected);
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
        }
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
    //  INotifyPropertyChanged
    // ═══════════════════════════════════════════════════════

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
