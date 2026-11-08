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

public sealed class AiQueueDrawerViewModelTests
{
    private readonly IAiCommandStore _commandStore = Substitute.For<IAiCommandStore>();
    private readonly ILocalizationService _localizationService = Substitute.For<ILocalizationService>();
    private readonly INetworkStatusService _networkStatusService = Substitute.For<INetworkStatusService>();

    public AiQueueDrawerViewModelTests()
    {
        _networkStatusService.IsOnline.Returns(true);
        _localizationService["ai_queue.title"].Returns("Hàng chờ AI");
        _localizationService["ai_queue.destructive_warning"].Returns("Lệnh xóa cần xác nhận – AI không tự xóa khi chưa xác nhận");
        _localizationService["ai_queue.status_pending"].Returns("Chờ xử lý");
        _localizationService["ai_queue.status_processing"].Returns("Đang xử lý");
        _localizationService["ai_queue.status_needs_confirmation"].Returns("Chờ xác nhận");
        _localizationService["ai_queue.status_error"].Returns("Lỗi");
        _localizationService["ai_queue.status_completed"].Returns("Hoàn tất");
        _localizationService["ai_queue.status_cancelled"].Returns("Đã huỷ");
        _localizationService.Format("ai_queue.pending_count", Arg.Any<object[]>())
            .Returns(ci => $"{ci.Arg<object[]>()?[0]} yêu cầu");
    }

    private static AiCommand CreateCommand(
        string id,
        int displayNo,
        string text,
        string status,
        int retryCount = 0,
        string? lastError = null)
    {
        return new AiCommand
        {
            Id = id,
            DisplayNo = displayNo,
            Text = text,
            TypedAt = DateTimeOffset.UtcNow,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = status,
            RetryCount = retryCount,
            LastError = lastError,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task FR_QUEUE_2_Loads_commands_and_calculates_pending_count()
    {
        var sampleCommands = new List<AiCommand>
        {
            CreateCommand("1", 1000, "Tạo sự kiện họp", AiCommandStatus.Pending),
            CreateCommand("2", 1001, "Tra cứu lịch tuần", AiCommandStatus.Processing),
            CreateCommand("3", 1002, "Xoá lịch hẹn", AiCommandStatus.NeedsConfirmation),
            CreateCommand("4", 1003, "Lịch bị lỗi", AiCommandStatus.Error, retryCount: 2, lastError: "Timeout"),
            CreateCommand("5", 1004, "Lệnh đã xong", AiCommandStatus.Completed),
            CreateCommand("6", 1005, "Lệnh đã bỏ", AiCommandStatus.Cancelled),
        };

        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiCommand>>(sampleCommands));

        using var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService);

        await vm.OpenDrawerAsync();

        vm.IsOpen.Should().BeTrue();
        vm.Commands.Should().HaveCount(6);

        // PendingCount excludes Completed and Cancelled: 4 active items
        vm.PendingCount.Should().Be(4);
        vm.PendingCountText.Should().Be("4 yêu cầu");
    }

    [Fact]
    public void FR_QUEUE_6_Action_buttons_availability_for_each_status()
    {
        var pendingItem = new AiCommandItemViewModel(CreateCommand("1", 1000, "Lệnh 1", AiCommandStatus.Pending), _localizationService);
        pendingItem.CanView.Should().BeTrue();
        pendingItem.CanCancel.Should().BeTrue();
        pendingItem.CanConfirm.Should().BeFalse();
        pendingItem.CanRetry.Should().BeFalse();
        pendingItem.CanEdit.Should().BeFalse();

        var confirmItem = new AiCommandItemViewModel(CreateCommand("2", 1001, "Lệnh 2", AiCommandStatus.NeedsConfirmation), _localizationService);
        confirmItem.CanView.Should().BeTrue();
        confirmItem.CanConfirm.Should().BeTrue();
        confirmItem.CanCancel.Should().BeTrue();
        confirmItem.CanRetry.Should().BeFalse();
        confirmItem.CanEdit.Should().BeFalse();

        var errorItem = new AiCommandItemViewModel(CreateCommand("3", 1002, "Lệnh 3", AiCommandStatus.Error, retryCount: 3), _localizationService);
        errorItem.CanView.Should().BeTrue();
        errorItem.CanRetry.Should().BeTrue();
        errorItem.CanEdit.Should().BeTrue();
        errorItem.CanCancel.Should().BeTrue();
        errorItem.CanConfirm.Should().BeFalse();

        var completedItem = new AiCommandItemViewModel(CreateCommand("4", 1003, "Lệnh 4", AiCommandStatus.Completed), _localizationService);
        completedItem.CanView.Should().BeTrue();
        completedItem.CanConfirm.Should().BeFalse();
        completedItem.CanCancel.Should().BeFalse();
        completedItem.CanRetry.Should().BeFalse();
        completedItem.CanEdit.Should().BeFalse();

        var cancelledItem = new AiCommandItemViewModel(CreateCommand("5", 1004, "Lệnh 5", AiCommandStatus.Cancelled), _localizationService);
        cancelledItem.CanView.Should().BeTrue();
        cancelledItem.CanConfirm.Should().BeFalse();
        cancelledItem.CanCancel.Should().BeFalse();
        cancelledItem.CanRetry.Should().BeFalse();
        cancelledItem.CanEdit.Should().BeFalse();
    }

    [Fact]
    public void FR_QUEUE_7_Destructive_command_shows_destructive_warning()
    {
        var deleteItem = new AiCommandItemViewModel(
            CreateCommand("1", 1000, "Xoá cuộc họp ngày mai", AiCommandStatus.NeedsConfirmation),
            _localizationService);

        deleteItem.IsDestructive.Should().BeTrue();
        deleteItem.DestructiveWarningText.Should().Be("Lệnh xóa cần xác nhận – AI không tự xóa khi chưa xác nhận");

        var normalItem = new AiCommandItemViewModel(
            CreateCommand("2", 1001, "Thêm lịch hẹn nha sĩ lúc 15h", AiCommandStatus.NeedsConfirmation),
            _localizationService);

        normalItem.IsDestructive.Should().BeFalse();
    }

    [Fact]
    public async Task FR_QUEUE_6_CancelCommand_calls_store_and_updates_status()
    {
        var cmd = CreateCommand("cmd-1", 1000, "Gặp khách hàng", AiCommandStatus.Pending);
        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiCommand>>([cmd]));

        using var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService);
        await vm.RefreshCommandsAsync(TestContext.Current.CancellationToken);

        var item = vm.Commands[0];
        await vm.CancelCommandAsync(item, TestContext.Current.CancellationToken);

        item.Status.Should().Be(AiCommandStatus.Cancelled);
        await _commandStore.Received(1).CancelAsync("cmd-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_6_RetryCommand_calls_store_and_updates_status()
    {
        var cmd = CreateCommand("cmd-2", 1001, "Gặp khách hàng", AiCommandStatus.Error);
        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiCommand>>([cmd]));

        using var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService);
        await vm.RefreshCommandsAsync(TestContext.Current.CancellationToken);

        var item = vm.Commands[0];
        await vm.RetryCommandAsync(item, TestContext.Current.CancellationToken);

        item.Status.Should().Be(AiCommandStatus.Pending);
        await _commandStore.Received(1).RetryNowAsync("cmd-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_6_EditCommand_fires_event_and_closes_drawer()
    {
        var cmd = CreateCommand("cmd-3", 1002, "Sửa lại câu lệnh", AiCommandStatus.Error);
        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiCommand>>([cmd]));

        using var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService);
        await vm.OpenDrawerAsync();

        string? editedText = null;
        vm.CommandEditRequested += (_, text) => editedText = text;

        var item = vm.Commands[0];
        vm.EditCommandCommand.Execute(item);

        editedText.Should().Be("Sửa lại câu lệnh");
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task FR_CHAT_2_Confirm_button_only_opens_the_dialog_and_never_changes_the_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var confirmation = Substitute.For<IAiCommandConfirmationService>();
        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateCommand("c1", 1000, "họp 9h", AiCommandStatus.NeedsConfirmation) });
        var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService, confirmation);
        await vm.RefreshCommandsAsync(ct);
        var raised = 0;
        vm.CommandActionRequested += (_, _) => raised++;

        vm.ConfirmCommandCommand.Execute(vm.Commands[0]);

        raised.Should().Be(1, "the button opens the confirmation dialog exactly once");
        await _commandStore.DidNotReceiveWithAnyArgs().UpdateStatusAsync(default!, default!, cancellationToken: ct);
        await confirmation.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, cancellationToken: ct);
    }

    [Fact]
    public async Task FR_CHAT_1_CompleteConfirmationAsync_runs_the_stored_proposal_without_reopening_the_dialog()
    {
        var ct = TestContext.Current.CancellationToken;
        var confirmation = Substitute.For<IAiCommandConfirmationService>();
        confirmation.ConfirmAsync("c1", null, Arg.Any<CancellationToken>()).Returns(ToolExecutionResult.Success("Họp"));
        _commandStore.GetAllAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateCommand("c1", 1000, "họp 9h", AiCommandStatus.NeedsConfirmation) });
        var vm = new AiQueueDrawerViewModel(_commandStore, _localizationService, _networkStatusService, confirmation);
        await vm.RefreshCommandsAsync(ct);
        var raised = 0;
        vm.CommandActionRequested += (_, _) => raised++;

        var result = await vm.CompleteConfirmationAsync(vm.Commands[0], cancellationToken: ct);

        result!.IsSuccess.Should().BeTrue();
        raised.Should().Be(0, "confirming must not raise the open-dialog event again (it looped before)");
        await confirmation.Received(1).ConfirmAsync("c1", null, Arg.Any<CancellationToken>());
    }
}
