using System.Reflection;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="SystemPromptProvider"/> and <see cref="PromptContext"/> (FR-CHAT-1, FR-CHAT-6, FR-QUEUE-4, spec §7, ADR-0030).
/// </summary>
public sealed class SystemPromptProviderTests
{
    private readonly SystemPromptProvider _provider = new();

    [Fact]
    public void FR_CHAT_6_DefaultVersion_is_v1_and_HasVersion_checks_resource_existence()
    {
        _provider.DefaultVersion.Should().Be("v1");
        _provider.HasVersion("v1").Should().BeTrue();
        _provider.HasVersion("V1").Should().BeTrue();
        _provider.HasVersion("1").Should().BeTrue();
        _provider.HasVersion("v2").Should().BeFalse();
        _provider.HasVersion(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_6_GetRawTemplate_loads_v1_prompt_with_required_rules_and_placeholders()
    {
        var template = _provider.GetRawTemplate("v1");

        template.Should().NotBeNullOrWhiteSpace();
        template.Should().Contain("{{TYPED_AT}}");
        template.Should().Contain("{{TIME_ZONE}}");
        template.Should().Contain("{{USER_LOCALE}}");
        template.Should().Contain("{{AVAILABLE_CALENDARS}}");

        // Temporal calculation anchor rules (FR-QUEUE-4, spec §7)
        template.Should().Contain("Anchor Timestamp");

        // RFC 5545 recurrence compliance
        template.Should().Contain("RFC 5545");
        template.Should().Contain("RRULE:FREQ=");

        // Vietnamese Lunar Calendar rules (Âm lịch)
        template.Should().Contain("create_lunar_rule");
        template.Should().Contain("lunarDay");
        template.Should().Contain("mùng 1");
        template.Should().Contain("rằm");

        // Mandatory clarification rule for missing time (FR-CHAT-6, FR-CONFIRM-8)
        template.Should().Contain("Missing Start Time");
        template.Should().Contain("DO NOT");

        // Bilingual and mixed examples (FR-CHAT-6)
        template.Should().Contain("Họp nhóm đồ án");
        template.Should().Contain("Team standup");
        template.Should().Contain("Remind me");
    }

    [Fact]
    public void FR_QUEUE_4_RenderSystemPrompt_substitutes_typed_at_timezone_and_calendars()
    {
        var typedAt = new DateTimeOffset(2026, 10, 15, 14, 30, 0, TimeSpan.FromHours(7));
        var context = new PromptContext(
            TypedAt: typedAt,
            TimeZoneId: "SE Asia Standard Time",
            Locale: "vi-VN",
            AvailableCalendars: ["primary (Personal)", "work-cal-123 (Work)"]);

        var rendered = _provider.RenderSystemPrompt(context, "v1");

        rendered.Should().NotContain("{{TYPED_AT}}");
        rendered.Should().NotContain("{{TIME_ZONE}}");
        rendered.Should().NotContain("{{USER_LOCALE}}");
        rendered.Should().NotContain("{{AVAILABLE_CALENDARS}}");

        rendered.Should().Contain("2026-10-15T14:30:00+07:00");
        rendered.Should().Contain("SE Asia Standard Time");
        rendered.Should().Contain("vi-VN");
        rendered.Should().Contain("- primary (Personal)");
        rendered.Should().Contain("- work-cal-123 (Work)");
    }

    [Fact]
    public void FR_QUEUE_4_RenderSystemPrompt_with_empty_calendars_defaults_to_primary()
    {
        var typedAt = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.FromHours(7));
        var context = new PromptContext(
            TypedAt: typedAt,
            TimeZoneId: "Asia/Ho_Chi_Minh",
            Locale: "en-US",
            AvailableCalendars: null);

        var rendered = _provider.RenderSystemPrompt(context);

        rendered.Should().Contain("- primary (Default Google Calendar)");
    }

    [Fact]
    public void SEC_PRIV_spec_section_7_PromptContext_does_not_contain_event_fields()
    {
        // Spec §7 and SEC-PRIV: Prompt context must NEVER contain event bodies or user calendar events.
        var forbiddenKeywords = new[]
        {
            "Event", "Events", "Summary", "Description", "Location", "Title",
            "Start", "End", "StartTime", "EndTime", "Attendees", "Attendee",
            "Recurrence", "RRule", "Payload", "Body"
        };

        var properties = typeof(PromptContext).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            foreach (var forbidden in forbiddenKeywords)
            {
                prop.Name.Should().NotBe(forbidden,
                    $"PromptContext must not leak calendar event content (found forbidden property '{prop.Name}')");
            }
        }
    }

    [Fact]
    public void FR_CHAT_1_RenderSystemPrompt_null_context_throws_ArgumentNullException()
    {
        var action = () => _provider.RenderSystemPrompt(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FR_CHAT_6_GetRawTemplate_nonexistent_version_throws_FileNotFoundException()
    {
        var action = () => _provider.GetRawTemplate("v999");

        action.Should().Throw<FileNotFoundException>();
    }
}
