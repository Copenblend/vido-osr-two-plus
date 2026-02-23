using System.Diagnostics;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Generates and sends TCode commands to the connected device at a configurable output rate.
/// Uses a dedicated background thread with Stopwatch-based precise timing and time extrapolation
/// to produce smooth, in-sync TCode output independent of the UI thread refresh rate.
/// </summary>
public class TCodeService : IDisposable
{
    private readonly InterpolationService _interpolation;

    // Transport
    private ITransportService? _transport;

    // Output thread
    private Thread? _outputThread;
    private volatile bool _threadRunning;

    // Playback state (written by UI thread, read by output thread)
    private volatile bool _isPlaying;
    private volatile float _playbackSpeed = 1.0f;
    private int _outputRateHz = 100;

    // Time extrapolation: the UI thread periodically sets the "sync point".
    // The output thread uses Stopwatch to extrapolate actual time between sync points.
    private readonly object _timeLock = new();
    private double _syncTimeMs;           // media time at last sync point
    private long _syncTicks;              // Stopwatch ticks at last sync point
    private bool _syncPlaying;            // whether media was playing at sync point

    // Axis data
    private Dictionary<string, FunscriptData> _scripts = new();
    private List<AxisConfig> _axisConfigs = new();
    private int _offsetMs;

    // Dirty value tracking: only send axes whose TCode value changed
    private readonly Dictionary<string, int> _lastSentValues = new();

    // ===== Fill Mode State =====

    // Random pattern generators — one per axis
    private readonly Dictionary<string, RandomPatternGenerator> _randomGenerators = new();

    // Stroke tracking for grind/random synchronization
    private double _lastStrokePosition = 50.0;
    private double _cumulativeStrokeDistance;

    // Per-axis cumulative fill time (for independent pattern fill)
    private readonly Dictionary<string, double> _cumulativeFillTime = new();

    // Return-to-center: axis ID → current interpolated TCode position
    private readonly Dictionary<string, double> _returningAxes = new();

    // Ramp-up: axis ID → blend factor (0.0 = midpoint, 1.0 = fully active)
    private readonly Dictionary<string, double> _rampingAxes = new();

    // Previous axis state snapshot for detecting transitions
    private readonly Dictionary<string, (bool Enabled, AxisFillMode FillMode)> _prevAxisState = new();

    /// <summary>Current output rate in Hz.</summary>
    public int OutputRateHz => _outputRateHz;

    /// <summary>Whether any axis has funscript data loaded.</summary>
    public bool HasScriptsLoaded => _scripts.Count > 0;

    /// <summary>Whether funscript is actively playing (blocks test mode).</summary>
    public bool IsFunscriptPlaying => _isPlaying && _scripts.Count > 0;

    /// <summary>
    /// The active transport (Serial or UDP). Set by the connection logic on connect.
    /// </summary>
    public ITransportService? Transport
    {
        get => _transport;
        set => _transport = value;
    }

    public TCodeService(InterpolationService interpolation)
    {
        _interpolation = interpolation;
        _syncTicks = Stopwatch.GetTimestamp();
    }

    // ===== Public API — thread-safe, called from UI thread =====

    /// <summary>
    /// Set the loaded funscript data for all axes.
    /// </summary>
    public void SetScripts(Dictionary<string, FunscriptData> scripts)
    {
        _scripts = scripts;
        _lastSentValues.Clear();
        _interpolation.ResetIndices();
        // Reset stroke tracking and random generators on script change
        _cumulativeStrokeDistance = 0;
        _lastStrokePosition = 50.0;
        _cumulativeFillTime.Clear();
        foreach (var gen in _randomGenerators.Values) gen.Reset();
    }

    /// <summary>
    /// Set the axis configurations (min/max/enabled/fill mode).
    /// Detects transitions to trigger ramp-up and return-to-center animations.
    /// </summary>
    public void SetAxisConfigs(List<AxisConfig> configs)
    {
        foreach (var cfg in configs)
        {
            bool hasPrev = _prevAxisState.TryGetValue(cfg.Id, out var prev);
            if (hasPrev)
            {
                bool wasEnabled = prev.Enabled;
                bool wasActiveFill = prev.Enabled && prev.FillMode != AxisFillMode.None;
                bool nowEnabled = cfg.Enabled;
                bool nowActiveFill = cfg.Enabled && cfg.FillMode != AxisFillMode.None;

                // Return-to-center when: axis disabled, OR fill mode set to None
                bool justDisabled = wasEnabled && !nowEnabled;
                bool fillJustCleared = wasActiveFill && nowEnabled && cfg.FillMode == AxisFillMode.None;
                if (justDisabled || fillJustCleared)
                {
                    if (_lastSentValues.TryGetValue(cfg.Id, out var lastVal) && Math.Abs(lastVal - 500) >= 1)
                    {
                        _returningAxes[cfg.Id] = lastVal;
                    }
                    _rampingAxes.Remove(cfg.Id);
                }

                // Ramp-up when: axis activated with active fill from inactive state
                bool justActivated = nowActiveFill && (!wasActiveFill || !wasEnabled);
                if (justActivated)
                {
                    _returningAxes.Remove(cfg.Id);
                    _rampingAxes[cfg.Id] = 0.0;
                }
            }

            _prevAxisState[cfg.Id] = (cfg.Enabled, cfg.FillMode);
        }
        _axisConfigs = configs;
    }

    /// <summary>
    /// Set the TCode output rate in Hz (30–200).
    /// </summary>
    public void SetOutputRate(int hz)
    {
        _outputRateHz = Math.Clamp(hz, 30, 200);
    }

    /// <summary>
    /// Called from the UI thread with the media player's current time (~60 Hz).
    /// </summary>
    public void SetTime(double timeMs)
    {
        lock (_timeLock)
        {
            _syncTimeMs = timeMs;
            _syncTicks = Stopwatch.GetTimestamp();
            _syncPlaying = _isPlaying;
        }
    }

    /// <summary>
    /// Set playback state. Re-anchors the sync point to preserve continuity.
    /// </summary>
    public void SetPlaying(bool playing)
    {
        lock (_timeLock)
        {
            _syncTimeMs = GetExtrapolatedTimeMsLocked();
            _syncTicks = Stopwatch.GetTimestamp();
            _syncPlaying = playing;
        }
        _isPlaying = playing;
    }

    /// <summary>
    /// Set the playback speed multiplier. Re-anchors the sync point.
    /// </summary>
    public void SetPlaybackSpeed(float speed)
    {
        lock (_timeLock)
        {
            _syncTimeMs = GetExtrapolatedTimeMsLocked();
            _syncTicks = Stopwatch.GetTimestamp();
        }
        _playbackSpeed = speed;
    }

    /// <summary>
    /// Set the funscript offset in milliseconds. Positive = script plays later, negative = earlier.
    /// </summary>
    public void SetOffset(int offsetMs)
    {
        _offsetMs = offsetMs;
    }

    // ===== Thread Lifecycle =====

    /// <summary>
    /// Start the TCode output thread.
    /// </summary>
    public void Start()
    {
        if (_outputThread != null) return;

        _threadRunning = true;
        _outputThread = new Thread(OutputLoop)
        {
            IsBackground = true,
            Name = "TCodeOutput",
            Priority = ThreadPriority.AboveNormal
        };
        _outputThread.Start();
    }

    /// <summary>
    /// Stop the TCode output thread and clear state.
    /// </summary>
    public void StopTimer()
    {
        _threadRunning = false;
        _outputThread?.Join(500);
        _outputThread = null;
        _lastSentValues.Clear();
    }

    public void Dispose()
    {
        StopTimer();
        GC.SuppressFinalize(this);
    }

    // ===== Time Extrapolation =====

    /// <summary>
    /// Returns the extrapolated media time in milliseconds.
    /// Must be called while holding _timeLock.
    /// </summary>
    private double GetExtrapolatedTimeMsLocked()
    {
        if (!_syncPlaying) return _syncTimeMs;
        var elapsedTicks = Stopwatch.GetTimestamp() - _syncTicks;
        var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
        return _syncTimeMs + elapsedMs * _playbackSpeed;
    }

    /// <summary>
    /// Returns the extrapolated media time in milliseconds (thread-safe).
    /// </summary>
    internal double GetExtrapolatedTimeMs()
    {
        lock (_timeLock) return GetExtrapolatedTimeMsLocked();
    }

    // ===== Output Loop =====

    private void OutputLoop()
    {
        var stopwatch = Stopwatch.StartNew();

        while (_threadRunning)
        {
            var targetIntervalMs = 1000.0 / _outputRateHz;

            var elapsedSec = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            stopwatch.Restart();

            try
            {
                if (_transport?.IsConnected == true)
                {
                    bool hasFillOrReturn = HasActiveFillModes();
                    if (_isPlaying || hasFillOrReturn)
                    {
                        OutputTick(elapsedSec);
                    }
                }
            }
            catch
            {
                // Swallow to keep the output thread alive; next tick may succeed
            }

            SleepPrecise(stopwatch, targetIntervalMs);
        }
    }

    /// <summary>
    /// Precise sleep using Stopwatch + SpinWait for sub-2ms accuracy.
    /// Uses Thread.Sleep for longer waits to reduce CPU usage, then SpinWait for the final stretch.
    /// </summary>
    internal static void SleepPrecise(Stopwatch stopwatch, double millisecondsTimeout)
    {
        var spinner = new SpinWait();
        var frequencyInverse = 1.0 / Stopwatch.Frequency;

        while (true)
        {
            var elapsedMs = stopwatch.ElapsedTicks * frequencyInverse * 1000.0;
            var remaining = millisecondsTimeout - elapsedMs;

            if (remaining <= 0) break;

            if (remaining <= 2)
                spinner.SpinOnce(-1);
            else if (remaining < 5)
                Thread.Sleep(1);
            else if (remaining < 15)
                Thread.Sleep(5);
            else
                Thread.Sleep(10);
        }
    }

    // ===== Output Tick =====

    private void OutputTick(double elapsedSec)
    {
        var rawTimeMs = GetExtrapolatedTimeMs();
        var currentTimeMs = rawTimeMs - _offsetMs;

        // Interval for TCode I parameter: actual elapsed time in ms
        var intervalMs = (int)Math.Floor(elapsedSec * 1000.0 + 0.75);
        intervalMs = Math.Max(1, intervalMs);

        var parts = new List<string>();

        // === First pass: compute L0 stroke position for grind/random sync ===
        double strokePosition = 50.0;
        bool hasStrokeScript = false;
        var strokeConfig = _axisConfigs.FirstOrDefault(c => c.Id == "L0" && c.Enabled);
        if (strokeConfig != null && _scripts.TryGetValue("L0", out var strokeScript))
        {
            strokePosition = _interpolation.GetPosition(strokeScript, currentTimeMs, "L0");
            hasStrokeScript = true;
        }
        // Accumulate stroke travel distance for random/sync fill speed
        _cumulativeStrokeDistance += Math.Abs(strokePosition - _lastStrokePosition);
        _lastStrokePosition = strokePosition;

        // === Per-axis loop ===
        foreach (var config in _axisConfigs)
        {
            // Disabled axis: finish return-to-center, skip everything else
            if (!config.Enabled)
            {
                ProcessReturnToCenter(config, intervalMs, parts);
                continue;
            }

            // Scripted axis: interpolate position from funscript
            if (_isPlaying && _scripts.TryGetValue(config.Id, out var script))
            {
                var position = _interpolation.GetPosition(script, currentTimeMs, config.Id);
                var tcodeValue = PositionToTCode(config, position);

                if (IsDirty(config.Id, tcodeValue))
                {
                    parts.Add(FormatTCodeCommand(config, tcodeValue, intervalMs));
                    _lastSentValues[config.Id] = tcodeValue;
                }
                continue;
            }

            // === Fill mode (no script for this axis) ===
            if (config.FillMode == AxisFillMode.None)
            {
                ProcessReturnToCenter(config, intervalMs, parts);
                continue;
            }

            // --- Grind / ReverseGrind (R2 only) ---
            if (config.FillMode is AxisFillMode.Grind or AxisFillMode.ReverseGrind
                && config.Id == "R2")
            {
                double grindPos;
                if (config.FillMode == AxisFillMode.Grind)
                    grindPos = config.Min + (strokePosition / 100.0) * (config.Max - config.Min);
                else // ReverseGrind
                    grindPos = config.Max - (strokePosition / 100.0) * (config.Max - config.Min);

                var grindVal = (int)(grindPos / 100.0 * 999);
                grindVal = Math.Clamp(grindVal, 0, 999);

                var finalGrindVal = ApplyRampUp(config.Id, grindVal);
                if (IsDirty(config.Id, finalGrindVal))
                {
                    parts.Add(FormatTCodeCommand(config, finalGrindVal, intervalMs));
                    _lastSentValues[config.Id] = finalGrindVal;
                }
                continue;
            }

            // --- Random fill ---
            if (config.FillMode == AxisFillMode.Random)
            {
                if (!_randomGenerators.TryGetValue(config.Id, out var generator))
                {
                    generator = new RandomPatternGenerator(config.Min, config.Max);
                    _randomGenerators[config.Id] = generator;
                }
                generator.SetRange(config.Min, config.Max);

                // Use cumulative stroke distance when synced; otherwise time-based
                double progress;
                if (config.SyncWithStroke && config.Id != "L0" && hasStrokeScript)
                    progress = _cumulativeStrokeDistance;
                else
                    progress = currentTimeMs;

                var randomPos = generator.GetPosition(progress);
                var targetVal = (int)(randomPos / 100.0 * 999);
                targetVal = Math.Clamp(targetVal, 0, 999);

                var randomVal = ApplyRampUp(config.Id, targetVal);
                if (IsDirty(config.Id, randomVal))
                {
                    parts.Add(FormatTCodeCommand(config, randomVal, intervalMs));
                    _lastSentValues[config.Id] = randomVal;
                }
                continue;
            }

            // --- Waveform fill (Triangle, Sine, Saw, etc.) ---
            {
                // Advance fill time
                double fillTime;
                if (config.SyncWithStroke && config.Id != "L0" && hasStrokeScript)
                {
                    // Sync with stroke: use cumulative stroke distance as time base
                    // Normalize: a full stroke cycle ≈ 200 distance units → 1.0 period
                    fillTime = _cumulativeStrokeDistance * config.FillSpeedHz / 200.0;
                }
                else
                {
                    // Independent: accumulate time based on fill speed
                    if (!_cumulativeFillTime.TryGetValue(config.Id, out var cumTime))
                        cumTime = 0;
                    cumTime += config.FillSpeedHz * elapsedSec;
                    _cumulativeFillTime[config.Id] = cumTime;
                    fillTime = cumTime;
                }

                // PatternGenerator.Calculate returns 0.0–1.0
                var patternValue = PatternGenerator.Calculate(config.FillMode, fillTime);
                // Map 0.0–1.0 to position 0–100, then to TCode via min/max
                var position = patternValue * 100.0;
                var targetVal = PositionToTCode(config, position);

                var finalVal = ApplyRampUp(config.Id, targetVal);
                if (IsDirty(config.Id, finalVal))
                {
                    parts.Add(FormatTCodeCommand(config, finalVal, intervalMs));
                    _lastSentValues[config.Id] = finalVal;
                }
            }
        }

        if (parts.Count > 0)
        {
            _transport?.Send(string.Join(" ", parts) + "\n");
        }
    }

    // ===== Fill Mode Helpers =====

    /// <summary>
    /// Applies exponential ramp-up blend from midpoint (500) to target.
    /// </summary>
    private int ApplyRampUp(string axisId, int targetVal)
    {
        if (!_rampingAxes.TryGetValue(axisId, out var blend))
            return targetVal;

        blend += (1.0 - blend) * 0.04;

        if (blend >= 0.99)
        {
            _rampingAxes.Remove(axisId);
            return targetVal;
        }

        _rampingAxes[axisId] = blend;
        var blendedVal = (int)Math.Round(500.0 + (targetVal - 500.0) * blend);
        return Math.Clamp(blendedVal, 0, 999);
    }

    /// <summary>
    /// Processes return-to-center animation for disabled or fill-mode-None axes.
    /// Exponential smoothing glide to midpoint (500) at factor 0.04/tick.
    /// </summary>
    private void ProcessReturnToCenter(AxisConfig config, int intervalMs, List<string> parts)
    {
        if (!_returningAxes.TryGetValue(config.Id, out var currentPos))
            return;

        var newPos = currentPos + (500.0 - currentPos) * 0.04;
        var newVal = (int)Math.Round(newPos);
        newVal = Math.Clamp(newVal, 0, 999);

        if (Math.Abs(newPos - 500.0) < 1.0)
        {
            _returningAxes.Remove(config.Id);
            newVal = 500;
        }
        else
        {
            _returningAxes[config.Id] = newPos;
        }

        if (IsDirty(config.Id, newVal))
        {
            parts.Add(FormatTCodeCommand(config, newVal, intervalMs));
            _lastSentValues[config.Id] = newVal;
        }
    }

    /// <summary>
    /// Returns true if any axis has an active fill mode, or if there are
    /// axes animating return-to-center. Used to keep the output thread active.
    /// </summary>
    private bool HasActiveFillModes()
    {
        if (_returningAxes.Count > 0) return true;
        foreach (var config in _axisConfigs)
        {
            if (config.Enabled && config.FillMode != AxisFillMode.None)
                return true;
        }
        return false;
    }

    // ===== Helpers =====

    /// <summary>
    /// Converts a position (0–100) to a TCode value (0–999) applying the axis min/max range.
    /// </summary>
    internal static int PositionToTCode(AxisConfig config, double position)
    {
        var normalized = position / 100.0;
        var scaled = config.Min + normalized * (config.Max - config.Min);
        var tcodeValue = (int)(scaled / 100.0 * 999);
        return Math.Clamp(tcodeValue, 0, 999);
    }

    /// <summary>
    /// Returns true if the axis value changed by ≥1 since last transmission.
    /// </summary>
    internal bool IsDirty(string axisId, int tcodeValue)
    {
        if (!_lastSentValues.TryGetValue(axisId, out var last))
            return true;
        return Math.Abs(last - tcodeValue) >= 1;
    }

    /// <summary>
    /// Formats a TCode command: {prefix}{axisNum}{value:D3}I{intervalMs}
    /// </summary>
    internal static string FormatTCodeCommand(AxisConfig config, int tcodeValue, int intervalMs)
    {
        var prefix = config.Type == "rotation" ? "R" : "L";
        var axisNum = config.Id[1];
        return $"{prefix}{axisNum}{tcodeValue:D3}I{intervalMs}";
    }
}
