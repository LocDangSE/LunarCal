using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Converts non-empty string to Visible and null/empty/whitespace string to Collapsed (or inverse if parameter is "Inverse").
/// </summary>
public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value as string);
        var inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);

        var isVisible = inverse ? isEmpty : !isEmpty;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
