using System.IO;
using System.Text.Json;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Parses .funscript JSON files into FunscriptData.
/// Handles both single-axis files and embedded multi-axis format.
/// </summary>
public class FunscriptParser
{
    /// <summary>
    /// Supported OSR2+ axis IDs. Any axes not in this set are ignored
    /// during multi-axis parsing.
    /// </summary>
    private static readonly HashSet<string> SupportedAxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "L0", "R0", "R1", "R2"
    };

    /// <summary>
    /// Parse a funscript JSON string into a FunscriptData object.
    /// Returns an empty FunscriptData on malformed JSON.
    /// </summary>
    /// <param name="json">Raw JSON content of a .funscript file.</param>
    /// <param name="axisId">The axis this script represents (default "L0").</param>
    public FunscriptData Parse(string json, string axisId = "L0")
    {
        var data = new FunscriptData { AxisId = axisId };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            ParseActions(root, data);
        }
        catch (JsonException)
        {
            // Malformed JSON — return empty data
        }

        return data;
    }

    /// <summary>
    /// Parse a funscript file from disk.
    /// Returns an empty FunscriptData on malformed JSON or missing file.
    /// </summary>
    /// <param name="filePath">Path to the .funscript file.</param>
    /// <param name="axisId">The axis this script represents (default "L0").</param>
    public FunscriptData ParseFile(string filePath, string axisId = "L0")
    {
        var json = File.ReadAllText(filePath);
        var data = Parse(json, axisId);
        data.FilePath = filePath;
        return data;
    }

    /// <summary>
    /// Try to parse a funscript file as an embedded multi-axis format.
    /// Returns a dictionary of axisId → FunscriptData if the file contains an "axes" array.
    /// Returns null if the file has no "axes" array or is malformed.
    /// Only returns axes supported by the plugin (L0, R0, R1, R2).
    /// </summary>
    /// <param name="filePath">Path to the .funscript file.</param>
    public Dictionary<string, FunscriptData>? TryParseMultiAxis(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("axes", out var axesElement) ||
                axesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new Dictionary<string, FunscriptData>(StringComparer.OrdinalIgnoreCase);

            // Top-level "actions" → L0 (main stroke axis)
            if (root.TryGetProperty("actions", out var topActions) &&
                topActions.ValueKind == JsonValueKind.Array)
            {
                var l0 = new FunscriptData { AxisId = "L0", FilePath = filePath };
                ParseActionsFromElement(topActions, l0);

                if (l0.Actions.Count > 0)
                    result["L0"] = l0;
            }

            // Each element in "axes" array: { "id": "R0", "actions": [...] }
            foreach (var axisObj in axesElement.EnumerateArray())
            {
                if (!axisObj.TryGetProperty("id", out var idElement))
                    continue;

                var id = idElement.GetString();
                if (string.IsNullOrEmpty(id))
                    continue;

                // Filter to supported axes only
                if (!SupportedAxes.Contains(id))
                    continue;

                var data = new FunscriptData { AxisId = id, FilePath = filePath };

                if (axisObj.TryGetProperty("actions", out var actionsElement) &&
                    actionsElement.ValueKind == JsonValueKind.Array)
                {
                    ParseActionsFromElement(actionsElement, data);
                }

                if (data.Actions.Count > 0)
                    result[id] = data;
            }

            return result.Count > 0 ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the "actions" property from a JSON element into the given FunscriptData,
    /// clamping pos to 0-100 and sorting by AtMs ascending.
    /// </summary>
    private static void ParseActions(JsonElement root, FunscriptData data)
    {
        if (root.TryGetProperty("actions", out var actionsElement) &&
            actionsElement.ValueKind == JsonValueKind.Array)
        {
            ParseActionsFromElement(actionsElement, data);
        }
    }

    /// <summary>
    /// Reads actions from a JSON array element, clamping pos and sorting.
    /// </summary>
    private static void ParseActionsFromElement(JsonElement actionsArray, FunscriptData data)
    {
        foreach (var action in actionsArray.EnumerateArray())
        {
            if (action.TryGetProperty("at", out var atProp) &&
                action.TryGetProperty("pos", out var posProp))
            {
                var at = atProp.GetInt64();
                var pos = Math.Clamp(posProp.GetInt32(), 0, 100);
                data.Actions.Add(new FunscriptAction(at, pos));
            }
        }

        data.Actions.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
    }
}
