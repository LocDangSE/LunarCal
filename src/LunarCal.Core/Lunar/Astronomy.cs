namespace LunarCal.Core.Lunar;

/// <summary>
/// Astronomical helpers from Hồ Ngọc Đức's lunar calendar algorithm (FR-LUNAR-1).
/// New moon: Meeus-style series (Astronomical Algorithms, ch. 49, simplified).
/// Sun longitude: low-precision series (accuracy about 0.01°).
/// All day numbers are Julian Day Numbers (JDN, the integer day that starts at noon UT).
/// </summary>
internal static class Astronomy
{
    /// <summary>Fixed time zone of the Vietnamese calendar: UTC+7 (spec FR-LUNAR-1, ADR-0006).</summary>
    public const double TimeZoneHours = 7.0;

    /// <summary>Mean length of a synodic month in days.</summary>
    public const double SynodicMonth = 29.530588853;

    /// <summary>Julian date of the new moon with index 0 (1900-01-01 01:50 UT, approximately).</summary>
    public const double NewMoonEpoch = 2415021.076998695;

    /// <summary>JDN of 0001-01-01 in the proleptic Gregorian calendar (the zero of <see cref="DateOnly.DayNumber"/>).</summary>
    private const int DayNumberToJdnOffset = 1721426;

    private const double DegToRad = Math.PI / 180.0;

    public static int ToJdn(DateOnly date) => date.DayNumber + DayNumberToJdnOffset;

    public static DateOnly FromJdn(int jdn) => DateOnly.FromDayNumber(jdn - DayNumberToJdnOffset);

    /// <summary>Julian date (UT, fractional) of the k-th new moon after <see cref="NewMoonEpoch"/>.</summary>
    public static double NewMoon(int k)
    {
        var t = k / 1236.85; // Julian centuries from 1900-01-00 12:00
        var t2 = t * t;
        var t3 = t2 * t;

        var jd1 = 2415020.75933 + (29.53058868 * k) + (0.0001178 * t2) - (0.000000155 * t3);
        jd1 += 0.00033 * Math.Sin((166.56 + (132.87 * t) - (0.009173 * t2)) * DegToRad);

        var m = 359.2242 + (29.10535608 * k) - (0.0000333 * t2) - (0.00000347 * t3); // Sun's mean anomaly
        var mpr = 306.0253 + (385.81691806 * k) + (0.0107306 * t2) + (0.00001236 * t3); // Moon's mean anomaly
        var f = 21.2964 + (390.67050646 * k) - (0.0016528 * t2) - (0.00000239 * t3); // Moon's argument of latitude

        var c1 = ((0.1734 - (0.000393 * t)) * Math.Sin(m * DegToRad)) + (0.0021 * Math.Sin(2 * DegToRad * m));
        c1 = c1 - (0.4068 * Math.Sin(mpr * DegToRad)) + (0.0161 * Math.Sin(DegToRad * 2 * mpr));
        c1 -= 0.0004 * Math.Sin(DegToRad * 3 * mpr);
        c1 = c1 + (0.0104 * Math.Sin(DegToRad * 2 * f)) - (0.0051 * Math.Sin(DegToRad * (m + mpr)));
        c1 = c1 - (0.0074 * Math.Sin(DegToRad * (m - mpr))) + (0.0004 * Math.Sin(DegToRad * ((2 * f) + m)));
        c1 = c1 - (0.0004 * Math.Sin(DegToRad * ((2 * f) - m))) - (0.0006 * Math.Sin(DegToRad * ((2 * f) + mpr)));
        c1 = c1 + (0.0010 * Math.Sin(DegToRad * ((2 * f) - mpr))) + (0.0005 * Math.Sin(DegToRad * ((2 * mpr) + m)));

        // ΔT (TT − UT) correction, in days.
        var deltaT = t < -11
            ? 0.001 + (0.000839 * t) + (0.0002261 * t2) - (0.00000845 * t3) - (0.000000081 * t * t3)
            : -0.000278 + (0.000265 * t) + (0.000262 * t2);

        return jd1 + c1 - deltaT;
    }

    /// <summary>JDN of the local (UTC+7) civil day that contains the k-th new moon.</summary>
    public static int NewMoonDay(int k) => (int)Math.Floor(NewMoon(k) + 0.5 + (TimeZoneHours / 24.0));

    /// <summary>Apparent sun longitude in radians, normalised to [0, 2π), at Julian date <paramref name="jd"/> (UT).</summary>
    public static double SunLongitude(double jd)
    {
        var t = (jd - 2451545.0) / 36525; // Julian centuries from J2000.0
        var t2 = t * t;

        var m = 357.52910 + (35999.05030 * t) - (0.0001559 * t2) - (0.00000048 * t * t2); // mean anomaly, degrees
        var l0 = 280.46645 + (36000.76983 * t) + (0.0003032 * t2); // mean longitude, degrees
        var dl = (1.914600 - (0.004817 * t) - (0.000014 * t2)) * Math.Sin(DegToRad * m);
        dl = dl + ((0.019993 - (0.000101 * t)) * Math.Sin(DegToRad * 2 * m)) + (0.000290 * Math.Sin(DegToRad * 3 * m));

        var l = (l0 + dl) * DegToRad;
        return l - (Math.PI * 2 * Math.Floor(l / (Math.PI * 2)));
    }

    /// <summary>
    /// Sun longitude at local (UTC+7) midnight at the start of day <paramref name="jdn"/>,
    /// as a sector index of <paramref name="sectors"/> equal parts of the ecliptic
    /// (12 for major terms / trung khí, 24 for all solar terms).
    /// </summary>
    public static int SunLongitudeSector(int jdn, int sectors) =>
        (int)Math.Floor(SunLongitude(jdn - 0.5 - (TimeZoneHours / 24.0)) / (2 * Math.PI) * sectors);

    /// <summary>Index k of the new moon whose day is closest to <paramref name="jdn"/> (for a JDN that is a new moon day).</summary>
    public static int NewMoonIndexNear(int jdn) => (int)Math.Floor(((jdn - NewMoonEpoch) / SynodicMonth) + 0.5);

    /// <summary>
    /// JDN of the start of lunar month 11 of Gregorian year <paramref name="year"/>:
    /// the month that contains the December (winter) solstice.
    /// </summary>
    public static int LunarMonth11Start(int year)
    {
        var off = ToJdn(new DateOnly(year, 12, 31)) - 2415021;
        var k = (int)Math.Floor(off / SynodicMonth);
        var nm = NewMoonDay(k);

        // If the sun is already past 270° (sector 9 of 12) at the start of that new moon day,
        // the solstice is in the previous month.
        return SunLongitudeSector(nm, 12) >= 9 ? NewMoonDay(k - 1) : nm;
    }

    /// <summary>
    /// Offset (in months after month 11 starting at <paramref name="month11Start"/>) of the leap month
    /// in a 13-month year: the first month that contains no major solar term (trung khí).
    /// </summary>
    public static int LeapMonthOffset(int month11Start)
    {
        var k = NewMoonIndexNear(month11Start);
        var i = 1;
        var arc = SunLongitudeSector(NewMoonDay(k + i), 12);
        int last;
        do
        {
            last = arc;
            i++;
            arc = SunLongitudeSector(NewMoonDay(k + i), 12);
        }
        while (arc != last && i < 14);

        return i - 1;
    }
}
