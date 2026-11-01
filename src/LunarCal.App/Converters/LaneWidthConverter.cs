using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Computes the width in pixels for a timed event within an overlapping cluster.
/// </summary>
public sealed class LaneWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is double actualWidth &&
            values[1] is int totalColumns &&
            totalColumns > 0)
        {
            double availableWidth = Math.Max(0, actualWidth - 4);
            double laneWidth = availableWidth / totalColumns;
            return Math.Max(20.0, laneWidth - 4);
        }

        if (values.Length >= 1 && values[0] is double fallbackWidth)
        {
            return Math.Max(20.0, fallbackWidth - 8);
        }

        return 100.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
