using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Computes the width in pixels for a timed event within an overlapping cluster (<see cref="LaneGeometry"/>).
/// Values: column width, number of lanes.
/// </summary>
public sealed class LaneWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var (left, right) = LaneGeometry.Insets(parameter);
        return values is [double width, int lanes, ..]
            ? LaneGeometry.Width(width, lanes, left, right)
            : LaneGeometry.MinWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
