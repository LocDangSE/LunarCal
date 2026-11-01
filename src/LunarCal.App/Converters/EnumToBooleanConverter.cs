using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>
/// Converts an enum value to boolean for radio/segmented button check states.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        var checkValue = value.ToString();
        var targetValue = parameter.ToString();
        return string.Equals(checkValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string paramString && targetType.IsEnum)
        {
            return Enum.Parse(targetType, paramString, ignoreCase: true);
        }

        return Binding.DoNothing;
    }
}
