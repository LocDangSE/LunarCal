namespace LunarCal.App.Converters;

/// <summary>
/// Horizontal placement of timed events in a day column, measured from Figma S06 (24:60): blocks start 3 px
/// after the column line, end 5 px before the next one, and overlapping lanes are 2 px apart.
/// </summary>
public static class LaneGeometry
{
    /// <summary>Gap between the column line and the first lane.</summary>
    public const double LeftInset = 3.0;

    /// <summary>Gap between the last lane and the next column line.</summary>
    public const double RightInset = 5.0;

    /// <summary>Gap between two overlapping lanes.</summary>
    public const double LaneGap = 2.0;

    /// <summary>Smallest width a block may get on very narrow windows.</summary>
    public const double MinWidth = 20.0;

    /// <summary>Width of one lane in a column of <paramref name="columnWidth"/> split into <paramref name="lanes"/>.</summary>
    public static double Width(double columnWidth, int lanes, double leftInset = LeftInset, double rightInset = RightInset)
    {
        lanes = Math.Max(1, lanes);
        var available = columnWidth - leftInset - rightInset - ((lanes - 1) * LaneGap);
        return Math.Max(MinWidth, available / lanes);
    }

    /// <summary>Left offset of lane <paramref name="index"/>.</summary>
    public static double Left(double columnWidth, int index, int lanes, double leftInset = LeftInset, double rightInset = RightInset) =>
        leftInset + (index * (Width(columnWidth, lanes, leftInset, rightInset) + LaneGap));

    /// <summary>
    /// Reads "left,right" insets from a converter parameter. The Day view (Figma S07 25:91) uses "8,16"; the
    /// Week view uses the defaults.
    /// </summary>
    public static (double Left, double Right) Insets(object? parameter)
    {
        if (parameter is string text)
        {
            var parts = text.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var left)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var right))
            {
                return (left, right);
            }
        }

        return (LeftInset, RightInset);
    }
}
