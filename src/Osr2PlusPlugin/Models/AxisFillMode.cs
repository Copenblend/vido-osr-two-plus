namespace Osr2PlusPlugin.Models;

/// <summary>
/// Fill mode applied to an axis when no funscript is loaded or as an override pattern.
/// </summary>
public enum AxisFillMode
{
    /// <summary>No fill — only funscript data or midpoint.</summary>
    None,
    /// <summary>Smooth random movement (cosine-interpolated).</summary>
    Random,
    /// <summary>Linear ascending/descending waveform.</summary>
    Triangle,
    /// <summary>Smooth sinusoidal waveform.</summary>
    Sine,
    /// <summary>Linear ascending ramp, instant drop.</summary>
    Saw,
    /// <summary>Instant snap up, linear descending ramp.</summary>
    SawtoothReverse,
    /// <summary>Instant alternation between min and max.</summary>
    Square,
    /// <summary>Holds at extremes with quick transitions.</summary>
    Pulse,
    /// <summary>Sine-like with sharper acceleration/deceleration at extremes.</summary>
    EaseInOut,
    /// <summary>R2 only: Pitch follows stroke position inversely (0→100, 100→0).</summary>
    Grind,
    /// <summary>R1/R2: Figure-8 Lissajous path — pitch/roll varies with stroke position and direction.</summary>
    Figure8
}
