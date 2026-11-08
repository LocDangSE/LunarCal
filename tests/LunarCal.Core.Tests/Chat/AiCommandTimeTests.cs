using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

/// <summary>
/// P4-R5 (FR-QUEUE-4, FR-CONFIRM-1): a command typed in Vietnam must be anchored in the user's zone, not in UTC, or
/// "6h tối mai" is shown as 11:00 and early-morning commands land on the wrong day.
/// </summary>
public sealed class AiCommandTimeTests
{
    private const string VietnamZone = "SE Asia Standard Time";

    // Typed 00:30 on 9 Nov in Vietnam is 17:30 on 8 Nov in UTC: the case where the UTC date is a day behind.
    private static readonly DateTimeOffset _typedUtc = new(2026, 11, 8, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public void FR_QUEUE_4_InZone_restores_the_local_offset_of_a_stored_utc_instant()
    {
        var local = AiCommandTime.InZone(_typedUtc, VietnamZone);

        local.Offset.Should().Be(TimeSpan.FromHours(7));
        local.Should().Be(_typedUtc, "it is the same instant");
        (local.Year, local.Month, local.Day, local.Hour, local.Minute).Should().Be((2026, 11, 9, 0, 30));
    }

    [Fact]
    public void FR_QUEUE_4_InZone_leaves_the_value_alone_for_an_unknown_zone()
    {
        AiCommandTime.InZone(_typedUtc, "Not/AZone").Should().Be(_typedUtc);
        AiCommandTime.InZone(_typedUtc, null).Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void FR_QUEUE_4_The_prompt_gets_the_local_time_and_an_explicit_offset_even_from_a_utc_instant()
    {
        var rendered = new SystemPromptProvider().RenderSystemPrompt(
            new PromptContext(TypedAt: _typedUtc, TimeZoneId: VietnamZone, Locale: "vi-VN"));

        rendered.Should().Contain("2026-11-09T00:30:00+07:00", "relative dates must be computed on the user's calendar day");
        rendered.Should().Contain("(UTC+07:00)");
        rendered.Should().NotContain("2026-11-08T17:30:00+00:00");
        rendered.Should().Contain("Explicit Offset");
    }

    [Fact]
    public void FR_QUEUE_4_A_confirmed_all_day_action_falls_back_to_the_command_zone_not_the_machine_zone()
    {
        var call = new ToolCall(ToolDefinitions.CreateEvent, """{"summary":"Nghỉ phép","start":"2026-11-10","allDay":true}""");

        var withZone = AiCommandTime.WithDefaultTimeZone(call, VietnamZone);

        withZone.ArgumentsJson.Should().Contain("\"timezone\":\"SE Asia Standard Time\"");
    }

    [Fact]
    public void FR_QUEUE_4_A_zone_sent_by_the_model_is_never_overwritten()
    {
        var call = new ToolCall(ToolDefinitions.CreateEvent, """{"summary":"x","timezone":"Asia/Tokyo"}""");

        AiCommandTime.WithDefaultTimeZone(call, VietnamZone).Should().BeSameAs(call);
        AiCommandTime.WithDefaultTimeZone(new ToolCall("x", "not json"), VietnamZone).ArgumentsJson.Should().Be("not json");
        AiCommandTime.WithDefaultTimeZone(call, null).Should().BeSameAs(call);
    }
}
