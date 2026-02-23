using System.Globalization;
using System.Windows.Data;
using Osr2PlusPlugin.Models;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts <see cref="AxisFillMode"/> enum values to user-friendly display strings.
/// </summary>
public class FillModeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AxisFillMode mode)
        {
            return mode switch
            {
                AxisFillMode.SawtoothReverse => "Reverse Saw",
                AxisFillMode.EaseInOut => "Ease In/Out",
                AxisFillMode.Figure8 => "Figure 8",
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
