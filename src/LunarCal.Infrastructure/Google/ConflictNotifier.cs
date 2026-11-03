using LunarCal.Core.Abstractions;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Thread-safe in-memory publisher for mutation conflict notifications (FR-SYNC-4).
/// </summary>
public sealed class ConflictNotifier : IConflictNotifier
{
    private readonly ILogger _logger;

    public event EventHandler<EventConflictNotice>? ConflictOccurred;

    public ConflictNotifier(ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<ConflictNotifier>();
    }

    public void NotifyConflict(EventConflictNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        _logger.Warning("Mutation conflict occurred for event {EventId} in calendar {CalendarId}. Reason: {Reason} (FR-SYNC-4)",
            notice.EventId, notice.CalendarId, notice.ConflictReason);

        ConflictOccurred?.Invoke(this, notice);
    }
}
