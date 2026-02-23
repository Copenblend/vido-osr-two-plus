using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts a hex color string to a <see cref="SolidColorBrush"/> at 20% opacity.
/// Used for axis badge backgrounds.
/// </summary>
public class HexToLowOpacityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                color.A = 51; // 20% of 255
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch { }
        }
        var fallback = new SolidColorBrush(Color.FromArgb(51, 128, 128, 128));
        fallback.Freeze();
        return fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
