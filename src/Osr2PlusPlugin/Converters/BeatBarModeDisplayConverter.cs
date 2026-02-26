using System.Globalization;
using System.Windows.Data;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts <see cref="BeatBarMode"/> instances to user-friendly display strings.
/// Built-in modes use friendly names; external modes use their <see cref="BeatBarMode.DisplayName"/>.
/// </summary>
public class BeatBarModeDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> BuiltInDisplayNames = new()
    {
        { "Off", "No Beat Bar" },
        { "OnPeak", "On Peak" },
        { "OnValley", "On Valley" },
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BeatBarMode mode)
        {
            if (BuiltInDisplayNames.TryGetValue(mode.Id, out var friendlyName))
                return friendlyName;

            return mode.DisplayName;
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
