namespace Osr2PlusPlugin.Models;

/// <summary>
/// Controls the beat bar overlay behavior. Supports built-in modes (Off, OnPeak, OnValley)
/// and dynamically registered external modes from plugins via <c>IExternalBeatSource</c>.
/// </summary>
public sealed class BeatBarMode : IEquatable<BeatBarMode>
{
    /// <summary>No beat bar displayed.</summary>
    public static readonly BeatBarMode Off = new("Off", "Off", isExternal: false);

    /// <summary>Beat markers appear at peaks (up→down direction changes).</summary>
    public static readonly BeatBarMode OnPeak = new("OnPeak", "OnPeak", isExternal: false);

    /// <summary>Beat markers appear at valleys (down→up direction changes).</summary>
    public static readonly BeatBarMode OnValley = new("OnValley", "OnValley", isExternal: false);

    /// <summary>All built-in modes in display order.</summary>
    public static readonly IReadOnlyList<BeatBarMode> BuiltInModes = [Off, OnPeak, OnValley];

    /// <summary>Unique identifier for this mode. For built-in modes: "Off", "OnPeak", "OnValley". For external: the source Id.</summary>
    public string Id { get; }

    /// <summary>Display name shown in the ComboBox.</summary>
    public string DisplayName { get; }

    /// <summary>True if this mode is provided by an external plugin.</summary>
    public bool IsExternal { get; }

    private BeatBarMode(string id, string displayName, bool isExternal)
    {
        Id = id;
        DisplayName = displayName;
        IsExternal = isExternal;
    }

    /// <summary>
    /// Creates an external beat bar mode from a plugin-provided beat source.
    /// </summary>
    public static BeatBarMode CreateExternal(string sourceId, string displayName)
        => new(sourceId, displayName, isExternal: true);

    public bool Equals(BeatBarMode? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as BeatBarMode);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => Id;

    public static bool operator ==(BeatBarMode? left, BeatBarMode? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(BeatBarMode? left, BeatBarMode? right)
        => !(left == right);
}
