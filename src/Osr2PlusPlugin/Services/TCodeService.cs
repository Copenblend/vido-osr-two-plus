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
    }

    /// <summary>
    /// Set the axis configurations (min/max/enabled/fill mode).
    /// </summary>
    public void SetAxisConfigs(List<AxisConfig> configs)
    {
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
                if (_transport?.IsConnected == true && _isPlaying)
                {
                    OutputTick(elapsedSec);
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

        foreach (var config in _axisConfigs)
        {
            if (!config.Enabled) continue;

            // Scripted axis: interpolate position from funscript
            if (_scripts.TryGetValue(config.Id, out var script))
            {
                var position = _interpolation.GetPosition(script, currentTimeMs, config.Id);
                var tcodeValue = PositionToTCode(config, position);

                if (IsDirty(config.Id, tcodeValue))
                {
                    parts.Add(FormatTCodeCommand(config, tcodeValue, intervalMs));
                    _lastSentValues[config.Id] = tcodeValue;
                }
            }

            // Fill mode handling added in VOSR-016
            // Position offset added in VOSR-017
            // Test mode added in VOSR-018
        }

        if (parts.Count > 0)
        {
            _transport?.Send(string.Join(" ", parts) + "\n");
        }
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
