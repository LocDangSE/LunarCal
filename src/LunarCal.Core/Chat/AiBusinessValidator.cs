using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LunarCal.Core.Abstractions;

namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CONFIRM-5: Default implementation of <see cref="IAiBusinessValidator"/>.
/// Evaluates business rules locally on SQLite cache without cloud data leaks.
/// </summary>
public sealed class AiBusinessValidator : IAiBusinessValidator
{
    /// <inheritdoc />
    public async Task<AiBusinessValidationResult> ValidateAsync(
        ToolCall toolCall,
        IEventStore eventStore,
        DateTimeOffset typedAt,
        string? targetCalendarId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(eventStore);

        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            return AiBusinessValidationResult.WithErrors(
            [
                new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Dữ liệu yêu cầu trống.")
            ]);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(toolCall.ArgumentsJson);
        }
        catch (JsonException ex)
        {
            return AiBusinessValidationResult.WithErrors(
            [
                new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, $"Cấu trúc yêu cầu không hợp lệ: {ex.Message}")
            ]);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var name = toolCall.Name;

            if (string.Equals(name, ToolDefinitions.CreateEvent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ToolDefinitions.CreateRecurringEvent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ToolDefinitions.CreateLunarRecurringEvent, StringComparison.OrdinalIgnoreCase))
            {
                return await ValidateSingleEventCreationAsync(root, eventStore, typedAt, targetCalendarId, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.Equals(name, ToolDefinitions.UpdateEvent, StringComparison.OrdinalIgnoreCase))
            {
                var eventId = root.TryGetProperty("event_id", out var idProp) ? idProp.GetString() : null;
                return await ValidateSingleEventCreationAsync(root, eventStore, typedAt, targetCalendarId, eventId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.Equals(name, ToolDefinitions.DeleteEvent, StringComparison.OrdinalIgnoreCase))
            {
                var eventId = root.TryGetProperty("event_id", out var idProp) ? idProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    return AiBusinessValidationResult.WithErrors(
                    [
                        new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Thiếu mã sự kiện (event_id) cần xoá.")
                    ]);
                }

                return AiBusinessValidationResult.Success("Đã qua kiểm tra nghiệp vụ: lệnh xoá hợp lệ.");
            }

            if (string.Equals(name, ToolDefinitions.BulkCreateEvents, StringComparison.OrdinalIgnoreCase))
            {
                return await ValidateBulkCreationAsync(root, eventStore, typedAt, targetCalendarId, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Default fallback for other tools (e.g. list_events)
            return AiBusinessValidationResult.Success();
        }
    }

    private static async Task<AiBusinessValidationResult> ValidateSingleEventCreationAsync(
        JsonElement root,
        IEventStore eventStore,
        DateTimeOffset typedAt,
        string? targetCalendarId,
        string? excludedEventId,
        CancellationToken cancellationToken)
    {
        var messages = new List<AiBusinessValidationMessage>();

        // 1. Validate summary
        if (!root.TryGetProperty("summary", out var summaryProp) || string.IsNullOrWhiteSpace(summaryProp.GetString()))
        {
            messages.Add(new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Tiêu đề sự kiện không được để trống."));
        }

        // 2. Validate start time
        if (!root.TryGetProperty("start", out var startProp) || string.IsNullOrWhiteSpace(startProp.GetString()))
        {
            messages.Add(new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Thời gian bắt đầu sự kiện bị thiếu."));
            return AiBusinessValidationResult.WithErrors(messages);
        }

        if (!DateTimeOffset.TryParse(startProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            messages.Add(new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Định dạng thời gian bắt đầu không hợp lệ."));
            return AiBusinessValidationResult.WithErrors(messages);
        }

        DateTimeOffset end;
        var hasExplicitEnd = root.TryGetProperty("end", out var endProp) && !string.IsNullOrWhiteSpace(endProp.GetString());

        if (hasExplicitEnd)
        {
            if (!DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
            {
                messages.Add(new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Định dạng thời gian kết thúc không hợp lệ."));
                return AiBusinessValidationResult.WithErrors(messages);
            }

            if (end <= start)
            {
                messages.Add(new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Giờ kết thúc phải sau giờ bắt đầu."));
            }
        }
        else
        {
            // Default 1 hour duration
            end = start.AddHours(1);
        }

        // 3. Past event warning
        if (start < typedAt.AddMinutes(-5))
        {
            messages.Add(new AiBusinessValidationMessage(
                AiBusinessValidationSeverity.Warning,
                "Lưu ý: Sự kiện được đặt ở thời điểm trong quá khứ."));
        }

        // If there are blocking errors, return immediately before conflict check
        if (messages.Any(m => m.Severity == AiBusinessValidationSeverity.Error))
        {
            return AiBusinessValidationResult.WithErrors(messages);
        }

        // 4. Schedule conflict check against SQLite cache
        var queryStartUtc = start.ToUniversalTime();
        var queryEndUtc = end.ToUniversalTime();

        try
        {
            var overlappingEvents = await eventStore.GetEventsAsync(
                queryStartUtc,
                queryEndUtc,
                targetCalendarId,
                cancellationToken).ConfigureAwait(false);

            foreach (var conflict in overlappingEvents)
            {
                // Skip the event itself if updating
                if (!string.IsNullOrEmpty(excludedEventId) && string.Equals(conflict.Id, excludedEventId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If not an exact edge match (e.g. existing ends right when new starts), flag conflict
                var conflictStart = conflict.StartUtc;
                var conflictEnd = conflict.EndUtc;

                if (conflictStart < queryEndUtc && conflictEnd > queryStartUtc)
                {
                    var title = string.IsNullOrWhiteSpace(conflict.Title) ? "Không có tiêu đề" : conflict.Title;
                    var localStart = conflictStart.ToOffset(typedAt.Offset);
                    var localEnd = conflictEnd.ToOffset(typedAt.Offset);

                    messages.Add(new AiBusinessValidationMessage(
                        AiBusinessValidationSeverity.Warning,
                        $"Trùng giờ với sự kiện '{title}' ({localStart:HH:mm} – {localEnd:HH:mm}).",
                        title,
                        conflictStart,
                        conflictEnd));
                }
            }
        }
        catch (Exception ex)
        {
            messages.Add(new AiBusinessValidationMessage(
                AiBusinessValidationSeverity.Warning,
                $"Không thể kiểm tra trùng lịch: {ex.Message}"));
        }

        if (messages.Count == 0)
        {
            return AiBusinessValidationResult.Success();
        }

        return AiBusinessValidationResult.WithWarnings(messages);
    }

    private static async Task<AiBusinessValidationResult> ValidateBulkCreationAsync(
        JsonElement root,
        IEventStore eventStore,
        DateTimeOffset typedAt,
        string? targetCalendarId,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("events", out var eventsProp) || eventsProp.ValueKind != JsonValueKind.Array)
        {
            return AiBusinessValidationResult.WithErrors(
            [
                new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Danh sách sự kiện bulk_create_events không hợp lệ.")
            ]);
        }

        var count = eventsProp.GetArrayLength();
        if (count == 0)
        {
            return AiBusinessValidationResult.WithErrors(
            [
                new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Danh sách sự kiện tạo hàng loạt rỗng.")
            ]);
        }

        if (count > ToolDefinitions.MaxBulkEvents)
        {
            return AiBusinessValidationResult.WithErrors(
            [
                new AiBusinessValidationMessage(
                    AiBusinessValidationSeverity.Error,
                    $"Số lượng sự kiện vượt quá giới hạn tối đa cho phép ({ToolDefinitions.MaxBulkEvents} sự kiện).")
            ]);
        }

        var allMessages = new List<AiBusinessValidationMessage>();

        foreach (var eventElement in eventsProp.EnumerateArray())
        {
            var singleResult = await ValidateSingleEventCreationAsync(
                eventElement,
                eventStore,
                typedAt,
                targetCalendarId,
                null,
                cancellationToken).ConfigureAwait(false);

            if (singleResult.HasError)
            {
                allMessages.AddRange(singleResult.Messages.Where(m => m.Severity == AiBusinessValidationSeverity.Error));
            }
            else if (singleResult.Severity == AiBusinessValidationSeverity.Warning)
            {
                allMessages.AddRange(singleResult.Messages.Where(m => m.Severity == AiBusinessValidationSeverity.Warning));
            }
        }

        if (allMessages.Any(m => m.Severity == AiBusinessValidationSeverity.Error))
        {
            return AiBusinessValidationResult.WithErrors(allMessages);
        }

        if (allMessages.Count > 0)
        {
            return AiBusinessValidationResult.WithWarnings(allMessages);
        }

        return AiBusinessValidationResult.Success($"Đã qua kiểm tra nghiệp vụ cho {count} sự kiện.");
    }
}
