using System.Globalization;
using System.Windows.Data;

namespace Osr2PlusPlugin.Converters;

/// <summary>
/// Inverts a boolean value. Provides a singleton <see cref="Instance"/> for use with x:Static.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
