using System.Globalization;
using System.Windows.Data;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts <see cref="BeatBarMode"/> enum values to user-friendly display strings.
/// </summary>
public class BeatBarModeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BeatBarMode mode)
        {
            return mode switch
            {
                BeatBarMode.Off => "No Beat Bar",
                BeatBarMode.OnPeak => "On Peak",
                BeatBarMode.OnValley => "On Valley",
                _ => mode.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
