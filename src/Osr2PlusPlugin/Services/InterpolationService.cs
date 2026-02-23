using System.Collections.Concurrent;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Provides linear interpolation between funscript actions for smooth position output.
/// Caches the last known index per axis for O(1) advancement during sequential playback,
/// falling back to binary search only when needed (e.g. after seeking).
/// </summary>
public class InterpolationService
{
    /// <summary>
    /// Per-axis cached index for fast sequential lookups.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _cachedIndices = new();

    /// <summary>
    /// Gets the interpolated position (0-100) at the given time in milliseconds.
    /// Uses a cached index for the given axis to avoid binary search on sequential calls.
    /// </summary>
    /// <param name="script">The funscript data containing sorted actions.</param>
    /// <param name="timeMs">Current playback time in milliseconds.</param>
    /// <param name="axisId">Axis identifier used for index caching.</param>
    /// <returns>Interpolated position value between 0 and 100.</returns>
    public double GetPosition(FunscriptData script, double timeMs, string axisId)
    {
        var actions = script.Actions;

        if (actions.Count == 0)
            return 50.0;

        if (actions.Count == 1)
            return actions[0].Pos;

        // Before first action
        if (timeMs <= actions[0].AtMs)
            return actions[0].Pos;

        // After last action
        if (timeMs >= actions[^1].AtMs)
            return actions[^1].Pos;

        // Try to use cached index for fast sequential advancement
        var lastIndex = _cachedIndices.GetOrAdd(axisId, 0);
        var lo = FindIndex(actions, timeMs, lastIndex);
        _cachedIndices[axisId] = lo;

        var hi = lo + 1;
        var a = actions[lo];
        var b = actions[hi];

        // Linear interpolation
        var range = b.AtMs - a.AtMs;
        if (range <= 0)
            return a.Pos;

        var t = (timeMs - a.AtMs) / range;
        return a.Pos + (b.Pos - a.Pos) * t;
    }

    /// <summary>
    /// Finds the index of the action just before timeMs.
    /// First tries to advance from lastIndex (O(1) for sequential playback),
    /// then falls back to binary search (O(log n) for seeks).
    /// </summary>
    private static int FindIndex(IReadOnlyList<FunscriptAction> actions, double timeMs, int lastIndex)
    {
        // Clamp lastIndex to valid range
        lastIndex = Math.Clamp(lastIndex, 0, actions.Count - 2);

        // Check if we can advance forward from the cached index
        // This is the common case during normal playback
        if (actions[lastIndex].AtMs <= timeMs)
        {
            while (lastIndex < actions.Count - 2 && actions[lastIndex + 1].AtMs <= timeMs)
            {
                lastIndex++;
            }
            return lastIndex;
        }

        // Went backward (seek) — fall back to binary search
        int lo = 0, hi = actions.Count - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (actions[mid].AtMs <= timeMs)
                lo = mid;
            else
                hi = mid;
        }

        return lo;
    }

    /// <summary>
    /// Resets all cached indices. Call on script load or seek.
    /// </summary>
    public void ResetIndices()
    {
        _cachedIndices.Clear();
    }
}
