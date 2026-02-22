namespace Osr2PlusPlugin.Models;

/// <summary>
/// A single funscript action: a position (0-100) at a specific time (ms).
/// </summary>
public record FunscriptAction(long AtMs, int Pos);

/// <summary>
/// Parsed funscript data for a single axis.
/// </summary>
public class FunscriptData
{
    public string AxisId { get; set; } = "L0";
    public string FilePath { get; set; } = "";
    public List<FunscriptAction> Actions { get; set; } = new();
}
