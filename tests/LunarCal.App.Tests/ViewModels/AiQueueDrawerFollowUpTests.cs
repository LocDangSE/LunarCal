using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>P4-R5/P4-R7: queue drawer closes behind the dialog and follows the worker; the dialog shows local time.</summary>
public sealed class AiQueueDrawerFollowUpTests
{
    private readonly IAiCommandStore _store = Substitute.For<IAiCommandStore>();
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);

    private static AiCommand Command(string id, string status, DateTimeOffset? typedAt = null, string? lastError = null) => new()
    {
        Id = id,
        DisplayNo = 1000,
        Text = "nhắc tôi họp 6h tối mai",
        TypedAt = typedAt ?? new DateTimeOffset(2026, 11, 8, 14, 12, 0, TimeSpan.FromHours(7)),
        TimeZone = "SE Asia Standard Time",
        InputLang = "vi",
        Status = status,
        LastError = lastError,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private void QueueHolds(params AiCommand[] commands) =>
        _store.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AiCommand>>(commands));

    [Fact]
    public async Task FR_QUEUE_6_Pressing_Xem_or_Xac_nhan_closes_the_drawer_and_opens_the_dialog()
    {
        QueueHolds(Command("a", AiCommandStatus.NeedsConfirmation));
        using var vm = new AiQueueDrawerViewModel(_store, _loc);
        var requested = 0;
        vm.CommandActionRequested += (_, _) => requested++;
        await vm.OpenDrawerAsync();
        var item = vm.Commands[0];

        vm.ViewCommand(item);
        vm.IsOpen.Should().BeFalse();

        await vm.OpenDrawerAsync();
        vm.ConfirmCommand(item);
        vm.IsOpen.Should().BeFalse();

        requested.Should().Be(2);
    }

    [Fact]
    public async Task FR_QUEUE_2_An_open_drawer_follows_status_changes_without_rebuilding_when_nothing_changed()
    {
        QueueHolds(Command("a", AiCommandStatus.Processing));
        using var vm = new AiQueueDrawerViewModel(_store, _loc);
        await vm.OpenDrawerAsync();
        var first = vm.Commands[0];

        await vm.RefreshIfChangedAsync(TestContext.Current.CancellationToken);
        vm.Commands[0].Should().BeSameAs(first, "nothing changed, so the list (and its scroll position) is kept");

        QueueHolds(Command("a", AiCommandStatus.NeedsConfirmation));
        await vm.RefreshIfChangedAsync(TestContext.Current.CancellationToken);

        vm.Commands[0].Status.Should().Be(AiCommandStatus.NeedsConfirmation);
        vm.Commands[0].CanConfirm.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_A_missing_key_error_is_translated_and_never_shown_as_raw_marker_text()
    {
        var item = new AiCommandItemViewModel(
            Command("a", AiCommandStatus.Error, lastError: "Gemini API key is not configured. Please enter your API key in Settings > AI."),
            _loc);

        item.RetryInfoText.Should().NotBeNull();
        item.RetryInfoText!.Should().NotStartWith("!").And.NotEndWith("!").And.NotContain("Please enter your API key");
    }

    [Fact]
    public void FR_QUEUE_5_The_retry_line_shows_the_real_wait_not_a_fixed_8_minutes()
    {
        var command = Command("a", AiCommandStatus.Error, lastError: "timed out") with
        {
            RetryCount = 2,
            NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(30),
        };

        var item = new AiCommandItemViewModel(command, _loc);

        item.RetryInfoText.Should().Contain("2/5").And.NotContain("8 phút");
    }

    [Fact]
    public void FR_CONFIRM_1_The_dialog_shows_the_local_time_of_the_command_not_utc()
    {
        // Stored as UTC the offset is +00:00; the model answered 18:00+07:00 for "6h tối mai".
        var storedAsUtc = new DateTimeOffset(2026, 11, 8, 7, 12, 0, TimeSpan.Zero);
        var command = Command("a", AiCommandStatus.NeedsConfirmation, typedAt: storedAsUtc);
        var call = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Họp","start":"2026-11-09T18:00:00+07:00","end":"2026-11-09T19:00:00+07:00"}""");
        var vm = new AiConfirmDialogViewModel(_loc);

        vm.Initialize(
            command,
            call,
            new AiConfidenceResult(95, AiConfidenceLevel.High, []),
            AiBusinessValidationResult.Success(),
            isLunarEnabled: false,
            isTrainingOptIn: false);

        vm.EventTimeDisplay.Should().Be("18:00 – 19:00");
        vm.EventDateDisplay.Should().Contain("09/11/2026").And.Contain("Thứ Hai", "the weekday follows the UI language");
        vm.TypedAtDisplay.Should().Contain("GMT+07:00").And.Contain("14:12");
    }

    [Fact]
    public void FR_QUEUE_6_Viewing_a_command_that_is_not_waiting_keeps_Confirm_locked()
    {
        var vm = new AiConfirmDialogViewModel(_loc) { IsReadOnly = true };

        vm.CanConfirm.Should().BeFalse();
        vm.CanConfirmDisabledReason.Should().Be(_loc["ai_confirm.readonly_hint"]);
    }
}
