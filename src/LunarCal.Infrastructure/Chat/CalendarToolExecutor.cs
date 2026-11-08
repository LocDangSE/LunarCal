using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Chat;

namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-CHAT-1, FR-CHAT-2, SEC-PI-2: Runs a confirmed tool call against the calendar through the same optimistic
/// mutation path as the event editor. Phase 4 covers <c>create_event</c> only (spec §13); the other tools arrive with
/// Phase 5 and are refused here rather than half-implemented. Callers must only invoke it after user confirmation
/// (<see cref="AiCommandConfirmationService"/>, or the orchestrator's explicit auto-confirm option).
/// </summary>
public sealed class CalendarToolExecutor : IToolExecutor
{
    private const string PrimaryAlias = "primary";

    private readonly IEventStore _eventStore;
    private readonly IMutationService _mutationService;

    public CalendarToolExecutor(IEventStore eventStore, IMutationService mutationService)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
    }

    /// <inheritdoc />
    public async Task<ToolExecutionResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!string.Equals(toolCall.Name, ToolDefinitions.CreateEvent, StringComparison.OrdinalIgnoreCase))
        {
            return ToolExecutionResult.Failure($"The action '{toolCall.Name}' is not supported yet.");
        }

        using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
        var root = doc.RootElement;

        var title = ToolArgumentReader.GetTitle(root);
        if (title is null || !TryGetDateTime(root, "start", out var start))
        {
            return ToolExecutionResult.Failure("The event needs a title and a start time.");
        }

        var calendar = await ResolveCalendarAsync(GetString(root, "calendarId"), cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            return ToolExecutionResult.Failure("No writable calendar is available.");
        }

        var allDay = ToolArgumentReader.IsAllDay(root);
        var timeZone = ResolveTimeZone(GetString(root, "timezone"));
        var hasEnd = TryGetDateTime(root, "end", out var end);
        DateTimeOffset startUtc;
        DateTimeOffset endUtc;
        if (allDay)
        {
            var startDate = DateOnly.FromDateTime(start.DateTime);
            var endDate = hasEnd ? DateOnly.FromDateTime(end.DateTime) : startDate;
            startUtc = CalendarTime.StartOfDayUtc(startDate, timeZone);
            endUtc = CalendarTime.StartOfDayUtc(endDate.AddDays(1), timeZone);
        }
        else
        {
            startUtc = start.ToUniversalTime();
            endUtc = hasEnd ? end.ToUniversalTime() : startUtc.AddHours(1);
        }

        var extended = new Dictionary<string, string>(StringComparer.Ordinal) { ["createdBy"] = "lunarcal-ai" };
        if (!string.IsNullOrWhiteSpace(toolCall.Id))
        {
            extended["requestId"] = toolCall.Id;
        }

        var calendarEvent = new CalendarEvent
        {
            Id = BuildEventId(toolCall.Id),
            CalendarId = calendar.Id,
            Title = title.Trim(),
            Description = GetString(root, "description"),
            Location = GetString(root, "location"),
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = allDay,
            TimeZone = timeZone.Id,
            Status = "confirmed",
            ReminderMinutes = GetReminders(root),
            ExtendedProperties = extended
        };

        var created = await _mutationService
            .CreateEventOptimisticAsync(calendar.AccountId, calendar.Id, calendarEvent, cancellationToken)
            .ConfigureAwait(false);

        return ToolExecutionResult.Success(created.Title, [created.Id]);
    }

    private async Task<CalendarInfo?> ResolveCalendarAsync(string? requested, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested) && !string.Equals(requested, PrimaryAlias, StringComparison.OrdinalIgnoreCase))
        {
            var byId = await _eventStore.GetCalendarByIdAsync(requested, cancellationToken).ConfigureAwait(false);
            if (byId is { IsReadOnly: false })
            {
                return byId;
            }
        }

        var calendars = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var writable = calendars.Where(c => !c.IsReadOnly).ToList();
        return writable.FirstOrDefault(c => c.IsPrimary) ?? writable.FirstOrDefault();
    }

    /// <summary>Derives a stable event id from the request id so a repeated confirmation upserts the same event.</summary>
    private static string BuildEventId(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return Guid.NewGuid().ToString("D");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(requestId));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // The model may return an IANA id Windows does not know; use the machine zone below.
            }
            catch (InvalidTimeZoneException)
            {
                // Same fallback.
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.GetString())
            ? prop.GetString()!.Trim()
            : null;

    private static bool TryGetDateTime(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        var text = GetString(root, name);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static List<int>? GetReminders(JsonElement root)
    {
        if (!root.TryGetProperty("reminders", out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var m) && m >= 0)
            .Select(e => e.GetInt32())
            .Distinct()
            .Order()
            .ToList();
    }
}
