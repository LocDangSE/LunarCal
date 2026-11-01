using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LunarCal.App.Converters;

/// <summary>
/// Converts a hex color string (e.g. #4285F4) to a SolidColorBrush.
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    private static readonly BrushConverter _converter = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (_converter.ConvertFromString(hex) is Brush brush)
                {
                    return brush;
                }
            }
            catch
            {
                // Fallback to default blue
            }
        }

        return Brushes.DodgerBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
