using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Google Calendar API v3 REST Gateway (FR-SYNC-1, FR-SYNC-6, ADR-0028).
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
            // When syncToken is present, Google API forbids timeMin and timeMax filters (HTTP 400 Bad Request)
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
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
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

                events.Add(new CalendarEvent
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
                });
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
}
