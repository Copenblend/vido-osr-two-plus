namespace Osr2PlusPlugin.Models;

/// <summary>
/// Direction-of-change filter for funscript-based beat detection.
/// Used by <see cref="Services.BeatDetectionService"/> to find peaks or valleys.
/// </summary>
public enum BeatDetectionMode
{
    /// <summary>Detect peaks (up→down direction changes).</summary>
    OnPeak,

    /// <summary>Detect valleys (down→up direction changes).</summary>
    OnValley
}
