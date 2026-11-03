using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Google Calendar API v3 REST Gateway (FR-SYNC-1, FR-SYNC-4, FR-SYNC-6, ADR-0028).
/// </summary>
public sealed class CalendarGateway : ICalendarGateway
{
    private const string BaseUrl = "https://www.googleapis.com/calendar/v3";
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public CalendarGateway(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? Log.ForContext<CalendarGateway>();
    }

    /// <inheritdoc />
    public async Task<CalendarListResult> GetCalendarListAsync(
        string accessToken,
        string? pageToken = null,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var query = new List<string> { "maxResults=250", "minAccessRole=reader", "showHidden=false" };
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        }
        if (!string.IsNullOrWhiteSpace(syncToken))
        {
            query.Add($"syncToken={Uri.EscapeDataString(syncToken)}");
        }

        var url = $"{BaseUrl}/users/me/calendarList?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Gone)
        {
            throw new GoogleCalendarSyncTokenExpiredException("users/me/calendarList");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google CalendarList API returned error status {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Google CalendarList API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        var etag = root.TryGetProperty("etag", out var etagProp) ? etagProp.GetString() : null;
        var nextPageToken = root.TryGetProperty("nextPageToken", out var nptProp) ? nptProp.GetString() : null;
        var nextSyncToken = root.TryGetProperty("nextSyncToken", out var nstProp) ? nstProp.GetString() : null;

        var calendars = new List<CalendarInfo>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var summary = item.TryGetProperty("summary", out var sProp) ? sProp.GetString() ?? id : id;
                var color = item.TryGetProperty("backgroundColor", out var bgProp)
                    ? bgProp.GetString()
                    : (item.TryGetProperty("colorId", out var cProp) ? cProp.GetString() : null);
                var selected = !item.TryGetProperty("selected", out var selProp) || selProp.GetBoolean();
                var primary = item.TryGetProperty("primary", out var priProp) && priProp.GetBoolean();
                var accessRole = item.TryGetProperty("accessRole", out var arProp) ? arProp.GetString() : null;
                var isReadOnly = string.Equals(accessRole, "reader", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(accessRole, "freeBusyReader", StringComparison.OrdinalIgnoreCase);
                var timeZone = item.TryGetProperty("timeZone", out var tzProp) ? tzProp.GetString() : null;

                calendars.Add(new CalendarInfo
                {
                    Id = id,
                    AccountId = string.Empty, // Populated by caller/SyncEngine
                    Summary = summary,
                    Color = color,
                    IsSelected = selected,
                    IsPrimary = primary,
                    IsReadOnly = isReadOnly,
                    TimeZone = timeZone,
                    SyncToken = null
                });
            }
        }

        return new CalendarListResult
        {
            Calendars = calendars,
            NextPageToken = nextPageToken,
            NextSyncToken = nextSyncToken,
            Etag = etag
        };
    }

    /// <inheritdoc />
    public async Task<EventsListResult> GetEventsAsync(
        string accessToken,
        string calendarId,
        bool singleEvents = true,
        DateTimeOffset? timeMin = null,
        DateTimeOffset? timeMax = null,
        string? pageToken = null,
        string? syncToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);

        var query = new List<string>
        {
            "maxResults=250",
            $"singleEvents={(singleEvents ? "true" : "false")}"
        };

        if (!string.IsNullOrWhiteSpace(syncToken))
        {
            query.Add($"syncToken={Uri.EscapeDataString(syncToken)}");
        }
        else
        {
            if (timeMin.HasValue)
            {
                query.Add($"timeMin={Uri.EscapeDataString(timeMin.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}");
            }
            if (timeMax.HasValue)
            {
                query.Add($"timeMax={Uri.EscapeDataString(timeMax.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}");
            }
        }

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        }

        var encodedCalId = Uri.EscapeDataString(calendarId);
        var url = $"{BaseUrl}/calendars/{encodedCalId}/events?{string.Join("&", query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Gone)
        {
            throw new GoogleCalendarSyncTokenExpiredException(calendarId);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google Events API returned error status {StatusCode} for calendar {CalendarId}", response.StatusCode, calendarId);
            throw new HttpRequestException($"Google Events API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        var etag = root.TryGetProperty("etag", out var etagProp) ? etagProp.GetString() : null;
        var nextPageToken = root.TryGetProperty("nextPageToken", out var nptProp) ? nptProp.GetString() : null;
        var nextSyncToken = root.TryGetProperty("nextSyncToken", out var nstProp) ? nstProp.GetString() : null;

        var events = new List<CalendarEvent>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var evt = ParseCalendarEvent(item, calendarId);
                if (evt is not null)
                {
                    events.Add(evt);
                }
            }
        }

        return new EventsListResult
        {
            Events = events,
            NextPageToken = nextPageToken,
            NextSyncToken = nextSyncToken,
            Etag = etag
        };
    }

    /// <inheritdoc />
    public async Task<CalendarEvent> CreateEventAsync(
        string accessToken,
        string calendarId,
        CalendarEvent calendarEvent,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var encodedCalId = Uri.EscapeDataString(calendarId);
        var url = $"{BaseUrl}/calendars/{encodedCalId}/events";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(clientRequestId))
        {
            request.Headers.TryAddWithoutValidation("X-Request-Id", clientRequestId);
        }
        request.Content = BuildEventJsonContent(calendarEvent);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google CreateEvent API returned error status {StatusCode} for calendar {CalendarId}", response.StatusCode, calendarId);
            throw new HttpRequestException($"Google CreateEvent API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var created = ParseCalendarEvent(document.RootElement, calendarId);
        return created ?? throw new InvalidOperationException("Failed to parse created event from Google Calendar API response.");
    }

    /// <inheritdoc />
    public async Task<CalendarEvent> UpdateEventAsync(
        string accessToken,
        string calendarId,
        CalendarEvent calendarEvent,
        string? expectedEtag = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var eventId = !string.IsNullOrWhiteSpace(calendarEvent.GoogleId) ? calendarEvent.GoogleId : calendarEvent.Id;
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        var encodedCalId = Uri.EscapeDataString(calendarId);
        var encodedEventId = Uri.EscapeDataString(eventId);
        var url = $"{BaseUrl}/calendars/{encodedCalId}/events/{encodedEventId}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(expectedEtag))
        {
            var formattedEtag = expectedEtag.StartsWith('"') ? expectedEtag : $"\"{expectedEtag}\"";
            request.Headers.TryAddWithoutValidation("If-Match", formattedEtag);
        }
        if (!string.IsNullOrWhiteSpace(clientRequestId))
        {
            request.Headers.TryAddWithoutValidation("X-Request-Id", clientRequestId);
        }
        request.Content = BuildEventJsonContent(calendarEvent);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.Warning("Precondition failed (HTTP 412) updating event {EventId} in calendar {CalendarId} (FR-SYNC-4)", eventId, calendarId);
            throw new GooglePreconditionFailedException(calendarId, eventId, expectedEtag);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google UpdateEvent API returned error status {StatusCode} for event {EventId}", response.StatusCode, eventId);
            throw new HttpRequestException($"Google UpdateEvent API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var updated = ParseCalendarEvent(document.RootElement, calendarId);
        return updated ?? throw new InvalidOperationException("Failed to parse updated event from Google Calendar API response.");
    }

    /// <inheritdoc />
    public async Task DeleteEventAsync(
        string accessToken,
        string calendarId,
        string eventId,
        string? expectedEtag = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        var encodedCalId = Uri.EscapeDataString(calendarId);
        var encodedEventId = Uri.EscapeDataString(eventId);
        var url = $"{BaseUrl}/calendars/{encodedCalId}/events/{encodedEventId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(expectedEtag))
        {
            var formattedEtag = expectedEtag.StartsWith('"') ? expectedEtag : $"\"{expectedEtag}\"";
            request.Headers.TryAddWithoutValidation("If-Match", formattedEtag);
        }
        if (!string.IsNullOrWhiteSpace(clientRequestId))
        {
            request.Headers.TryAddWithoutValidation("X-Request-Id", clientRequestId);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.Warning("Precondition failed (HTTP 412) deleting event {EventId} in calendar {CalendarId} (FR-SYNC-4)", eventId, calendarId);
            throw new GooglePreconditionFailedException(calendarId, eventId, expectedEtag);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Already deleted on server
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google DeleteEvent API returned error status {StatusCode} for event {EventId}", response.StatusCode, eventId);
            throw new HttpRequestException($"Google DeleteEvent API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }
    }

    /// <inheritdoc />
    public async Task<CalendarEvent?> GetEventAsync(
        string accessToken,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        var encodedCalId = Uri.EscapeDataString(calendarId);
        var encodedEventId = Uri.EscapeDataString(eventId);
        var url = $"{BaseUrl}/calendars/{encodedCalId}/events/{encodedEventId}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning("Google GetEvent API returned error status {StatusCode} for event {EventId}", response.StatusCode, eventId);
            throw new HttpRequestException($"Google GetEvent API error (status {response.StatusCode}): {errorContent}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ParseCalendarEvent(document.RootElement, calendarId);
    }

    private static StringContent BuildEventJsonContent(CalendarEvent @event)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("summary", @event.Title);
            if (!string.IsNullOrWhiteSpace(@event.Description))
            {
                writer.WriteString("description", @event.Description);
            }
            if (!string.IsNullOrWhiteSpace(@event.Location))
            {
                writer.WriteString("location", @event.Location);
            }
            if (!string.IsNullOrWhiteSpace(@event.Status))
            {
                writer.WriteString("status", @event.Status);
            }

            // Start
            writer.WriteStartObject("start");
            if (@event.AllDay)
            {
                writer.WriteString("date", @event.StartUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteString("dateTime", @event.StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(@event.TimeZone))
            {
                writer.WriteString("timeZone", @event.TimeZone);
            }
            writer.WriteEndObject();

            // End
            writer.WriteStartObject("end");
            if (@event.AllDay)
            {
                writer.WriteString("date", @event.EndUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteString("dateTime", @event.EndUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrWhiteSpace(@event.TimeZone))
            {
                writer.WriteString("timeZone", @event.TimeZone);
            }
            writer.WriteEndObject();

            // Extended properties (FR-SYNC-6 private extendedProperties)
            if (@event.ExtendedProperties.Count > 0)
            {
                writer.WriteStartObject("extendedProperties");
                writer.WriteStartObject("private");
                foreach (var kvp in @event.ExtendedProperties)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/json");
    }

    private static CalendarEvent? ParseCalendarEvent(JsonElement item, string calendarId)
    {
        if (!item.TryGetProperty("id", out var idProp) || idProp.GetString() is not { } id || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var summary = item.TryGetProperty("summary", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;
        var description = item.TryGetProperty("description", out var dProp) ? dProp.GetString() : null;
        var location = item.TryGetProperty("location", out var lProp) ? lProp.GetString() : null;
        var status = item.TryGetProperty("status", out var stProp) ? stProp.GetString() : "confirmed";
        var itemEtag = item.TryGetProperty("etag", out var ieProp) ? ieProp.GetString() : null;
        var recurringEventId = item.TryGetProperty("recurringEventId", out var rProp) ? rProp.GetString() : null;

        DateTimeOffset? updated = null;
        if (item.TryGetProperty("updated", out var uProp) && uProp.GetString() is { } uStr)
        {
            if (DateTimeOffset.TryParse(uStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var uDt))
            {
                updated = uDt;
            }
        }

        // Parse Start and End (timed vs all-day)
        DateTimeOffset startUtc = DateTimeOffset.UtcNow;
        DateTimeOffset endUtc = startUtc.AddHours(1);
        bool allDay = false;
        string? timeZone = null;

        if (item.TryGetProperty("start", out var startObj))
        {
            if (startObj.TryGetProperty("dateTime", out var startDtProp) && startDtProp.GetString() is { } sDtStr)
            {
                if (DateTimeOffset.TryParse(sDtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    startUtc = dt.ToUniversalTime();
                }
            }
            else if (startObj.TryGetProperty("date", out var startDateProp) && startDateProp.GetString() is { } sDStr)
            {
                if (DateOnly.TryParseExact(sDStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dOnly))
                {
                    startUtc = new DateTimeOffset(dOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                    allDay = true;
                }
            }

            if (startObj.TryGetProperty("timeZone", out var stzProp))
            {
                timeZone = stzProp.GetString();
            }
        }

        if (item.TryGetProperty("end", out var endObj))
        {
            if (endObj.TryGetProperty("dateTime", out var endDtProp) && endDtProp.GetString() is { } eDtStr)
            {
                if (DateTimeOffset.TryParse(eDtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    endUtc = dt.ToUniversalTime();
                }
            }
            else if (endObj.TryGetProperty("date", out var endDateProp) && endDateProp.GetString() is { } eDStr)
            {
                if (DateOnly.TryParseExact(eDStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dOnly))
                {
                    endUtc = new DateTimeOffset(dOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                }
            }

            if (string.IsNullOrWhiteSpace(timeZone) && endObj.TryGetProperty("timeZone", out var etzProp))
            {
                timeZone = etzProp.GetString();
            }
        }

        if (endUtc <= startUtc && !allDay)
        {
            endUtc = startUtc.AddHours(1);
        }

        // Extended properties (FR-SYNC-6 private extendedProperties)
        var extDict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.TryGetProperty("extendedProperties", out var extProp) &&
            extProp.TryGetProperty("private", out var privProp) &&
            privProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in privProp.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { } val)
                {
                    extDict[prop.Name] = val;
                }
            }
        }

        return new CalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            GoogleId = id,
            RecurringEventId = recurringEventId,
            Title = summary,
            Description = description,
            Location = location,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = allDay,
            TimeZone = timeZone,
            Etag = itemEtag,
            Updated = updated,
            Status = status,
            ExtendedProperties = extDict
        };
    }
}
