using AwesomeAssertions;
using LunarCal.Core.Calendar;
using Xunit;

namespace LunarCal.Core.Tests.Calendar;

public sealed class CalendarModelTests
{
    [Fact]
    public void FR_AUTH_5_CalendarInfo_has_AccountId_and_spec_fields()
    {
        var calendar = new CalendarInfo
        {
            Id = "cal-1",
            AccountId = "acc-google-user1",
            Summary = "Lịch cá nhân",
            Color = "#4285F4",
            IsSelected = true,
            IsPrimary = true,
            IsReadOnly = false,
            SyncToken = "token-xyz-123",
            TimeZone = "Asia/Ho_Chi_Minh",
            Description = "Mô tả lịch cá nhân",
        };

        calendar.Id.Should().Be("cal-1");
        calendar.AccountId.Should().Be("acc-google-user1");
        calendar.Summary.Should().Be("Lịch cá nhân");
        calendar.Color.Should().Be("#4285F4");
        calendar.IsSelected.Should().BeTrue();
        calendar.IsPrimary.Should().BeTrue();
        calendar.IsReadOnly.Should().BeFalse();
        calendar.SyncToken.Should().Be("token-xyz-123");
        calendar.TimeZone.Should().Be("Asia/Ho_Chi_Minh");
        calendar.Description.Should().Be("Mô tả lịch cá nhân");
    }

    [Fact]
    public void FR_SYNC_6_CalendarEvent_has_all_spec_fields()
    {
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(7));
        var end = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(7));
        var updated = new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.FromHours(7));

        var evt = new CalendarEvent
        {
            Id = "evt-1",
            CalendarId = "cal-1",
            GoogleId = "gid-123",
            RecurringEventId = "rec-series-456",
            Title = "Họp nhóm",
            Description = "Chi tiết cuộc họp",
            Location = "Tầng 5",
            StartUtc = start.ToUniversalTime(),
            EndUtc = end.ToUniversalTime(),
            AllDay = false,
            TimeZone = "Asia/Ho_Chi_Minh",
            Etag = "\"etag-12345\"",
            Updated = updated.ToUniversalTime(),
            Status = "confirmed",
            ExtendedProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lunarRuleId"] = "rule-gio-to",
                ["createdBy"] = "lunarcal-chat",
                ["batchId"] = "batch-999",
            },
        };

        evt.Id.Should().Be("evt-1");
        evt.CalendarId.Should().Be("cal-1");
        evt.GoogleId.Should().Be("gid-123");
        evt.RecurringEventId.Should().Be("rec-series-456");
        evt.Title.Should().Be("Họp nhóm");
        evt.Description.Should().Be("Chi tiết cuộc họp");
        evt.Location.Should().Be("Tầng 5");
        evt.StartUtc.Should().Be(start.ToUniversalTime());
        evt.EndUtc.Should().Be(end.ToUniversalTime());
        evt.AllDay.Should().BeFalse();
        evt.TimeZone.Should().Be("Asia/Ho_Chi_Minh");
        evt.Etag.Should().Be("\"etag-12345\"");
        evt.Updated.Should().Be(updated.ToUniversalTime());
        evt.Status.Should().Be("confirmed");
        evt.LunarRuleId.Should().Be("rule-gio-to");
        evt.CreatedBy.Should().Be("lunarcal-chat");
        evt.BatchId.Should().Be("batch-999");
    }

    [Fact]
    public void FR_SYNC_6_CalendarEvent_extended_property_accessors_return_null_when_empty()
    {
        var evt = new CalendarEvent
        {
            Id = "evt-minimal",
            CalendarId = "cal-1",
            Title = "Đơn giản",
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddHours(1),
        };

        evt.LunarRuleId.Should().BeNull();
        evt.CreatedBy.Should().BeNull();
        evt.BatchId.Should().BeNull();
    }
}
