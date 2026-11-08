using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace LunarCal.Core.Chat;

/// <summary>
/// Heuristic evaluator for AI tool call confidence scores (FR-CONFIRM-4).
/// Calculates a 0-100 score based on required fields, ambiguity indicators,
/// time parsing agreement, and local business validation outcomes.
/// Thresholds: High >= 90%, Medium 60-89%, Low &lt; 60%.
/// </summary>
public static class AiConfidenceEvaluator
{
    private static readonly string[] _ambiguityTokens =
    [
        "hoặc", "chắc là", "khoảng", "tầm", "hình như", "có lẽ",
        "maybe", "around", "probably", "perhaps", "or", "unsure"
    ];

    /// <summary>
    /// Evaluates local heuristic confidence for a tool call.
    /// </summary>
    /// <param name="toolCall">The tool call produced by the model.</param>
    /// <param name="userMessage">The raw command typed by the user.</param>
    /// <param name="typedAt">The timestamp anchor when the user typed the command.</param>
    /// <param name="validationResult">Optional local business validation result.</param>
    /// <returns>A comprehensive <see cref="AiConfidenceResult"/>.</returns>
    public static AiConfidenceResult Evaluate(
        ToolCall? toolCall,
        string? userMessage,
        DateTimeOffset typedAt,
        AiBusinessValidationResult? validationResult = null)
    {
        var score = 100;
        var reasons = new List<string>();

        if (toolCall is null)
        {
            return new AiConfidenceResult(0, AiConfidenceLevel.Low, ["Tool call is missing"]);
        }

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(toolCall.ArgumentsJson);
        }
        catch (JsonException ex)
        {
            return new AiConfidenceResult(0, AiConfidenceLevel.Low, [$"Invalid arguments JSON: {ex.Message}"]);
        }

        using (doc)
        {
            var root = doc.RootElement;

            // 1. Check required fields and structure based on tool name
            if (string.Equals(toolCall.Name, ToolDefinitions.CreateEvent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolCall.Name, ToolDefinitions.CreateRecurringEvent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolCall.Name, ToolDefinitions.CreateLunarRecurringEvent, StringComparison.OrdinalIgnoreCase))
            {
                var hasSummary = root.TryGetProperty("summary", out var summaryProp) &&
                                 !string.IsNullOrWhiteSpace(summaryProp.GetString());
                if (!hasSummary)
                {
                    score -= 40;
                    reasons.Add("Missing or empty event summary");
                }

                var hasStart = root.TryGetProperty("start", out var startProp) &&
                               !string.IsNullOrWhiteSpace(startProp.GetString());
                if (!hasStart)
                {
                    score -= 30;
                    reasons.Add("Missing event start time");
                }
                else if (!DateTimeOffset.TryParse(startProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
                {
                    score -= 25;
                    reasons.Add("Malformed event start time format");
                }
                else
                {
                    // Check end time consistency if present
                    if (root.TryGetProperty("end", out var endProp) &&
                        !string.IsNullOrWhiteSpace(endProp.GetString()) &&
                        DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
                    {
                        if (endTime <= startTime)
                        {
                            score -= 35;
                            reasons.Add("Event end time is before or equal to start time");
                        }
                    }
                }
            }
            else if (string.Equals(toolCall.Name, ToolDefinitions.UpdateEvent, StringComparison.OrdinalIgnoreCase))
            {
                var hasEventId = root.TryGetProperty("event_id", out var eventIdProp) &&
                                 !string.IsNullOrWhiteSpace(eventIdProp.GetString());
                if (!hasEventId)
                {
                    score -= 40;
                    reasons.Add("Missing event_id for update");
                }
            }
            else if (string.Equals(toolCall.Name, ToolDefinitions.DeleteEvent, StringComparison.OrdinalIgnoreCase))
            {
                var hasEventId = root.TryGetProperty("event_id", out var eventIdProp) &&
                                 !string.IsNullOrWhiteSpace(eventIdProp.GetString());
                if (!hasEventId)
                {
                    score -= 40;
                    reasons.Add("Missing event_id for deletion");
                }
            }
            else if (string.Equals(toolCall.Name, ToolDefinitions.BulkCreateEvents, StringComparison.OrdinalIgnoreCase))
            {
                var hasEvents = root.TryGetProperty("events", out var eventsProp) &&
                                eventsProp.ValueKind == JsonValueKind.Array &&
                                eventsProp.GetArrayLength() > 0;
                if (!hasEvents)
                {
                    score -= 40;
                    reasons.Add("Missing or empty events array for bulk creation");
                }
            }
        }

        // 2. Check ambiguity keywords in user message
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            var lowerMessage = userMessage.ToLowerInvariant();
            foreach (var token in _ambiguityTokens)
            {
                if (lowerMessage.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score -= 15;
                    reasons.Add($"Input contains ambiguity indicator '{token}'");
                    break;
                }
            }
        }

        // 3. Evaluate impact of local business validation
        if (validationResult is not null)
        {
            if (validationResult.HasError)
            {
                score -= 50;
                reasons.Add("Business validation produced blocking errors");
            }
            else if (validationResult.Severity == AiBusinessValidationSeverity.Warning)
            {
                score -= 15;
                reasons.Add("Business validation produced warnings (past event or schedule conflict)");
            }
        }

        // Clamp score between 0 and 100
        score = Math.Clamp(score, 0, 100);

        var level = score switch
        {
            >= 90 => AiConfidenceLevel.High,
            >= 60 => AiConfidenceLevel.Medium,
            _ => AiConfidenceLevel.Low
        };

        return new AiConfidenceResult(score, level, reasons);
    }
}
