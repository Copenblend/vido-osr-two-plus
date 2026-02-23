using System.IO;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Finds matching .funscript files for a given video file using naming conventions.
/// Convention: video.funscript → L0, video.twist.funscript → R0,
///             video.roll.funscript → R1, video.pitch.funscript → R2
/// Only supports the 4 OSR2+ axes (no L1/surge, L2/sway).
/// </summary>
public class FunscriptMatcher
{
    /// <summary>
    /// Maps axis suffixes to axis IDs.
    /// The default (no suffix) maps to L0 (stroke).
    /// </summary>
    private static readonly Dictionary<string, string> SuffixToAxis = new(StringComparer.OrdinalIgnoreCase)
    {
        { "",      "L0" },  // video.funscript → L0 (stroke)
        { "twist", "R0" },  // video.twist.funscript → R0
        { "roll",  "R1" },  // video.roll.funscript → R1
        { "pitch", "R2" },  // video.pitch.funscript → R2
    };

    /// <summary>
    /// Finds matching funscript files for the given video file.
    /// Searches the same directory as the video using case-insensitive matching.
    /// </summary>
    /// <param name="videoPath">Full path to the video file.</param>
    /// <returns>Dictionary of axisId → funscript file path. Empty if no matches.</returns>
    public Dictionary<string, string> FindMatchingScripts(string videoPath)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(videoPath))
            return result;

        var directory = Path.GetDirectoryName(videoPath);
        if (directory == null || !Directory.Exists(directory))
            return result;

        var videoNameNoExt = Path.GetFileNameWithoutExtension(videoPath);

        // Get all .funscript files in the directory for case-insensitive matching
        var funscriptFiles = Directory.GetFiles(directory, "*.funscript", SearchOption.TopDirectoryOnly);

        foreach (var (suffix, axisId) in SuffixToAxis)
        {
            string expectedFileName = string.IsNullOrEmpty(suffix)
                ? $"{videoNameNoExt}.funscript"
                : $"{videoNameNoExt}.{suffix}.funscript";

            // Case-insensitive match against actual files in directory
            var match = Array.Find(funscriptFiles,
                f => string.Equals(Path.GetFileName(f), expectedFileName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                result[axisId] = match;
            }
        }

        return result;
    }
}
