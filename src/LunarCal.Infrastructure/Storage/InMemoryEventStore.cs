using System.Collections.Concurrent;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// In-memory event store pre-seeded with sample data for Phase 2 UI shell development (spec §6, FR-UI-1, FR-SYNC-6).
/// Phase 3 replaces or supplements this with SQLite + SQLCipher persistent storage.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, CalendarInfo> _calendars = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CalendarEvent> _events = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryEventStore"/>, optionally seeded with sample data.
    /// </summary>
    /// <param name="seedSampleData">Whether to pre-seed sample calendars and events.</param>
    public InMemoryEventStore(bool seedSampleData = true)
    {
        if (seedSampleData)
        {
            SeedSampleData();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<CalendarInfo> query = _calendars.Values;
        if (!string.IsNullOrEmpty(accountId))
        {
            query = query.Where(c => string.Equals(c.AccountId, accountId, StringComparison.Ordinal));
        }

        var result = query
            .OrderBy(c => !c.IsPrimary)
            .ThenBy(c => c.AccountId, StringComparer.Ordinal) // keep each account's calendars together (Figma 01)
            .ThenBy(c => c.Summary, StringComparer.CurrentCulture)
            .ToList();

        return Task.FromResult<IReadOnlyList<CalendarInfo>>(result);
    }

    /// <inheritdoc/>
    public Task<CalendarInfo?> GetCalendarByIdAsync(string calendarId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        cancellationToken.ThrowIfCancellationRequested();

        _calendars.TryGetValue(calendarId, out var calendar);
        return Task.FromResult(calendar);
    }

    /// <inheritdoc/>
    public Task UpsertCalendarAsync(CalendarInfo calendar, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendar.Id);
        cancellationToken.ThrowIfCancellationRequested();

        _calendars[calendar.Id] = calendar;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteCalendarAsync(string calendarId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        cancellationToken.ThrowIfCancellationRequested();

        _calendars.TryRemove(calendarId, out _);

        // Also cascade delete events belonging to this calendar
        var associatedEvents = _events.Values
            .Where(e => string.Equals(e.CalendarId, calendarId, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToList();

        foreach (var eventId in associatedEvents)
        {
            _events.TryRemove(eventId, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? calendarId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (startUtc >= endUtc)
        {
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>());
        }

        IEnumerable<CalendarEvent> query = _events.Values;

        if (!string.IsNullOrEmpty(calendarId))
        {
            query = query.Where(e => string.Equals(e.CalendarId, calendarId, StringComparison.Ordinal));
        }

        // Interval overlap condition:
        // Event overlaps [startUtc, endUtc) when event starts before query end AND ends after query start.
        // For zero-duration events, event starts within [startUtc, endUtc).
        var overlappingEvents = query
            .Where(e => e.StartUtc < endUtc &&
                        (e.EndUtc > startUtc || (e.StartUtc == e.EndUtc && e.StartUtc >= startUtc)))
            .OrderBy(e => e.StartUtc)
            .ThenBy(e => e.EndUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<CalendarEvent>>(overlappingEvents);
    }

    /// <inheritdoc/>
    public Task<CalendarEvent?> GetEventByIdAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        cancellationToken.ThrowIfCancellationRequested();

        _events.TryGetValue(eventId, out var calendarEvent);
        return Task.FromResult(calendarEvent);
    }

    /// <inheritdoc/>
    public Task UpsertEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarEvent.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarEvent.CalendarId);
        cancellationToken.ThrowIfCancellationRequested();

        _events[calendarEvent.Id] = calendarEvent;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        cancellationToken.ThrowIfCancellationRequested();

        _events.TryRemove(eventId, out _);
        return Task.CompletedTask;
    }

    private void SeedSampleData()
    {
        // 1. Calendars
        var googlePrimary = new CalendarInfo
        {
            Id = "cal-google-primary",
            AccountId = "acc-google-default",
            Summary = "Cá nhân · Google",
            Color = "#2F6BFF", // Figma 01 calendar colours
            IsSelected = true,
            IsPrimary = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            Description = "Lịch cá nhân mặc định",
        };

        var googleWork = new CalendarInfo
        {
            Id = "cal-google-work",
            AccountId = "acc-google-default",
            Summary = "Công việc · Google",
            Color = "#12B76A",
            IsSelected = true,
            IsPrimary = false,
            TimeZone = "Asia/Ho_Chi_Minh",
            Description = "Lịch công việc và dự án",
        };

        var lunarLocal = new CalendarInfo
        {
            Id = "cal-lunar-local",
            AccountId = "acc-local",
            Summary = "Âm lịch · cục bộ",
            Color = "#F79009",
            IsSelected = true,
            IsPrimary = false,
            TimeZone = "Asia/Ho_Chi_Minh",
            Description = "Sự kiện và ngày lễ âm lịch lưu trên máy",
        };

        var aiTasks = new CalendarInfo
        {
            Id = "cal-ai-tasks",
            AccountId = "acc-local",
            Summary = "Nhắc việc từ AI",
            Color = "#7A5AF8",
            IsSelected = true,
            IsPrimary = false,
            TimeZone = "Asia/Ho_Chi_Minh",
            Description = "Các sự kiện và nhắc việc do trợ lý AI tạo",
        };

        _calendars[googlePrimary.Id] = googlePrimary;
        _calendars[googleWork.Id] = googleWork;
        _calendars[lunarLocal.Id] = lunarLocal;
        _calendars[aiTasks.Id] = aiTasks;

        // 2. Events (seeded relative to current date in UTC+7 for immediate UI realism)
        var offset7 = TimeSpan.FromHours(7);
        var todayLocal = DateTimeOffset.UtcNow.ToOffset(offset7).Date;

        DateTimeOffset LocalTime(DateTime localDate, int hour, int minute) =>
            new DateTimeOffset(localDate.Year, localDate.Month, localDate.Day, hour, minute, 0, offset7).ToUniversalTime();

        var events = new[]
        {
            // Today: Work standup
            new CalendarEvent
            {
                Id = "evt-sample-standup",
                CalendarId = googleWork.Id,
                Title = "Họp giao ban đầu tuần",
                Description = "Đồng bộ tiến độ sprint và rà soát backlog các phase.",
                Location = "Google Meet",
                StartUtc = LocalTime(todayLocal, 9, 0),
                EndUtc = LocalTime(todayLocal, 10, 0),
                AllDay = false,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
            },

            // Today: LunarCal architecture meeting
            new CalendarEvent
            {
                Id = "evt-sample-arch",
                CalendarId = googleWork.Id,
                Title = "Thảo luận kiến trúc LunarCal",
                Description = "Rà soát port IEventStore và cấu trúc UI shell.",
                Location = "Phòng họp 201",
                StartUtc = LocalTime(todayLocal, 14, 0),
                EndUtc = LocalTime(todayLocal, 15, 30),
                AllDay = false,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
            },

            // Tomorrow: Personal dinner
            new CalendarEvent
            {
                Id = "evt-sample-dinner",
                CalendarId = googlePrimary.Id,
                Title = "Gặp gỡ bạn bè",
                Description = "Ăn tối cùng nhóm bạn thời đại học.",
                Location = "Nhà hàng Sen Tây Hồ",
                StartUtc = LocalTime(todayLocal.AddDays(1), 18, 30),
                EndUtc = LocalTime(todayLocal.AddDays(1), 20, 30),
                AllDay = false,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
            },

            // In 2 days: Full moon festival (Lunar event with lunarRuleId)
            new CalendarEvent
            {
                Id = "evt-sample-full-moon",
                CalendarId = lunarLocal.Id,
                Title = "Rằm tháng Chín (Âm lịch)",
                Description = "Ngày Rằm tháng 9 âm lịch. Nhớ chuẩn bị hoa quả thắp hương.",
                Location = null,
                StartUtc = LocalTime(todayLocal.AddDays(2), 0, 0),
                EndUtc = LocalTime(todayLocal.AddDays(3), 0, 0),
                AllDay = true,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
                ExtendedProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["lunarRuleId"] = "rule-ram-hang-thang",
                },
            },

            // In 3 days: AI created task
            new CalendarEvent
            {
                Id = "evt-sample-ai-call-mom",
                CalendarId = aiTasks.Id,
                Title = "Nhắc gọi điện hỏi thăm mẹ",
                Description = "Tạo tự động từ câu lệnh AI: 'Nhắc tôi gọi điện cho mẹ lúc 10h sáng'.",
                Location = null,
                StartUtc = LocalTime(todayLocal.AddDays(3), 10, 0),
                EndUtc = LocalTime(todayLocal.AddDays(3), 10, 30),
                AllDay = false,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
                ExtendedProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["createdBy"] = "lunarcal-chat",
                    ["batchId"] = "batch-seed-001",
                },
            },

            // Next week: Birthday (All-day)
            new CalendarEvent
            {
                Id = "evt-sample-birthday",
                CalendarId = googlePrimary.Id,
                Title = "Sinh nhật đồng nghiệp",
                Description = "Gửi lời chúc mừng sinh nhật.",
                Location = null,
                StartUtc = LocalTime(todayLocal.AddDays(6), 0, 0),
                EndUtc = LocalTime(todayLocal.AddDays(7), 0, 0),
                AllDay = true,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
            },

            // Fixed benchmark event for deterministic test assertions
            new CalendarEvent
            {
                Id = "evt-sample-fixed-2026-11-15",
                CalendarId = googlePrimary.Id,
                Title = "Sự kiện mốc cố định tháng 11/2026",
                Description = "Sự kiện cố định phục vụ unit test và benchmark.",
                Location = "Hà Nội",
                StartUtc = new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero),
                EndUtc = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero),
                AllDay = false,
                TimeZone = "Asia/Ho_Chi_Minh",
                Status = "confirmed",
            },
        };

        foreach (var ev in events)
        {
            _events[ev.Id] = ev;
        }
    }
}
