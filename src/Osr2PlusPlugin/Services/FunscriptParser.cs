using System.IO;
using System.Text;
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
        if (string.IsNullOrEmpty(json))
            return new FunscriptData { AxisId = axisId };

        var bytes = Encoding.UTF8.GetBytes(json);
        return ParseFromBytes(bytes.AsSpan(), axisId);
    }

    /// <summary>
    /// Parse a funscript file from disk.
    /// Returns an empty FunscriptData on malformed JSON or missing file.
    /// </summary>
    /// <param name="filePath">Path to the .funscript file.</param>
    /// <param name="axisId">The axis this script represents (default "L0").</param>
    public FunscriptData ParseFile(string filePath, string axisId = "L0")
    {
        var bytes = File.ReadAllBytes(filePath);
        var data = ParseFromBytes(bytes.AsSpan(), axisId);
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
            var bytes = File.ReadAllBytes(filePath);
            var reader = new Utf8JsonReader(bytes.AsSpan(), CreateReaderOptions());

            bool hasAxesArray = false;
            var result = new Dictionary<string, FunscriptData>(StringComparer.OrdinalIgnoreCase);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("actions"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        SkipCurrentValue(ref reader);
                        continue;
                    }

                    var l0 = new FunscriptData { AxisId = "L0", FilePath = filePath };
                    ParseActionsFromReader(ref reader, l0.Actions);
                    SortActionsIfNeeded(l0.Actions);

                    if (l0.Actions.Count > 0)
                        result["L0"] = l0;

                    continue;
                }

                if (reader.ValueTextEquals("axes"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        SkipCurrentValue(ref reader);
                        continue;
                    }

                    hasAxesArray = true;
                    ParseAxesArray(ref reader, filePath, result);
                    continue;
                }

                if (!reader.Read())
                    break;

                SkipCurrentValue(ref reader);
            }

            if (!hasAxesArray)
                return null;

            return result.Count > 0 ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse funscript UTF-8 JSON bytes into a FunscriptData object.
    /// </summary>
    /// <param name="utf8Json">UTF-8 encoded funscript JSON bytes.</param>
    /// <param name="axisId">The axis this script represents.</param>
    private static FunscriptData ParseFromBytes(ReadOnlySpan<byte> utf8Json, string axisId)
    {
        var data = new FunscriptData { AxisId = axisId };

        try
        {
            var reader = new Utf8JsonReader(utf8Json, CreateReaderOptions());

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return data;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (!reader.ValueTextEquals("actions"u8))
                {
                    if (!reader.Read())
                        break;

                    SkipCurrentValue(ref reader);
                    continue;
                }

                if (!reader.Read())
                    break;

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    SkipCurrentValue(ref reader);
                    continue;
                }

                ParseActionsFromReader(ref reader, data.Actions);
                break;
            }

            SortActionsIfNeeded(data.Actions);
        }
        catch (JsonException)
        {
            // Malformed JSON — return empty data
        }

        return data;
    }

    /// <summary>
    /// Parse an axes array from a multi-axis funscript payload.
    /// </summary>
    /// <param name="reader">Utf8JsonReader positioned at StartArray for "axes".</param>
    /// <param name="filePath">Source file path.</param>
    /// <param name="result">Target dictionary.</param>
    private static void ParseAxesArray(
        ref Utf8JsonReader reader,
        string filePath,
        Dictionary<string, FunscriptData> result)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipCurrentValue(ref reader);
                continue;
            }

            string? axisId = null;
            var axisActions = new List<FunscriptAction>();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("id"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType == JsonTokenType.String)
                        axisId = reader.GetString();
                    else
                        SkipCurrentValue(ref reader);

                    continue;
                }

                if (reader.ValueTextEquals("actions"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType == JsonTokenType.StartArray)
                        ParseActionsFromReader(ref reader, axisActions);
                    else
                        SkipCurrentValue(ref reader);

                    continue;
                }

                if (!reader.Read())
                    break;

                SkipCurrentValue(ref reader);
            }

            if (string.IsNullOrEmpty(axisId) || !SupportedAxes.Contains(axisId) || axisActions.Count == 0)
                continue;

            SortActionsIfNeeded(axisActions);
            result[axisId] = new FunscriptData
            {
                AxisId = axisId,
                FilePath = filePath,
                Actions = axisActions,
            };
        }
    }

    /// <summary>
    /// Parse actions from a JSON array reader and append valid entries.
    /// </summary>
    /// <param name="reader">Utf8JsonReader positioned at StartArray.</param>
    /// <param name="actions">Target action list.</param>
    private static void ParseActionsFromReader(ref Utf8JsonReader reader, List<FunscriptAction> actions)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipCurrentValue(ref reader);
                continue;
            }

            long at = 0;
            int pos = 0;
            bool hasAt = false;
            bool hasPos = false;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("at"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var atMs))
                    {
                        at = atMs;
                        hasAt = true;
                    }
                    else
                    {
                        SkipCurrentValue(ref reader);
                    }

                    continue;
                }

                if (reader.ValueTextEquals("pos"u8))
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var position))
                    {
                        pos = Math.Clamp(position, 0, 100);
                        hasPos = true;
                    }
                    else
                    {
                        SkipCurrentValue(ref reader);
                    }

                    continue;
                }

                if (!reader.Read())
                    break;

                SkipCurrentValue(ref reader);
            }

            if (hasAt && hasPos)
                actions.Add(new FunscriptAction(at, pos));
        }
    }

    /// <summary>
    /// Sort actions only when input is not already ascending by timestamp.
    /// </summary>
    /// <param name="actions">Action list to inspect and conditionally sort.</param>
    private static void SortActionsIfNeeded(List<FunscriptAction> actions)
    {
        if (actions.Count <= 1)
            return;

        bool isSorted = true;
        for (int i = 1; i < actions.Count; i++)
        {
            if (actions[i].AtMs < actions[i - 1].AtMs)
            {
                isSorted = false;
                break;
            }
        }

        if (!isSorted)
            actions.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
    }

    /// <summary>
    /// Advance a reader past the current value (object/array/scalar).
    /// </summary>
    /// <param name="reader">Reader positioned at a value token.</param>
    private static void SkipCurrentValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
    }

    /// <summary>
    /// Reader options used for funscript parsing.
    /// </summary>
    private static JsonReaderOptions CreateReaderOptions()
        => new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
}
