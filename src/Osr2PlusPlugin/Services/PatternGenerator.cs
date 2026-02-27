using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Static utility for deterministic waveform calculations.
/// Time-based patterns take a normalized time parameter t (0.0–1.0, wrapping)
/// and return a position value (0.0–1.0).
/// </summary>
public static class PatternGenerator
{
    /// <summary>
    /// Computes a waveform position for the given fill mode.
    /// </summary>
    /// <param name="fillMode">The waveform type.</param>
    /// <param name="t">Normalized time (0.0–1.0, wrapping modulo 1.0).</param>
    /// <returns>Position 0.0–1.0</returns>
    public static double Calculate(AxisFillMode fillMode, double t)
    {
        t = t % 1.0;
        if (t < 0) t += 1.0;

        return fillMode switch
        {
            AxisFillMode.Triangle        => CalculateTriangle(t),
            AxisFillMode.Sine            => CalculateSine(t),
            AxisFillMode.Saw             => CalculateSaw(t),
            AxisFillMode.SawtoothReverse => CalculateSawtoothReverse(t),
            AxisFillMode.Square          => CalculateSquare(t),
            AxisFillMode.Pulse           => CalculatePulse(t),
            AxisFillMode.EaseInOut       => CalculateEaseInOut(t),
            _ => 0.5
        };
    }

    /// <summary>Triangle: linear ramp up 0→1 (first half), linear ramp down 1→0 (second half).</summary>
    private static double CalculateTriangle(double t)
        => t < 0.5 ? t * 2.0 : 2.0 - t * 2.0;

    /// <summary>Sine: smooth sinusoidal oscillation.</summary>
    private static double CalculateSine(double t)
        => (-Math.Cos(t * Math.PI * 2.0) + 1.0) / 2.0;

    /// <summary>
    /// Saw: linear ramp 0→1 over 85% of period, then smooth cosine drop 1→0 over 15%.
    /// No instant direction changes — safe for physical actuators.
    /// </summary>
    private static double CalculateSaw(double t)
    {
        const double rampEnd = 0.85;
        if (t < rampEnd)
            return t / rampEnd; // Linear 0→1
        // Cosine drop 1→0
        var dt = (t - rampEnd) / (1.0 - rampEnd);
        return (Math.Cos(dt * Math.PI) + 1.0) / 2.0;
    }

    /// <summary>
    /// Sawtooth Reverse: smooth cosine rise 0→1 over 15% of period, then linear ramp 1→0 over 85%.
    /// No instant direction changes — safe for physical actuators.
    /// </summary>
    private static double CalculateSawtoothReverse(double t)
    {
        const double riseEnd = 0.15;
        if (t < riseEnd)
        {
            // Cosine rise 0→1
            var dt = t / riseEnd;
            return (-Math.Cos(dt * Math.PI) + 1.0) / 2.0;
        }
        // Linear 1→0
        return 1.0 - (t - riseEnd) / (1.0 - riseEnd);
    }

    /// <summary>
    /// Square: smooth cosine transitions between high and low dwells.
    /// Rise over 10%, dwell at 1.0 for 40%, fall over 10%, dwell at 0.0 for 40%.
    /// No instant direction changes — safe for physical actuators.
    /// </summary>
    private static double CalculateSquare(double t)
    {
        const double riseEnd = 0.10;
        const double highEnd = 0.50;
        const double fallEnd = 0.60;

        if (t < riseEnd)
        {
            var rt = t / riseEnd;
            return (-Math.Cos(rt * Math.PI) + 1.0) / 2.0;
        }
        if (t < highEnd)
            return 1.0;
        if (t < fallEnd)
        {
            var ft = (t - highEnd) / (fallEnd - highEnd);
            return (Math.Cos(ft * Math.PI) + 1.0) / 2.0;
        }
        return 0.0;
    }

    /// <summary>
    /// Pulse: holds at extremes with quick cosine transitions.
    /// Dwells at 1.0 for ~35% of period, transitions smoothly, dwells at 0.0 for ~35%.
    /// </summary>
    private static double CalculatePulse(double t)
    {
        const double riseEnd = 0.15;
        const double highEnd = 0.5;
        const double fallEnd = 0.65;
        // 0.65–1.0: low dwell

        if (t < riseEnd)
        {
            // Rise: cosine interpolation 0→1
            var rt = t / riseEnd;
            return (-Math.Cos(rt * Math.PI) + 1.0) / 2.0;
        }
        if (t < highEnd)
            return 1.0; // High dwell
        if (t < fallEnd)
        {
            // Fall: cosine interpolation 1→0
            var ft = (t - highEnd) / (fallEnd - highEnd);
            return (Math.Cos(ft * Math.PI) + 1.0) / 2.0;
        }
        return 0.0; // Low dwell
    }

    /// <summary>
    /// Ease In/Out: cubic ease-in-out applied to triangle base.
    /// Sharper acceleration at extremes than sine.
    /// </summary>
    private static double CalculateEaseInOut(double t)
    {
        // Map t to a full cycle: first half 0→1, second half 1→0
        double phase = t < 0.5 ? t * 2.0 : 2.0 - t * 2.0;
        // Apply cubic ease-in-out
        double eased = phase < 0.5
            ? 4.0 * phase * phase * phase
            : 1.0 - Math.Pow(-2.0 * phase + 2.0, 3) / 2.0;
        return eased;
    }
}
