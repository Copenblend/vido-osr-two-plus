namespace Osr2PlusPlugin.Models;

/// <summary>
/// Represents the mutable runtime state for a single axis in test mode.
/// </summary>
internal class TestAxisState
{
    /// <summary>
    /// Gets or sets the normalized waveform phase in the range [0, 1).
    /// </summary>
    public double Phase { get; set; }

    /// <summary>
    /// Gets or sets the currently applied test speed in Hertz.
    /// </summary>
    public double CurrentSpeedHz { get; set; }

    /// <summary>
    /// Gets or sets the target test speed in Hertz.
    /// </summary>
    public double TargetSpeedHz { get; set; }

    /// <summary>
    /// Gets or sets the currently applied amplitude.
    /// </summary>
    public double CurrentAmplitude { get; set; }

    /// <summary>
    /// Gets or sets the target amplitude.
    /// </summary>
    public double TargetAmplitude { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp in Stopwatch ticks.
    /// </summary>
    public long LastTickAt { get; set; }

    /// <summary>
    /// Gets or sets cumulative progress used by random-mode test generation.
    /// </summary>
    public double CumulativeProgress { get; set; }
}
