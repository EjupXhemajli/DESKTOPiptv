using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ExIptv.Converters;

/// <summary>true -> Visible, false -> Collapsed. Mit Parameter "invert" umkehrbar.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
