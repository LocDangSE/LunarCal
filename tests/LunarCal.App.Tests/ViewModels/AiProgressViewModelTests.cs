using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// P4-R7 (FR-QUEUE-2, FR-QUEUE-6, FR-CONFIRM-1): after sending, the user sees that the model is working, sees retries and
/// failures, and the confirmation dialog opens by itself when the proposal is ready.
/// </summary>
public sealed class AiProgressViewModelTests
{
    private static readonly DateTimeOffset _start = new(2026, 11, 8, 14, 12, 56, TimeSpan.FromHours(7));

    private readonly IAiCommandStore _store = Substitute.For<IAiCommandStore>();
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);
    private DateTimeOffset _now = _start;

    private AiProgressViewModel Create() => new(_store, _loc, () => _now);

    private static AiCommand Command(string status, int retryCount = 0, DateTimeOffset? nextRetryAt = null, string? lastError = null) => new()
    {
        Id = "c1",
        DisplayNo = 1019,
        Text = "nhắc tôi họp 6h tối mai",
        TypedAt = _start,
        TimeZone = "SE Asia Standard Time",
        InputLang = "vi",
        Status = status,
        RetryCount = retryCount,
        NextRetryAt = nextRetryAt,
        LastError = lastError,
        CreatedAt = _start,
        UpdatedAt = _start,
    };

    private void StoreReturns(AiCommand command) =>
        _store.GetByIdAsync("c1", Arg.Any<System.Threading.CancellationToken>()).Returns(command);

    [Fact]
    public void FR_QUEUE_2_Sending_a_command_shows_a_card_right_away_with_its_number()
    {
        var vm = Create();

        vm.Track(Command(AiCommandStatus.Pending));

        vm.IsVisible.Should().BeTrue();
        vm.IsProcessing.Should().BeTrue();
        vm.IsBusy.Should().BeTrue("the Cancel button applies while the model works");
        vm.Title.Should().Contain("#1019");
        vm.HasTrackedCommands.Should().BeTrue();
    }

    [Fact]
    public async Task FR_QUEUE_2_The_card_counts_the_elapsed_time_while_the_model_works()
    {
        var vm = Create();
        vm.Track(Command(AiCommandStatus.Pending));
        StoreReturns(Command(AiCommandStatus.Processing));

        _now = _start.AddSeconds(75);
        await vm.TickAsync(TestContext.Current.CancellationToken);

        vm.IsProcessing.Should().BeTrue();
        vm.Detail.Should().Contain("1 phút 15 giây");
    }

    [Fact]
    public async Task FR_QUEUE_5_A_scheduled_retry_is_shown_with_the_attempt_and_the_wait()
    {
        var vm = Create();
        vm.Track(Command(AiCommandStatus.Pending));
        StoreReturns(Command(AiCommandStatus.Error, retryCount: 2, nextRetryAt: _start.AddSeconds(30), lastError: "timed out"));

        await vm.TickAsync(TestContext.Current.CancellationToken);

        vm.IsRetrying.Should().BeTrue();
        vm.Title.Should().Contain("2/5");
        vm.Detail.Should().Contain("30 giây");
    }

    [Fact]
    public async Task FR_CONFIRM_1_A_ready_proposal_opens_the_dialog_by_itself_and_clears_the_card()
    {
        var vm = Create();
        vm.CanAutoOpen = () => true;
        AiCommandItemViewModel? opened = null;
        vm.AutoOpenRequested += (_, item) => opened = item;
        vm.Track(Command(AiCommandStatus.Pending));
        StoreReturns(Command(AiCommandStatus.NeedsConfirmation));

        await vm.TickAsync(TestContext.Current.CancellationToken);

        opened.Should().NotBeNull();
        opened!.Id.Should().Be("c1");
        vm.IsVisible.Should().BeFalse();
        vm.HasTrackedCommands.Should().BeFalse("nothing is followed any more, so the shell stops polling");
    }

    [Fact]
    public async Task FR_CONFIRM_1_When_the_dialog_must_not_steal_focus_the_card_offers_Xem_instead()
    {
        var vm = Create();
        vm.CanAutoOpen = () => false;
        var autoOpened = false;
        AiCommandItemViewModel? viewed = null;
        vm.AutoOpenRequested += (_, _) => autoOpened = true;
        vm.ViewRequested += (_, item) => viewed = item;
        vm.Track(Command(AiCommandStatus.Pending));
        StoreReturns(Command(AiCommandStatus.NeedsConfirmation));

        await vm.TickAsync(TestContext.Current.CancellationToken);

        autoOpened.Should().BeFalse();
        vm.IsReady.Should().BeTrue();
        vm.Title.Should().Contain("#1019");

        vm.ViewCommand.Execute(null);

        viewed.Should().NotBeNull();
        vm.IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task FR_CHAT_7_A_command_that_failed_for_good_shows_the_reason_in_the_user_language()
    {
        var vm = Create();
        vm.Track(Command(AiCommandStatus.Pending));
        StoreReturns(Command(AiCommandStatus.Error, retryCount: 0, nextRetryAt: null, lastError: "Gemini API key is not configured. Please enter your API key in Settings > AI."));

        await vm.TickAsync(TestContext.Current.CancellationToken);

        vm.IsFailed.Should().BeTrue();
        vm.Detail.Should().NotStartWith("!").And.NotContain("Gemini API key is not configured");
        vm.HasTrackedCommands.Should().BeFalse();
    }

    [Fact]
    public async Task FR_QUEUE_6_Cancel_stops_following_the_command_and_cancels_it_in_the_queue()
    {
        var vm = Create();
        vm.Track(Command(AiCommandStatus.Pending));

        await vm.CancelCommand.ExecuteAsync(null);

        await _store.Received(1).CancelAsync("c1", Arg.Any<System.Threading.CancellationToken>());
        vm.IsVisible.Should().BeFalse();
        vm.HasTrackedCommands.Should().BeFalse();
    }

    [Fact]
    public async Task FR_CONFIRM_6_The_done_message_disappears_after_a_few_seconds()
    {
        var vm = Create();

        vm.ShowCompleted();
        vm.IsDone.Should().BeTrue();
        vm.Title.Should().Contain("Google Calendar");

        _now = _start.AddSeconds(5);
        await vm.TickAsync(TestContext.Current.CancellationToken);

        vm.IsVisible.Should().BeFalse();
    }
}
