using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Detects beat timestamps (peaks or valleys) from funscript action data.
/// </summary>
public class BeatDetectionService
{
    /// <summary>
    /// Detects beat timestamps from funscript actions based on the selected mode.
    /// </summary>
    /// <param name="script">The parsed funscript data (typically L0 axis).</param>
    /// <param name="mode">Whether to detect peaks, valleys, or nothing.</param>
    /// <returns>Sorted list of beat times in milliseconds.</returns>
    public List<double> DetectBeats(FunscriptData? script, BeatBarMode mode)
    {
        if (mode == BeatBarMode.Off || script is null || script.Actions.Count < 3)
            return new List<double>();

        var actions = script.Actions;
        var beats = new List<double>();

        for (int i = 1; i < actions.Count - 1; i++)
        {
            var prev = actions[i - 1].Pos;
            var curr = actions[i].Pos;
            var next = actions[i + 1].Pos;

            switch (mode)
            {
                case BeatBarMode.OnPeak when curr > prev && curr >= next:
                    beats.Add(actions[i].AtMs);
                    break;

                case BeatBarMode.OnValley when curr < prev && curr <= next:
                    beats.Add(actions[i].AtMs);
                    break;
            }
        }

        return beats;
    }
}
