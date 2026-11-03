using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>Converts between <see cref="TimeOnly"/> and an "HH:mm" text box value (FR-UI-4). Invalid text leaves the source unchanged.</summary>
public sealed class TimeOnlyToStringConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeOnly time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : Binding.DoNothing;
}
