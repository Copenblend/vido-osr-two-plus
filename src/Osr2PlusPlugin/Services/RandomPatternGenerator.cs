namespace Osr2PlusPlugin.Services;

/// <summary>
/// Generates smooth random movement patterns using cosine interpolation
/// between randomly selected target positions.
/// Progress input is cumulative stroke distance (synced) or cumulative
/// time (independent), so random speed matches the configured fill speed.
/// </summary>
public class RandomPatternGenerator
{
    private readonly Random _rng;
    private double _startPosition;
    private double _targetPosition;
    private double _transitionStart;
    private double _transitionDuration;
    private double _min;
    private double _max;
    private bool _initialized;

    /// <summary>
    /// Creates a new random pattern generator.
    /// </summary>
    /// <param name="min">Minimum output value (default 0).</param>
    /// <param name="max">Maximum output value (default 100).</param>
    /// <param name="seed">Optional RNG seed for deterministic testing. Null uses default RNG.</param>
    public RandomPatternGenerator(double min = 0, double max = 100, int? seed = null)
    {
        _min = min;
        _max = max;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Update the min/max output range (e.g. when axis config changes).
    /// </summary>
    public void SetRange(double min, double max)
    {
        _min = min;
        _max = max;
    }

    /// <summary>
    /// Get the cosine-interpolated position at the given progress value.
    /// Progress should be cumulative stroke distance (synced mode) or
    /// cumulative time in arbitrary units (independent mode).
    /// </summary>
    /// <returns>A value in the [min, max] range.</returns>
    public double GetPosition(double progress)
    {
        if (!_initialized)
        {
            _startPosition = _min + _rng.NextDouble() * (_max - _min);
            _targetPosition = _startPosition;
            _transitionStart = progress;
            _transitionDuration = 1; // Force immediate new-target generation
            _initialized = true;
        }

        var elapsed = progress - _transitionStart;
        if (elapsed >= _transitionDuration)
        {
            GenerateNewTarget(progress);
            elapsed = 0;
        }

        var t = Math.Clamp(elapsed / _transitionDuration, 0, 1);
        // Cosine interpolation for smooth acceleration/deceleration
        var cosineT = (1.0 - Math.Cos(t * Math.PI)) / 2.0;
        return _startPosition + (_targetPosition - _startPosition) * cosineT;
    }

    /// <summary>
    /// Reset state (e.g. when playback restarts or scripts change).
    /// </summary>
    public void Reset()
    {
        _initialized = false;
    }

    private void GenerateNewTarget(double progress)
    {
        _startPosition = _targetPosition;
        _targetPosition = _min + _rng.NextDouble() * (_max - _min);

        // Transition duration scales with target distance for natural feel.
        // Min duration = distance so random axis never moves faster than stroke.
        // Base duration 50–300 progress units (a full stroke cycle ≈ 200 distance).
        var distance = Math.Abs(_targetPosition - _startPosition);
        _transitionDuration = Math.Max(distance, 50 + _rng.NextDouble() * 250);
        _transitionStart = progress;
    }
}
