namespace Osr2PlusPlugin.Models;

/// <summary>
/// Controls the beat bar overlay behavior.
/// </summary>
public enum BeatBarMode
{
    /// <summary>No beat bar displayed.</summary>
    Off,

    /// <summary>Beat markers appear at peaks (up→down direction changes).</summary>
    OnPeak,

    /// <summary>Beat markers appear at valleys (down→up direction changes).</summary>
    OnValley
}
