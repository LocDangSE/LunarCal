using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Converters;

/// <summary>
/// Converts a hex colour string (calendar colours from Google, e.g. #2F6BFF) to a frozen, cached
/// <see cref="SolidColorBrush"/>. Invalid values fall back to <see cref="CalendarColors.DefaultHex"/>.
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Brush> _cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && !string.IsNullOrWhiteSpace(hex)
            ? _cache.GetOrAdd(hex.Trim(), Create)
            : _cache.GetOrAdd(CalendarColors.DefaultHex, Create);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush Create(string hex)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            color = (Color)ColorConverter.ConvertFromString(CalendarColors.DefaultHex);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
