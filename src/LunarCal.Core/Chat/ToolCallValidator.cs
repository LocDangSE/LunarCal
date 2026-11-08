using System.Globalization;
using System.Text.Json;

namespace LunarCal.Core.Chat;

/// <summary>
/// Validates model tool invocations against schema specifications and security constraints (FR-CHAT §4.6, SEC-PI-2, FR-CHAT-3).
/// Enforces limits: <= 500 events per bulk, <= 50 calls per session, <= 4000 chars per string, <= 5 years duration, no mass deletion.
/// </summary>
public static class ToolCallValidator
{
    private static readonly TimeSpan _maxTimespan = TimeSpan.FromDays(ToolDefinitions.MaxTimespanYears * 366);

    /// <summary>
    /// Validates a single tool call requested by the AI model.
    /// </summary>
    /// <param name="toolCall">The tool call to validate.</param>
    /// <param name="currentSessionCallCount">The count of tool calls already executed in the current session (default 0).</param>
    /// <returns>A <see cref="ToolValidationResult"/> indicating whether the request is valid or the reason for rejection.</returns>
    public static ToolValidationResult Validate(ToolCall? toolCall, int currentSessionCallCount = 0)
    {
        // 1. Session call count limit (FR-CHAT-3: max 50 tool calls per session)
        if (currentSessionCallCount >= ToolDefinitions.MaxToolCallsPerSession)
        {
            return ToolValidationResult.Failure(
                $"Session tool call limit reached ({ToolDefinitions.MaxToolCallsPerSession} calls). Further tool calls are blocked.",
                ToolValidationCode.SessionCallLimitExceeded);
        }

        if (toolCall == null || string.IsNullOrWhiteSpace(toolCall.Name))
        {
            return ToolValidationResult.Failure("Tool call name is missing.", ToolValidationCode.UnknownTool);
        }

        // 2. Known tool verification
        if (!ToolDefinitions.IsKnownTool(toolCall.Name))
        {
            return ToolValidationResult.Failure(
                $"Tool '{toolCall.Name}' is unknown. Allowed tools are: {string.Join(", ", ToolDefinitions.AllTools.Select(t => t.Name))}.",
                ToolValidationCode.UnknownTool);
        }

        // 3. JSON parse and arguments verification
        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            return ToolValidationResult.Failure("Tool call arguments JSON is empty.", ToolValidationCode.InvalidJson);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(toolCall.ArgumentsJson);
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure(
                $"Arguments JSON is malformed: {ex.Message}",
                ToolValidationCode.InvalidJson);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ToolValidationResult.Failure(
                    "Tool call arguments must be a JSON object.",
                    ToolValidationCode.InvalidJson);
            }

            // 4. String length safety checks across all string fields (SEC-PI-2, FR-CHAT-3: max 4000 chars)
            if (CheckAnyStringExceedsMaxLength(doc.RootElement, ToolDefinitions.MaxStringLength, out var offendingProperty))
            {
                return ToolValidationResult.Failure(
                    $"Field '{offendingProperty}' exceeds the maximum allowed length of {ToolDefinitions.MaxStringLength} characters.",
                    ToolValidationCode.StringLengthExceeded);
            }

            // 5. Tool-specific schema and business policy validations
            return toolCall.Name.ToLowerInvariant() switch
            {
                ToolDefinitions.CreateEvent => ValidateCreateEvent(doc.RootElement),
                ToolDefinitions.CreateRecurringEvent => ValidateCreateRecurringEvent(doc.RootElement),
                ToolDefinitions.CreateLunarRecurringEvent => ValidateCreateLunarRecurringEvent(doc.RootElement),
                ToolDefinitions.BulkCreateEvents => ValidateBulkCreateEvents(doc.RootElement),
                ToolDefinitions.ListEvents => ValidateListEvents(doc.RootElement),
                ToolDefinitions.UpdateEvent => ValidateUpdateEvent(doc.RootElement),
                ToolDefinitions.DeleteEvent => ValidateDeleteEvent(doc.RootElement),
                _ => ToolValidationResult.Failure($"Unsupported tool: {toolCall.Name}", ToolValidationCode.UnknownTool)
            };
        }
    }

    private static ToolValidationResult ValidateCreateEvent(JsonElement root)
    {
        if (!TryGetRequiredString(root, "title", out var title, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!TryGetRequiredDateTime(root, "start", out var start, out error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (root.TryGetProperty("end", out var endProp) && endProp.ValueKind == JsonValueKind.String)
        {
            if (!DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return ToolValidationResult.Failure("Field 'end' is not a valid ISO 8601 date.", ToolValidationCode.InvalidDateRange);
            }

            if (end < start)
            {
                return ToolValidationResult.Failure("Field 'end' cannot be earlier than 'start'.", ToolValidationCode.InvalidDateRange);
            }

            if (end - start > _maxTimespan)
            {
                return ToolValidationResult.Failure($"Event duration exceeds the 5-year maximum limit.", ToolValidationCode.TimespanLimitExceeded);
            }
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateCreateRecurringEvent(JsonElement root)
    {
        if (!TryGetRequiredString(root, "title", out _, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!TryGetRequiredDateTime(root, "start", out var start, out error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!TryGetRequiredString(root, "rrule", out var rrule, out error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!rrule.Contains("FREQ=", StringComparison.OrdinalIgnoreCase))
        {
            return ToolValidationResult.Failure("Field 'rrule' must contain a valid FREQ rule (e.g. DAILY, WEEKLY, MONTHLY, YEARLY).", ToolValidationCode.PolicyViolation);
        }

        if (root.TryGetProperty("until", out var untilProp) && untilProp.ValueKind == JsonValueKind.String)
        {
            if (!DateTimeOffset.TryParse(untilProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
            {
                return ToolValidationResult.Failure("Field 'until' is not a valid ISO 8601 date.", ToolValidationCode.InvalidDateRange);
            }

            if (until < start)
            {
                return ToolValidationResult.Failure("Field 'until' cannot be earlier than 'start'.", ToolValidationCode.InvalidDateRange);
            }

            if (until - start > _maxTimespan)
            {
                return ToolValidationResult.Failure("Recurrence duration exceeds the 5-year maximum limit.", ToolValidationCode.TimespanLimitExceeded);
            }
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateCreateLunarRecurringEvent(JsonElement root)
    {
        if (!TryGetRequiredString(root, "title", out _, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!root.TryGetProperty("lunarDay", out var dayProp) || dayProp.ValueKind != JsonValueKind.Number || !dayProp.TryGetInt32(out var day))
        {
            return ToolValidationResult.Failure("Field 'lunarDay' is required and must be an integer.", ToolValidationCode.MissingRequiredParameter);
        }

        if (day is < 1 or > 30)
        {
            return ToolValidationResult.Failure("Field 'lunarDay' must be between 1 and 30.", ToolValidationCode.InvalidLunarParameters);
        }

        if (root.TryGetProperty("lunarMonth", out var monthProp) && monthProp.ValueKind != JsonValueKind.Null)
        {
            if (monthProp.ValueKind != JsonValueKind.Number || !monthProp.TryGetInt32(out var month) || month is < 1 or > 12)
            {
                return ToolValidationResult.Failure("Field 'lunarMonth' must be an integer between 1 and 12.", ToolValidationCode.InvalidLunarParameters);
            }
        }

        if (root.TryGetProperty("yearsAhead", out var yearsProp) && yearsProp.ValueKind != JsonValueKind.Null)
        {
            if (yearsProp.ValueKind != JsonValueKind.Number || !yearsProp.TryGetInt32(out var years) || years is < 1 or > ToolDefinitions.MaxLunarYearsAhead)
            {
                return ToolValidationResult.Failure($"Field 'yearsAhead' must be between 1 and {ToolDefinitions.MaxLunarYearsAhead}.", ToolValidationCode.InvalidLunarParameters);
            }
        }

        if (root.TryGetProperty("leapPolicy", out var leapProp) && leapProp.ValueKind == JsonValueKind.String)
        {
            var policy = leapProp.GetString();
            if (!string.Equals(policy, "NormalOnly", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(policy, "IncludeLeap", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(policy, "LeapOnly", StringComparison.OrdinalIgnoreCase))
            {
                return ToolValidationResult.Failure("Field 'leapPolicy' must be 'NormalOnly', 'IncludeLeap', or 'LeapOnly'.", ToolValidationCode.InvalidLunarParameters);
            }
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateBulkCreateEvents(JsonElement root)
    {
        if (!root.TryGetProperty("events", out var eventsProp) || eventsProp.ValueKind != JsonValueKind.Array)
        {
            return ToolValidationResult.Failure("Field 'events' is required and must be an array of event objects.", ToolValidationCode.MissingRequiredParameter);
        }

        var count = eventsProp.GetArrayLength();
        if (count == 0)
        {
            return ToolValidationResult.Failure("Bulk create 'events' array must contain at least 1 event.", ToolValidationCode.MissingRequiredParameter);
        }

        if (count > ToolDefinitions.MaxBulkEvents)
        {
            return ToolValidationResult.Failure(
                $"Bulk events count ({count}) exceeds the maximum allowed limit of {ToolDefinitions.MaxBulkEvents} events.",
                ToolValidationCode.BulkLimitExceeded);
        }

        var index = 0;
        foreach (var item in eventsProp.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return ToolValidationResult.Failure($"Item at index {index} must be an object.", ToolValidationCode.InvalidJson);
            }

            if (!TryGetRequiredString(item, "title", out _, out _))
            {
                return ToolValidationResult.Failure($"Item at index {index} is missing required 'title'.", ToolValidationCode.MissingRequiredParameter);
            }

            if (!TryGetRequiredDateTime(item, "start", out _, out _))
            {
                return ToolValidationResult.Failure($"Item at index {index} is missing or has invalid 'start'.", ToolValidationCode.MissingRequiredParameter);
            }

            index++;
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateListEvents(JsonElement root)
    {
        if (!TryGetRequiredDateTime(root, "from", out var from, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!TryGetRequiredDateTime(root, "to", out var to, out error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (to < from)
        {
            return ToolValidationResult.Failure("Field 'to' cannot be earlier than 'from'.", ToolValidationCode.InvalidDateRange);
        }

        if (to - from > _maxTimespan)
        {
            return ToolValidationResult.Failure("Search window exceeds the 5-year maximum limit.", ToolValidationCode.TimespanLimitExceeded);
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateUpdateEvent(JsonElement root)
    {
        if (!TryGetRequiredString(root, "eventId", out var eventId, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (root.TryGetProperty("start", out var startProp) && startProp.ValueKind == JsonValueKind.String &&
            root.TryGetProperty("end", out var endProp) && endProp.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(startProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
                DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                if (end < start)
                {
                    return ToolValidationResult.Failure("Field 'end' cannot be earlier than 'start'.", ToolValidationCode.InvalidDateRange);
                }
            }
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateDeleteEvent(JsonElement root)
    {
        if (!TryGetRequiredString(root, "eventId", out var eventId, out var error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        // Security check (SEC-PI-2): Deny bulk wildcards in eventId (e.g. '*', ',', ';')
        if (eventId.Contains('*') || eventId.Contains(',') || eventId.Contains(';') || eventId.Contains('%'))
        {
            return ToolValidationResult.Failure(
                "Wildcard or multi-item deletions are strictly forbidden for delete_event.",
                ToolValidationCode.PolicyViolation);
        }

        if (!TryGetRequiredString(root, "scope", out var scope, out error))
        {
            return ToolValidationResult.Failure(error, ToolValidationCode.MissingRequiredParameter);
        }

        if (!string.Equals(scope, "this", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            return ToolValidationResult.Failure("Field 'scope' must be either 'this' or 'all'.", ToolValidationCode.InvalidScope);
        }

        return ToolValidationResult.Success();
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value, out string errorMessage)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Missing required field '{propertyName}'.";
            return false;
        }

        var str = prop.GetString();
        if (string.IsNullOrWhiteSpace(str))
        {
            errorMessage = $"Required field '{propertyName}' cannot be empty.";
            return false;
        }

        value = str;
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryGetRequiredDateTime(JsonElement root, string propertyName, out DateTimeOffset value, out string errorMessage)
    {
        value = default;
        if (!TryGetRequiredString(root, propertyName, out var dateStr, out errorMessage))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            errorMessage = $"Field '{propertyName}' must be a valid ISO 8601 date string.";
            return false;
        }

        return true;
    }

    private static bool CheckAnyStringExceedsMaxLength(JsonElement element, int maxLength, out string offendingProperty)
    {
        offendingProperty = string.Empty;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (val != null && val.Length > maxLength)
                        {
                            offendingProperty = prop.Name;
                            return true;
                        }
                    }
                    else if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        if (CheckAnyStringExceedsMaxLength(prop.Value, maxLength, out offendingProperty))
                        {
                            if (string.IsNullOrEmpty(offendingProperty))
                            {
                                offendingProperty = prop.Name;
                            }
                            return true;
                        }
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (CheckAnyStringExceedsMaxLength(item, maxLength, out offendingProperty))
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
    }
}
