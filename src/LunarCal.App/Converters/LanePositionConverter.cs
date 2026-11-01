using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Computes the horizontal left offset in pixels for a timed event within an overlapping cluster.
/// </summary>
public sealed class LanePositionConverter : IMultiValueConverter
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
            return 2.0 + columnIndex * laneWidth;
        }

        return 2.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
