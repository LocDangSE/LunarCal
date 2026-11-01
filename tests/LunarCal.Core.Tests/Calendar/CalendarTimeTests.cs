using AwesomeAssertions;
using LunarCal.Core.Calendar;
using Xunit;

namespace LunarCal.Core.Tests.Calendar;

public sealed class CalendarTimeTests
{
    private static readonly TimeZoneInfo _utcPlus7 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");

    private static readonly TimeZoneInfo _utcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    // DST starts at local midnight on 1 March, so 00:00–00:59 on that day does not exist.
    private static readonly TimeZoneInfo _midnightDstZone = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MidnightDst",
        TimeSpan.Zero,
        "MidnightDst",
        "MidnightDst",
        "MidnightDst-Summer",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 1)),
        ]);

    private static CalendarEvent Event(DateTimeOffset start, DateTimeOffset end, bool allDay = false, string? timeZone = null) => new()
    {
        Id = "e1",
        CalendarId = "c1",
        Title = "Event",
        StartUtc = start.ToUniversalTime(),
        EndUtc = end.ToUniversalTime(),
        AllDay = allDay,
        TimeZone = timeZone,
    };

    [Fact]
    public void FR_UI_7_GetUtcRange_uses_local_midnights_of_selected_timezone()
    {
        var (start, end) = CalendarTime.GetUtcRange(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16), _utcPlus7);

        start.Should().Be(new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero));
        end.Should().Be(new DateTimeOffset(2026, 11, 15, 17, 0, 0, TimeSpan.Zero));
        start.Offset.Should().Be(TimeSpan.Zero);

        var (startWest, _) = CalendarTime.GetUtcRange(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 16), _utcMinus5);
        startWest.Should().Be(new DateTimeOffset(2026, 11, 15, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FR_UI_7_StartOfDay_skips_nonexistent_midnight_in_dst_gap()
    {
        var start = CalendarTime.StartOfDayUtc(new DateOnly(2026, 3, 1), _midnightDstZone);

        // 01:00 local summer time (UTC+1) is the first valid moment of the day.
        start.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        CalendarTime.ToLocal(start, _midnightDstZone).Hour.Should().Be(1);
    }

    [Fact]
    public void FR_UI_7_ToLocalDate_depends_on_selected_timezone()
    {
        var instant = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

        CalendarTime.ToLocalDate(instant, _utcPlus7).Should().Be(new DateOnly(2026, 11, 16));
        CalendarTime.ToLocalDate(instant, _utcMinus5).Should().Be(new DateOnly(2026, 11, 15));
    }

    [Fact]
    public void FR_UI_7_Event_ending_at_midnight_does_not_cover_next_day()
    {
        var plus7 = TimeSpan.FromHours(7);
        var ev = Event(
            new DateTimeOffset(2026, 11, 15, 22, 0, 0, plus7),
            new DateTimeOffset(2026, 11, 16, 0, 0, 0, plus7));

        CalendarTime.GetCoveredDates(ev, _utcPlus7).Should().Be((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 15)));
        CalendarTime.Covers(ev, new DateOnly(2026, 11, 16), _utcPlus7).Should().BeFalse();
    }

    [Fact]
    public void FR_UI_7_Multi_day_and_zero_length_events_cover_expected_dates()
    {
        var plus7 = TimeSpan.FromHours(7);
        var multiDay = Event(
            new DateTimeOffset(2026, 11, 15, 20, 0, 0, plus7),
            new DateTimeOffset(2026, 11, 17, 9, 0, 0, plus7));
        CalendarTime.GetCoveredDates(multiDay, _utcPlus7).Should().Be((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 17)));
        CalendarTime.Covers(multiDay, new DateOnly(2026, 11, 16), _utcPlus7).Should().BeTrue();

        var instant = new DateTimeOffset(2026, 11, 15, 9, 0, 0, plus7);
        var zeroLength = Event(instant, instant);
        CalendarTime.GetCoveredDates(zeroLength, _utcPlus7).Should().Be((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void FR_UI_7_All_day_event_keeps_its_date_in_its_own_timezone()
    {
        var ev = Event(
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
            allDay: true,
            timeZone: "UTC");

        // Viewer in UTC-5 would see 14 Nov 19:00 if the zone were applied; the all-day date must stay 15 Nov.
        CalendarTime.GetCoveredDates(ev, _utcMinus5).Should().Be((new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void FR_UI_7_ResolveTimeZone_falls_back_for_empty_or_unknown_ids()
    {
        CalendarTime.ResolveTimeZone(null, _utcPlus7).Should().BeSameAs(_utcPlus7);
        CalendarTime.ResolveTimeZone(" ", _utcPlus7).Should().BeSameAs(_utcPlus7);
        CalendarTime.ResolveTimeZone("No/Such_Zone", _utcPlus7).Should().BeSameAs(_utcPlus7);
        CalendarTime.ResolveTimeZone("UTC", _utcPlus7).BaseUtcOffset.Should().Be(TimeSpan.Zero);
    }
}
