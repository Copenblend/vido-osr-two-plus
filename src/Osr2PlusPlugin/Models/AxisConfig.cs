using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Osr2PlusPlugin.Models;

/// <summary>
/// Per-axis configuration. Observable for UI binding. Persisted to settings (except ephemeral fields).
/// </summary>
public class AxisConfig : INotifyPropertyChanged
{
    // ===== Identity (non-persisted, set at construction) =====
    public string Id { get; set; } = "";          // "L0", "R0", "R1", "R2"
    public string Name { get; set; } = "";        // "Stroke", "Twist", "Roll", "Pitch"
    public string Type { get; set; } = "linear";  // "linear" or "rotation"
    public string Color { get; set; } = "#007ACC";

    // ===== Persisted Settings =====
    private int _min = 0;
    private int _max = 100;
    private bool _enabled = true;
    private AxisFillMode _fillMode = AxisFillMode.None;
    private bool _syncWithStroke = true;
    private double _fillSpeedHz = 1.0;

    /// <summary>Minimum amplitude (-50 to 149). Must be strictly less than Max.</summary>
    public int Min
    {
        get => _min;
        set { if (value < Max && Set(ref _min, Math.Clamp(value, -50, 149))) { OnPropertyChanged(nameof(RangeLabel)); OnPropertyChanged(nameof(IsExtendedRange)); } }
    }

    /// <summary>Maximum amplitude (-49 to 150). Must be strictly greater than Min.</summary>
    public int Max
    {
        get => _max;
        set { if (value > Min && Set(ref _max, Math.Clamp(value, -49, 150))) { OnPropertyChanged(nameof(RangeLabel)); OnPropertyChanged(nameof(IsExtendedRange)); } }
    }

    /// <summary>Whether this axis sends TCode instructions to the device.</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Active fill mode for this axis.</summary>
    public AxisFillMode FillMode { get => _fillMode; set => Set(ref _fillMode, value); }

    /// <summary>When true, fill pattern ticks only when L0 is moving and speed-matches L0. Not applicable to L0 itself.</summary>
    public bool SyncWithStroke { get => _syncWithStroke; set => Set(ref _syncWithStroke, value); }

    /// <summary>Independent fill pattern speed in Hz (0.1–3.0). Used when SyncWithStroke is false.</summary>
    public double FillSpeedHz { get => _fillSpeedHz; set => Set(ref _fillSpeedHz, Math.Clamp(value, 0.1, 3.0)); }

    // ===== Ephemeral (NOT persisted, reset each session) =====
    private double _positionOffset = 0;          // L0: -50 to +50 (%), R0: 0-359 (degrees)

    [JsonIgnore]
    public double PositionOffset { get => _positionOffset; set => Set(ref _positionOffset, value); }

    // ===== Derived =====
    [JsonIgnore]
    public string RangeLabel => $"{Min}-{Max}";

    [JsonIgnore]
    public bool HasPositionOffset => Id is "L0" or "R0" or "R1" or "R2";

    [JsonIgnore]
    public bool IsStroke => Id == "L0";

    [JsonIgnore]
    public bool IsPitch => Id == "R2";

    [JsonIgnore]
    public bool IsExtendedRange => Min < 0 || Max > 100;

    [JsonIgnore]
    public AxisFillMode[] AvailableFillModes => Id switch
    {
        "L0" => new[] { AxisFillMode.None },
        "R2" => new[] {
            AxisFillMode.None, AxisFillMode.Random,
            AxisFillMode.Triangle, AxisFillMode.Sine, AxisFillMode.Saw,
            AxisFillMode.SawtoothReverse, AxisFillMode.Square, AxisFillMode.Pulse,
            AxisFillMode.EaseInOut, AxisFillMode.Grind, AxisFillMode.Figure8
        },
        "R1" => new[] {
            AxisFillMode.None, AxisFillMode.Random,
            AxisFillMode.Triangle, AxisFillMode.Sine, AxisFillMode.Saw,
            AxisFillMode.SawtoothReverse, AxisFillMode.Square, AxisFillMode.Pulse,
            AxisFillMode.EaseInOut, AxisFillMode.Figure8
        },
        _ => new[] {
            AxisFillMode.None, AxisFillMode.Random,
            AxisFillMode.Triangle, AxisFillMode.Sine, AxisFillMode.Saw,
            AxisFillMode.SawtoothReverse, AxisFillMode.Square, AxisFillMode.Pulse,
            AxisFillMode.EaseInOut
        }
    };

    // ===== Funscript assignment (ephemeral) =====
    private string? _scriptFileName;
    private bool _isScriptManual;

    [JsonIgnore]
    public string? ScriptFileName { get => _scriptFileName; set { Set(ref _scriptFileName, value); OnPropertyChanged(nameof(HasScript)); } }

    [JsonIgnore]
    public bool IsScriptManual { get => _isScriptManual; set => Set(ref _isScriptManual, value); }

    [JsonIgnore]
    public bool HasScript => ScriptFileName != null;

    // ===== Card UI state (ephemeral) =====
    private bool _isExpanded = false;

    [JsonIgnore]
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    // ===== Defaults factory =====
    public static List<AxisConfig> CreateDefaults() => new()
    {
        new() { Id = "L0", Name = "Stroke", Type = "linear",   Color = "#007ACC", Min = 0,  Max = 100 },
        new() { Id = "R0", Name = "Twist",  Type = "rotation", Color = "#B800CC", Min = 0,  Max = 100 },
        new() { Id = "R1", Name = "Roll",   Type = "rotation", Color = "#CC5200", Min = 0,  Max = 100 },
        new() { Id = "R2", Name = "Pitch",  Type = "rotation", Color = "#14CC00", Min = 0,  Max = 75  },
    };

    // ===== INotifyPropertyChanged =====
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
