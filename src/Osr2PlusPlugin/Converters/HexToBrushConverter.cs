using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#007ACC") to a <see cref="SolidColorBrush"/>.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
