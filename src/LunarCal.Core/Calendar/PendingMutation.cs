namespace LunarCal.Core.Calendar;

/// <summary>
/// Mutation kinds stored in PendingMutation queue for offline synchronization (FR-SYNC-4, NFR-REL).
/// </summary>
public enum MutationKind
{
    /// <summary>Create a new calendar event on remote provider.</summary>
    CreateEvent = 1,

    /// <summary>Update an existing calendar event on remote provider.</summary>
    UpdateEvent = 2,

    /// <summary>Delete an existing calendar event on remote provider.</summary>
    DeleteEvent = 3
}

/// <summary>
/// Represents a queued offline mutation waiting to be dispatched to Google Calendar API (spec §6, FR-SYNC-4, NFR-REL).
/// </summary>
public sealed class PendingMutation
{
    /// <summary>
    /// Unique mutation identifier (UUID).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Type of mutation (CreateEvent, UpdateEvent, DeleteEvent).
    /// </summary>
    public required MutationKind Kind { get; init; }

    /// <summary>
    /// Serialized JSON payload containing mutation details.
    /// </summary>
    public required string PayloadJson { get; init; }

    /// <summary>
    /// Creation timestamp in UTC (spec §6 created_at).
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Number of delivery attempts made so far (spec §6 attempts).
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Last error message encountered during delivery attempt (spec §6 last_error).
    /// </summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Deserialized payload payload data for event mutations.
/// </summary>
public sealed class EventMutationPayload
{
    /// <summary>Account identifier owning the calendar.</summary>
    public required string AccountId { get; init; }

    /// <summary>Target calendar identifier.</summary>
    public required string CalendarId { get; init; }

    /// <summary>Event identifier being created, modified or deleted.</summary>
    public required string EventId { get; init; }

    /// <summary>Full event entity (populated for create and update).</summary>
    public CalendarEvent? Event { get; init; }

    /// <summary>Original ETag before mutation (used for If-Match concurrency checks per FR-SYNC-4).</summary>
    public string? OriginalEtag { get; init; }

    /// <summary>Idempotency client request identifier (SEC-NET-2, X-Request-Id).</summary>
    public string? ClientRequestId { get; init; }
}
