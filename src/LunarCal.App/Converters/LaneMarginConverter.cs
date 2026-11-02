using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Computes the left margin for a timed event within an overlapping cluster (<see cref="LaneGeometry"/>).
/// Values: column width, lane index, number of lanes.
/// </summary>
public sealed class LaneMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var (left, right) = LaneGeometry.Insets(parameter);
        return values is [double width, int index, int lanes, ..]
            ? new Thickness(LaneGeometry.Left(width, index, lanes, left, right), 0, 0, 0)
            : new Thickness(left, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
