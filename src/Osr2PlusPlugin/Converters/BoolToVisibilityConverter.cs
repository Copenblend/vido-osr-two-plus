using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>.
/// true → Visible, false → Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
