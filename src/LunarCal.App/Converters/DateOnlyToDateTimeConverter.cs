using System.Globalization;
using System.Windows.Data;

namespace LunarCal.App.Converters;

/// <summary>Converts between <see cref="DateOnly"/> (view model) and <see cref="DateTime"/> (<c>DatePicker.SelectedDate</c>) (FR-UI-4).</summary>
public sealed class DateOnlyToDateTimeConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : null;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : Binding.DoNothing;
}
