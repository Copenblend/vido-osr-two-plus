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

    // ===== Test Mode State =====
    private readonly object _testLock = new();
    private readonly Dictionary<string, TestAxisState> _testingAxes = new();

    /// <summary>Raised when a test axis finishes ramping down.</summary>
    public event Action<string>? TestAxisStopped;

    /// <summary>Raised when all test axes are auto-stopped (e.g. playback starts).</summary>
    public event Action? AllTestsStopped;

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
    /// Auto-stops all test axes when funscript playback starts.
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

        // Auto-stop all test axes when funscript playback starts
        if (playing && _scripts.Count > 0)
        {
            StopAllTestAxes();
        }
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
        lock (_testLock) _testingAxes.Clear();
    }

    // ===== Test Mode API =====

    /// <summary>
    /// Start test oscillation on the given axis.
    /// </summary>
    public void StartTestAxis(string axisId, double speedHz)
    {
        speedHz = Math.Clamp(speedHz, 0.1, 5.0);
        lock (_testLock)
        {
            _testingAxes[axisId] = new TestAxisState
            {
                Phase = 0,
                CurrentSpeedHz = speedHz,
                TargetSpeedHz = speedHz,
                CurrentAmplitude = 0,       // Ramps up smoothly
                TargetAmplitude = 50,       // Full range: ±50 around center
                LastTickAt = Stopwatch.GetTimestamp()
            };
        }
    }

    /// <summary>
    /// Begin smooth stop of test oscillation on the given axis.
    /// </summary>
    public void StopTestAxis(string axisId)
    {
        lock (_testLock)
        {
            if (_testingAxes.TryGetValue(axisId, out var state))
            {
                state.TargetAmplitude = 0; // Ramp down; removed in tick when amplitude < 0.5
            }
            else
            {
                // Not currently testing — send midpoint as safety
                SendMidpoint(axisId);
            }
        }
    }

    /// <summary>
    /// Update the test speed for an axis currently under test.
    /// </summary>
    public void UpdateTestSpeed(string axisId, double speedHz)
    {
        speedHz = Math.Clamp(speedHz, 0.1, 5.0);
        lock (_testLock)
        {
            if (_testingAxes.TryGetValue(axisId, out var state))
            {
                state.TargetSpeedHz = speedHz;
            }
        }
    }

    /// <summary>
    /// Check whether an axis is currently under test.
    /// </summary>
    public bool IsAxisTesting(string axisId)
    {
        lock (_testLock) return _testingAxes.ContainsKey(axisId);
    }

    /// <summary>
    /// Stop all test axes immediately (e.g. on disconnect or playback start).
    /// </summary>
    public void StopAllTestAxes()
    {
        List<string> stoppedIds;
        lock (_testLock)
        {
            stoppedIds = _testingAxes.Keys.ToList();
            _testingAxes.Clear();
        }

        // Send midpoints outside the lock
        foreach (var id in stoppedIds)
        {
            SendMidpoint(id);
        }

        if (stoppedIds.Count > 0)
        {
            AllTestsStopped?.Invoke();
        }
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
                    bool hasTestAxes;
                    lock (_testLock) hasTestAxes = _testingAxes.Count > 0;

                    bool hasFillOrReturn = HasActiveFillModes();
                    if (_isPlaying || hasFillOrReturn || hasTestAxes)
                    {
                        OutputTick(elapsedSec, hasTestAxes);
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

    private void OutputTick(double elapsedSec, bool hasTestAxes)
    {
        var rawTimeMs = GetExtrapolatedTimeMs();
        var currentTimeMs = rawTimeMs - _offsetMs;

        // Interval for TCode I parameter: actual elapsed time in ms
        var intervalMs = (int)Math.Floor(elapsedSec * 1000.0 + 0.75);
        intervalMs = Math.Max(1, intervalMs);

        var parts = new List<string>();
        var finishedTestAxes = new List<string>();

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

            // === Test mode: cosine oscillation with smooth ramp-up/down ===
            TestAxisState? testState = null;
            if (hasTestAxes)
            {
                lock (_testLock) _testingAxes.TryGetValue(config.Id, out testState);
            }

            if (testState != null)
            {
                var now = Stopwatch.GetTimestamp();
                var testDeltaSec = Math.Min(
                    (now - testState.LastTickAt) / (double)Stopwatch.Frequency, 0.1);
                testState.LastTickAt = now;

                // Smooth speed transition (exponential smoothing, factor 0.03)
                testState.CurrentSpeedHz += (testState.TargetSpeedHz - testState.CurrentSpeedHz) * 0.03;

                // Smooth amplitude ramp (exponential smoothing, factor 0.02)
                testState.CurrentAmplitude += (testState.TargetAmplitude - testState.CurrentAmplitude) * 0.02;

                // Check if finished ramping down
                if (testState.TargetAmplitude == 0 && testState.CurrentAmplitude < 0.5)
                {
                    finishedTestAxes.Add(config.Id);
                    var midVal = PositionToTCode(config, 50.0);
                    parts.Add(FormatTCodeCommand(config, midVal, intervalMs));
                    _lastSentValues[config.Id] = midVal;
                    continue;
                }

                // Advance phase (cumulative — no jumps on speed change)
                testState.Phase += testState.CurrentSpeedHz * testDeltaSec;
                testState.Phase %= 1.0;

                // Cosine waveform: smooth direction reversals
                var testPosition = 50.0 + testState.CurrentAmplitude * Math.Cos(testState.Phase * 2.0 * Math.PI);

                // Scale through axis min/max and apply offset
                var testTcode = ApplyPositionOffset(config, PositionToTCode(config, testPosition));
                if (IsDirty(config.Id, testTcode))
                {
                    parts.Add(FormatTCodeCommand(config, testTcode, intervalMs));
                    _lastSentValues[config.Id] = testTcode;
                }
                continue; // Skip normal playback for this axis
            }

            // Scripted axis: interpolate position from funscript
            if (_isPlaying && _scripts.TryGetValue(config.Id, out var script))
            {
                var position = _interpolation.GetPosition(script, currentTimeMs, config.Id);
                var tcodeValue = ApplyPositionOffset(config, PositionToTCode(config, position));

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
                grindVal = ApplyPositionOffset(config, grindVal);

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
                targetVal = ApplyPositionOffset(config, targetVal);

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
                var targetVal = ApplyPositionOffset(config, PositionToTCode(config, position));

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

        // Clean up finished test axes (outside the per-axis loop to avoid modifying dictionary during iteration)
        if (finishedTestAxes.Count > 0)
        {
            lock (_testLock)
            {
                foreach (var id in finishedTestAxes)
                    _testingAxes.Remove(id);
            }
            foreach (var id in finishedTestAxes)
                TestAxisStopped?.Invoke(id);
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
    /// Applies per-axis position offset to a TCode value.
    /// L0: offset is -50 to +50 (percentage points), added after min/max scaling, result clamped 0–999.
    /// R0: offset is 0–359 (degrees), rotated via modular wrapping.
    /// R1, R2: no offset applied.
    /// </summary>
    internal static int ApplyPositionOffset(AxisConfig config, int tcodeValue)
    {
        if (config.PositionOffset == 0 || !config.HasPositionOffset)
            return tcodeValue;

        if (config.Id == "L0")
        {
            // L0: offset is percentage points (-50 to +50) added to the scaled position
            var offsetTcode = (int)(config.PositionOffset / 100.0 * 999);
            return Math.Clamp(tcodeValue + offsetTcode, 0, 999);
        }

        if (config.Id == "R0")
        {
            // R0: offset is degrees (0–359), wrapping via modulo
            var offsetTcode = (int)(config.PositionOffset / 360.0 * 999);
            var result = (tcodeValue + offsetTcode) % 1000;
            if (result < 0) result += 1000;
            return result;
        }

        return tcodeValue;
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

    /// <summary>
    /// Sends a midpoint command (500) for the given axis to the transport.
    /// </summary>
    private void SendMidpoint(string axisId)
    {
        var config = _axisConfigs.FirstOrDefault(c => c.Id == axisId);
        if (config == null || _transport?.IsConnected != true) return;
        var prefix = config.Type == "rotation" ? "R" : "L";
        var axisNum = config.Id[1];
        _transport.Send($"{prefix}{axisNum}500I500\n");
        _lastSentValues.Remove(axisId);
    }

    // ===== Test Axis State =====

    private class TestAxisState
    {
        public double Phase { get; set; }
        public double CurrentSpeedHz { get; set; }
        public double TargetSpeedHz { get; set; }
        public double CurrentAmplitude { get; set; }
        public double TargetAmplitude { get; set; }
        public long LastTickAt { get; set; }
    }
}
