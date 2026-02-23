using System.IO;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Orchestrates funscript loading/unloading when videos are loaded.
/// Tries multi-axis format first, then falls back to individual file matching.
/// Supports manual overrides that persist across auto-loads.
/// </summary>
public class FunscriptLoadingService
{
    private readonly FunscriptParser _parser;
    private readonly FunscriptMatcher _matcher;

    /// <summary>
    /// Currently loaded scripts per axis (axisId → FunscriptData).
    /// </summary>
    private readonly Dictionary<string, FunscriptData> _loadedScripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Manual overrides per axis (axisId → filePath). These persist across auto-loads
    /// until explicitly cleared.
    /// </summary>
    private readonly Dictionary<string, string> _manualOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The video path that scripts are currently loaded for.
    /// </summary>
    public string? CurrentVideoPath { get; private set; }

    /// <summary>
    /// Fired when the loaded scripts change (load, unload, or override).
    /// The dictionary maps axisId → FunscriptData for all loaded axes.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, FunscriptData>>? ScriptsChanged;

    public FunscriptLoadingService(FunscriptParser parser, FunscriptMatcher matcher)
    {
        _parser = parser;
        _matcher = matcher;
    }

    /// <summary>
    /// Gets the currently loaded scripts (axisId → FunscriptData).
    /// </summary>
    public IReadOnlyDictionary<string, FunscriptData> LoadedScripts => _loadedScripts;

    /// <summary>
    /// Loads funscripts for the given video. Tries multi-axis format first,
    /// then falls back to individual file matching. Manual overrides are applied last.
    /// </summary>
    /// <param name="videoPath">Full path to the video file.</param>
    /// <returns>List of log messages describing what was loaded.</returns>
    public List<string> LoadScriptsForVideo(string videoPath)
    {
        var logs = new List<string>();
        _loadedScripts.Clear();
        CurrentVideoPath = videoPath;

        if (string.IsNullOrEmpty(videoPath))
        {
            logs.Add("No video path provided, skipping script load");
            ScriptsChanged?.Invoke(_loadedScripts);
            return logs;
        }

        // Step 1: Try multi-axis format on the base .funscript file
        var baseFunscript = Path.ChangeExtension(videoPath, ".funscript");
        bool multiAxisLoaded = false;

        if (File.Exists(baseFunscript))
        {
            var multiAxis = _parser.TryParseMultiAxis(baseFunscript);
            if (multiAxis != null)
            {
                foreach (var (axisId, data) in multiAxis)
                {
                    _loadedScripts[axisId] = data;
                    logs.Add($"Multi-axis: loaded {axisId} from {Path.GetFileName(baseFunscript)} ({data.Actions.Count} actions)");
                }
                multiAxisLoaded = true;
            }
        }

        // Step 2: Fall back to individual file matching if multi-axis didn't load
        if (!multiAxisLoaded)
        {
            var matched = _matcher.FindMatchingScripts(videoPath);
            foreach (var (axisId, filePath) in matched)
            {
                var data = _parser.ParseFile(filePath, axisId);
                if (data.Actions.Count > 0)
                {
                    _loadedScripts[axisId] = data;
                    logs.Add($"Auto-matched: {axisId} ← {Path.GetFileName(filePath)} ({data.Actions.Count} actions)");
                }
            }
        }

        // Step 3: Apply manual overrides (these take precedence)
        foreach (var (axisId, filePath) in _manualOverrides)
        {
            if (File.Exists(filePath))
            {
                var data = _parser.ParseFile(filePath, axisId);
                if (data.Actions.Count > 0)
                {
                    _loadedScripts[axisId] = data;
                    logs.Add($"Manual override: {axisId} ← {Path.GetFileName(filePath)} ({data.Actions.Count} actions)");
                }
            }
            else
            {
                logs.Add($"Manual override: {axisId} file not found — {filePath}");
            }
        }

        if (_loadedScripts.Count == 0)
        {
            logs.Add("No funscript files found for this video");
        }

        ScriptsChanged?.Invoke(_loadedScripts);
        return logs;
    }

    /// <summary>
    /// Clears all loaded scripts and resets current video path.
    /// Does not clear manual overrides.
    /// </summary>
    /// <returns>Log messages.</returns>
    public List<string> ClearScripts()
    {
        var logs = new List<string>();

        if (_loadedScripts.Count > 0)
        {
            logs.Add($"Cleared {_loadedScripts.Count} loaded script(s)");
        }

        _loadedScripts.Clear();
        CurrentVideoPath = null;

        ScriptsChanged?.Invoke(_loadedScripts);
        return logs;
    }

    /// <summary>
    /// Sets a manual override for an axis. This persists across auto-loads
    /// until explicitly cleared.
    /// </summary>
    /// <param name="axisId">The axis to override (e.g. "L0", "R0").</param>
    /// <param name="filePath">Full path to the funscript file.</param>
    public void SetManualOverride(string axisId, string filePath)
    {
        _manualOverrides[axisId] = filePath;

        // If we have a current video, re-apply the override immediately
        if (CurrentVideoPath != null && File.Exists(filePath))
        {
            var data = _parser.ParseFile(filePath, axisId);
            if (data.Actions.Count > 0)
            {
                _loadedScripts[axisId] = data;
                ScriptsChanged?.Invoke(_loadedScripts);
            }
        }
    }

    /// <summary>
    /// Clears a manual override for an axis.
    /// </summary>
    /// <param name="axisId">The axis to clear the override for.</param>
    public void ClearManualOverride(string axisId)
    {
        _manualOverrides.Remove(axisId);
    }

    /// <summary>
    /// Clears all manual overrides.
    /// </summary>
    public void ClearAllManualOverrides()
    {
        _manualOverrides.Clear();
    }

    /// <summary>
    /// Whether any manual overrides are set.
    /// </summary>
    public bool HasManualOverrides => _manualOverrides.Count > 0;

    /// <summary>
    /// Gets the current manual overrides (axisId → filePath).
    /// </summary>
    public IReadOnlyDictionary<string, string> ManualOverrides => _manualOverrides;
}
