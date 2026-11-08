using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;

using AwesomeAssertions;

using LunarCal.Core.Abstractions;

using LunarCal.Core.Calendar;

using LunarCal.Core.Chat;

using NSubstitute;

using NSubstitute.ExceptionExtensions;

using Xunit;



namespace LunarCal.Core.Tests.Chat;



public sealed class AiBusinessValidatorTests

{

    private static readonly DateTimeOffset _typedAt = new(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7));

    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();

    private readonly AiBusinessValidator _validator = new();



    [Fact]

    public async Task FR_CONFIRM_5_Valid_event_passes_without_errors_or_warnings()

    {

        var toolCall = new ToolCall(

            ToolDefinitions.CreateEvent,

            """{"summary": "Họp chiến lược", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");



        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .Returns(new List<CalendarEvent>());



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);



        result.IsValid.Should().BeTrue();

        result.HasError.Should().BeFalse();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Pass);

        result.Messages.Should().ContainSingle(m => m.Severity == AiBusinessValidationSeverity.Pass);

    }



    [Fact]

    public async Task FR_CONFIRM_5_End_before_or_equal_to_start_fails_validation_with_error()

    {

        var toolCall = new ToolCall(

            ToolDefinitions.CreateEvent,

            """{"summary": "Họp", "start": "2026-10-22T10:00:00+07:00", "end": "2026-10-22T09:00:00+07:00"}""");



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);



        result.IsValid.Should().BeFalse();

        result.HasError.Should().BeTrue();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Error);

        result.Messages.Should().Contain(m => m.Message.Contains("kết thúc phải sau"));

    }



    [Fact]

    public async Task FR_CONFIRM_5_Event_in_the_past_produces_warning()

    {

        var toolCall = new ToolCall(

            ToolDefinitions.CreateEvent,

            """{"summary": "Họp hôm qua", "start": "2026-10-15T09:00:00+07:00", "end": "2026-10-15T10:00:00+07:00"}""");



        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .Returns(new List<CalendarEvent>());



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);



        result.IsValid.Should().BeTrue();

        result.HasError.Should().BeFalse();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Warning);

        result.Messages.Should().Contain(m => m.Message.Contains("quá khứ"));

    }



    [Fact]

    public async Task FR_CONFIRM_5_Overlapping_event_in_event_store_produces_schedule_conflict_warning()

    {

        var toolCall = new ToolCall(

            ToolDefinitions.CreateEvent,

            """{"summary": "Họp mới", "start": "2026-10-22T09:30:00+07:00", "end": "2026-10-22T10:30:00+07:00"}""");



        var existingStart = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();

        var existingEnd = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();



        var existingEvent = new CalendarEvent

        {

            Id = "existing-1",

            CalendarId = "primary",

            Title = "Họp giao ban tuần",

            StartUtc = existingStart,

            EndUtc = existingEnd

        };



        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .Returns(new List<CalendarEvent> { existingEvent });



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);



        result.IsValid.Should().BeTrue();

        result.HasError.Should().BeFalse();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Warning);

        result.Messages.Should().Contain(m => m.Message.Contains("Trùng giờ với sự kiện 'Họp giao ban tuần'"));

    }



    [Fact]

    public async Task FR_CONFIRM_5_Update_event_ignores_itself_during_conflict_check()

    {

        var toolCall = new ToolCall(

            ToolDefinitions.UpdateEvent,

            """{"event_id": "event-123", "summary": "Họp cập nhật", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");



        var sameEvent = new CalendarEvent

        {

            Id = "event-123",

            CalendarId = "primary",

            Title = "Họp cũ",

            StartUtc = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(),

            EndUtc = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime()

        };



        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .Returns(new List<CalendarEvent> { sameEvent });



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);



        result.IsValid.Should().BeTrue();

        result.HasError.Should().BeFalse();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Pass);

    }



    [Fact]

    public async Task FR_CONFIRM_5_ValidateAsync_handles_null_arguments_and_json_errors()

    {

        var emptyArgs = new ToolCall(ToolDefinitions.CreateEvent, "");

        var res1 = await _validator.ValidateAsync(emptyArgs, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res1.HasError.Should().BeTrue();



        var malformedArgs = new ToolCall(ToolDefinitions.CreateEvent, "{bad json");

        var res2 = await _validator.ValidateAsync(malformedArgs, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res2.HasError.Should().BeTrue();

    }



    [Fact]

    public async Task FR_CONFIRM_5_Delete_event_validates_presence_of_event_id()

    {

        var missingId = new ToolCall(ToolDefinitions.DeleteEvent, "{}");

        var res1 = await _validator.ValidateAsync(missingId, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res1.HasError.Should().BeTrue();

        res1.Messages.Should().Contain(m => m.Message.Contains("Thiếu mã sự kiện"));



        var withId = new ToolCall(ToolDefinitions.DeleteEvent, """{"event_id": "ev-1"}""");

        var res2 = await _validator.ValidateAsync(withId, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res2.IsValid.Should().BeTrue();

        res2.Severity.Should().Be(AiBusinessValidationSeverity.Pass);

    }



    [Fact]

    public async Task FR_CONFIRM_5_Bulk_create_validates_empty_and_limit_and_events()

    {

        var emptyBulk = new ToolCall(ToolDefinitions.BulkCreateEvents, "{}");

        var res1 = await _validator.ValidateAsync(emptyBulk, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res1.HasError.Should().BeTrue();



        var zeroBulk = new ToolCall(ToolDefinitions.BulkCreateEvents, """{"events": []}""");

        var res2 = await _validator.ValidateAsync(zeroBulk, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res2.HasError.Should().BeTrue();

        res2.Messages.Should().Contain(m => m.Message.Contains("rỗng"));



        var validBulk = new ToolCall(ToolDefinitions.BulkCreateEvents, """{"events": [{"summary": "E1", "start": "2026-10-22T09:00:00+07:00"}]}""");

        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .Returns(new List<CalendarEvent>());

        var res3 = await _validator.ValidateAsync(validBulk, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res3.IsValid.Should().BeTrue();

    }



    [Fact]

    public async Task FR_CONFIRM_5_Missing_summary_or_start_and_malformed_time_flags_error()

    {

        var noSummary = new ToolCall(ToolDefinitions.CreateEvent, """{"start": "2026-10-22T09:00:00+07:00"}""");

        var res1 = await _validator.ValidateAsync(noSummary, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res1.HasError.Should().BeTrue();



        var noStart = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Test"}""");

        var res2 = await _validator.ValidateAsync(noStart, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res2.HasError.Should().BeTrue();



        var badStart = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Test", "start": "invalid"}""");

        var res3 = await _validator.ValidateAsync(badStart, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res3.HasError.Should().BeTrue();



        var badEnd = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Test", "start": "2026-10-22T09:00:00+07:00", "end": "invalid"}""");

        var res4 = await _validator.ValidateAsync(badEnd, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        res4.HasError.Should().BeTrue();

    }



    [Fact]

    public async Task FR_CONFIRM_5_Event_store_exception_adds_conflict_warning()

    {

        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Test", "start": "2026-10-22T09:00:00+07:00"}""");

        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())

            .ThrowsAsync(new InvalidOperationException("DB offline"));



        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Warning);

        result.Messages.Should().Contain(m => m.Message.Contains("Không thể kiểm tra trùng lịch"));

    }



    [Fact]

    public async Task FR_CONFIRM_5_Unknown_tool_returns_success()

    {

        var toolCall = new ToolCall("unknown_tool", "{}");

        var result = await _validator.ValidateAsync(toolCall, _eventStore, _typedAt, cancellationToken: TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();

        result.Severity.Should().Be(AiBusinessValidationSeverity.Pass);

    }

}

