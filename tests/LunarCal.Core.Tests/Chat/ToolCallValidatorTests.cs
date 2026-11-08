using System.Text.Json;
using AwesomeAssertions;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="ToolDefinitions"/> and <see cref="ToolCallValidator"/> (FR-CHAT §4.6, SEC-PI-2, FR-CHAT-3).
/// </summary>
public sealed class ToolCallValidatorTests
{
    [Fact]
    public void ToolDefinitions_contains_all_7_tools_with_valid_json_schemas()
    {
        ToolDefinitions.AllTools.Should().HaveCount(7);

        var toolNames = ToolDefinitions.AllTools.Select(t => t.Name).ToList();
        toolNames.Should().Contain(
        [
            ToolDefinitions.CreateEvent,
            ToolDefinitions.CreateRecurringEvent,
            ToolDefinitions.CreateLunarRecurringEvent,
            ToolDefinitions.BulkCreateEvents,
            ToolDefinitions.ListEvents,
            ToolDefinitions.UpdateEvent,
            ToolDefinitions.DeleteEvent,
        ]);

        foreach (var tool in ToolDefinitions.AllTools)
        {
            tool.Name.Should().NotBeNullOrWhiteSpace();
            tool.Description.Should().NotBeNullOrWhiteSpace();

            var action = () => JsonDocument.Parse(tool.ParametersSchemaJson);
            action.Should().NotThrow($"Tool '{tool.Name}' schema must be valid JSON.");
        }
    }

    [Fact]
    public void FR_CHAT_3_Session_call_limit_50_blocks_further_calls()
    {
        var call = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Meeting","start":"2026-10-16T10:00:00+07:00"}""");

        var validResult = ToolCallValidator.Validate(call, currentSessionCallCount: 49);
        validResult.IsValid.Should().BeTrue();

        var blockedResult = ToolCallValidator.Validate(call, currentSessionCallCount: 50);
        blockedResult.IsValid.Should().BeFalse();
        blockedResult.Code.Should().Be(ToolValidationCode.SessionCallLimitExceeded);
    }

    [Fact]
    public void SEC_PI_2_Unknown_tool_is_rejected()
    {
        var maliciousCall = new ToolCall("system_exec", """{"cmd":"rm -rf /"}""");

        var result = ToolCallValidator.Validate(maliciousCall);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.UnknownTool);
    }

    [Fact]
    public void Validate_malformed_json_arguments_returns_InvalidJson()
    {
        var call = new ToolCall(ToolDefinitions.CreateEvent, "{ malformed json ");

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.InvalidJson);
    }

    [Fact]
    public void SEC_PI_2_FR_CHAT_3_String_length_exceeding_4000_characters_is_rejected()
    {
        var longTitle = new string('A', 4001);
        var call = new ToolCall(ToolDefinitions.CreateEvent, $$"""{"title":"{{longTitle}}","start":"2026-10-16T10:00:00+07:00"}""");

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.StringLengthExceeded);
    }

    // --- 1. create_event tests ---

    [Fact]
    public void CreateEvent_valid_arguments_passes_validation()
    {
        var json = """
        {
            "title": "Họp đồ án",
            "start": "2026-10-16T14:00:00+07:00",
            "end": "2026-10-16T15:30:00+07:00",
            "allDay": false,
            "location": "Phòng 302",
            "description": "Thảo luận về tính năng AI",
            "reminders": [10, 30],
            "calendarId": "primary"
        }
        """;
        var call = new ToolCall(ToolDefinitions.CreateEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateEvent_missing_title_or_start_fails()
    {
        var callMissingTitle = new ToolCall(ToolDefinitions.CreateEvent, """{"start":"2026-10-16T14:00:00+07:00"}""");
        ToolCallValidator.Validate(callMissingTitle).Code.Should().Be(ToolValidationCode.MissingRequiredParameter);

        var callMissingStart = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Meeting"}""");
        ToolCallValidator.Validate(callMissingStart).Code.Should().Be(ToolValidationCode.MissingRequiredParameter);
    }

    [Fact]
    public void CreateEvent_end_before_start_fails_validation()
    {
        var json = """{"title":"Meeting","start":"2026-10-16T15:00:00+07:00","end":"2026-10-16T14:00:00+07:00"}""";
        var call = new ToolCall(ToolDefinitions.CreateEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.InvalidDateRange);
    }

    [Fact]
    public void CreateEvent_duration_exceeding_5_years_fails_TimespanLimitExceeded()
    {
        var json = """{"title":"Long Event","start":"2026-01-01T00:00:00+07:00","end":"2032-01-01T00:00:00+07:00"}""";
        var call = new ToolCall(ToolDefinitions.CreateEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.TimespanLimitExceeded);
    }

    // --- 2. create_recurring_event tests ---

    [Fact]
    public void CreateRecurringEvent_valid_RFC5545_passes_validation()
    {
        var json = """
        {
            "title": "Daily Standup",
            "start": "2026-10-16T09:00:00+07:00",
            "end": "2026-10-16T09:30:00+07:00",
            "rrule": "RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
            "until": "2026-12-31T23:59:59+07:00"
        }
        """;
        var call = new ToolCall(ToolDefinitions.CreateRecurringEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateRecurringEvent_invalid_rrule_or_exceeded_until_fails()
    {
        var callInvalidRRule = new ToolCall(ToolDefinitions.CreateRecurringEvent,
            """{"title":"Meeting","start":"2026-10-16T09:00:00+07:00","rrule":"INVALID_RULE"}""");
        ToolCallValidator.Validate(callInvalidRRule).Code.Should().Be(ToolValidationCode.PolicyViolation);

        var callUntilTooFar = new ToolCall(ToolDefinitions.CreateRecurringEvent,
            """{"title":"Meeting","start":"2026-10-16T09:00:00+07:00","rrule":"RRULE:FREQ=DAILY","until":"2035-10-16T09:00:00+07:00"}""");
        ToolCallValidator.Validate(callUntilTooFar).Code.Should().Be(ToolValidationCode.TimespanLimitExceeded);
    }

    // --- 3. create_lunar_recurring_event tests ---

    [Fact]
    public void CreateLunarRecurringEvent_valid_parameters_passes()
    {
        var json = """
        {
            "title": "Thắp hương Rằm",
            "lunarDay": 15,
            "lunarMonth": null,
            "time": "07:00:00",
            "yearsAhead": 5,
            "leapPolicy": "NormalOnly"
        }
        """;
        var call = new ToolCall(ToolDefinitions.CreateLunarRecurringEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateLunarRecurringEvent_invalid_bounds_fails()
    {
        var callBadDay = new ToolCall(ToolDefinitions.CreateLunarRecurringEvent, """{"title":"Test","lunarDay":35}""");
        ToolCallValidator.Validate(callBadDay).Code.Should().Be(ToolValidationCode.InvalidLunarParameters);

        var callBadMonth = new ToolCall(ToolDefinitions.CreateLunarRecurringEvent, """{"title":"Test","lunarDay":15,"lunarMonth":13}""");
        ToolCallValidator.Validate(callBadMonth).Code.Should().Be(ToolValidationCode.InvalidLunarParameters);

        var callBadYears = new ToolCall(ToolDefinitions.CreateLunarRecurringEvent, """{"title":"Test","lunarDay":15,"yearsAhead":25}""");
        ToolCallValidator.Validate(callBadYears).Code.Should().Be(ToolValidationCode.InvalidLunarParameters);

        var callBadPolicy = new ToolCall(ToolDefinitions.CreateLunarRecurringEvent, """{"title":"Test","lunarDay":15,"leapPolicy":"Unknown"}""");
        ToolCallValidator.Validate(callBadPolicy).Code.Should().Be(ToolValidationCode.InvalidLunarParameters);
    }

    // --- 4. bulk_create_events tests (max 500) ---

    [Fact]
    public void BulkCreateEvents_valid_events_passes()
    {
        var json = """
        {
            "events": [
                { "title": "Event 1", "start": "2026-10-16T10:00:00+07:00" },
                { "title": "Event 2", "start": "2026-10-17T10:00:00+07:00" }
            ]
        }
        """;
        var call = new ToolCall(ToolDefinitions.BulkCreateEvents, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_3_BulkCreateEvents_exceeding_500_events_fails_BulkLimitExceeded()
    {
        var items = Enumerable.Range(1, 501)
            .Select(i => $$"""{"title":"Event {{i}}","start":"2026-10-16T10:00:00+07:00"}""");
        var json = $$"""{"events":[{{string.Join(",", items)}}]}""";

        var call = new ToolCall(ToolDefinitions.BulkCreateEvents, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.BulkLimitExceeded);
    }

    [Fact]
    public void BulkCreateEvents_empty_array_fails()
    {
        var call = new ToolCall(ToolDefinitions.BulkCreateEvents, """{"events":[]}""");

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.MissingRequiredParameter);
    }

    // --- 5. list_events tests ---

    [Fact]
    public void ListEvents_valid_range_passes()
    {
        var json = """
        {
            "from": "2026-10-01T00:00:00+07:00",
            "to": "2026-11-01T00:00:00+07:00",
            "query": "Họp",
            "calendarId": "primary"
        }
        """;
        var call = new ToolCall(ToolDefinitions.ListEvents, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ListEvents_range_over_5_years_or_to_before_from_fails()
    {
        var callToBeforeFrom = new ToolCall(ToolDefinitions.ListEvents,
            """{"from":"2026-11-01T00:00:00+07:00","to":"2026-10-01T00:00:00+07:00"}""");
        ToolCallValidator.Validate(callToBeforeFrom).Code.Should().Be(ToolValidationCode.InvalidDateRange);

        var callTooLong = new ToolCall(ToolDefinitions.ListEvents,
            """{"from":"2026-01-01T00:00:00+07:00","to":"2032-01-01T00:00:00+07:00"}""");
        ToolCallValidator.Validate(callTooLong).Code.Should().Be(ToolValidationCode.TimespanLimitExceeded);
    }

    // --- 6. update_event tests ---

    [Fact]
    public void UpdateEvent_valid_arguments_passes()
    {
        var json = """
        {
            "eventId": "evt_123456",
            "title": "Tiêu đề mới",
            "start": "2026-10-16T15:00:00+07:00",
            "end": "2026-10-16T16:00:00+07:00"
        }
        """;
        var call = new ToolCall(ToolDefinitions.UpdateEvent, json);

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateEvent_missing_eventId_fails()
    {
        var call = new ToolCall(ToolDefinitions.UpdateEvent, """{"title":"New Title"}""");

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.MissingRequiredParameter);
    }

    // --- 7. delete_event tests ---

    [Fact]
    public void DeleteEvent_valid_scope_passes()
    {
        var callThis = new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId":"evt_999","scope":"this"}""");
        ToolCallValidator.Validate(callThis).IsValid.Should().BeTrue();

        var callAll = new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId":"evt_999","scope":"all"}""");
        ToolCallValidator.Validate(callAll).IsValid.Should().BeTrue();
    }

    [Fact]
    public void DeleteEvent_invalid_scope_fails()
    {
        var call = new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId":"evt_999","scope":"invalid_scope"}""");

        var result = ToolCallValidator.Validate(call);

        result.IsValid.Should().BeFalse();
        result.Code.Should().Be(ToolValidationCode.InvalidScope);
    }

    [Fact]
    public void SEC_PI_2_DeleteEvent_wildcards_and_mass_deletion_are_rejected()
    {
        // SEC-PI-2: Prevent mass deletion exploits like {"eventId": "*", "scope": "all"}
        var callWildcard = new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId":"*","scope":"all"}""");
        var result1 = ToolCallValidator.Validate(callWildcard);
        result1.IsValid.Should().BeFalse();
        result1.Code.Should().Be(ToolValidationCode.PolicyViolation);

        var callCommaList = new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId":"id1,id2,id3","scope":"all"}""");
        var result2 = ToolCallValidator.Validate(callCommaList);
        result2.IsValid.Should().BeFalse();
        result2.Code.Should().Be(ToolValidationCode.PolicyViolation);
    }
}
