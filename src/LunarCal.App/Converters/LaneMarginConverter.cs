using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Computes the left margin for a timed event within an overlapping cluster.
/// </summary>
public sealed class LaneMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 &&
            values[0] is double actualWidth &&
            values[1] is int columnIndex &&
            values[2] is int totalColumns &&
            totalColumns > 0)
        {
            double availableWidth = Math.Max(0, actualWidth - 4);
            double laneWidth = availableWidth / totalColumns;
            double left = 2.0 + columnIndex * laneWidth;
            return new Thickness(left, 0, 0, 0);
        }

        return new Thickness(2.0, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
