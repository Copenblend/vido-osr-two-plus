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
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch { }
        }
        var fallback = new SolidColorBrush(Colors.Gray);
        fallback.Freeze();
        return fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
