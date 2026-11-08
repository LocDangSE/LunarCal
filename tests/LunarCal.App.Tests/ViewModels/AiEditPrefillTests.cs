using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>FR-CONFIRM-4 (P4-10): "Sửa" in the AI confirmation dialog opens the editor filled with the proposal.</summary>
public sealed class AiEditPrefillTests
{
    private const string Zone = "SE Asia Standard Time";

    [Fact]
    public void FR_CONFIRM_4_The_prefill_reads_title_and_local_wall_clock_times_from_the_proposal()
    {
        // The model answered in UTC; the editor must show the user's 18:00.
        var call = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Họp nhóm","start":"2026-11-09T11:00:00Z","end":"2026-11-09T12:30:00Z"}""");

        var prefill = EventEditorPrefill.FromToolCall(call, Zone);

        prefill.Should().NotBeNull();
        prefill!.Title.Should().Be("Họp nhóm");
        prefill.Start.Should().Be(new DateTime(2026, 11, 9, 18, 0, 0));
        prefill.End.Should().Be(new DateTime(2026, 11, 9, 19, 30, 0));
        prefill.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public void FR_CONFIRM_4_Without_a_usable_proposal_there_is_no_prefill()
    {
        EventEditorPrefill.FromToolCall(null, Zone).Should().BeNull();
        EventEditorPrefill.FromToolCall(new ToolCall(ToolDefinitions.CreateEvent, "not json"), Zone).Should().BeNull();
        EventEditorPrefill.FromToolCall(new ToolCall(ToolDefinitions.CreateEvent, """{"title":"x"}"""), Zone).Should().BeNull();
    }

    [Fact]
    public async Task FR_CONFIRM_4_The_editor_starts_from_the_proposal_when_the_user_edits_it()
    {
        var store = Substitute.For<IEventStore>();
        store.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new List<CalendarInfo>
            {
                new() { Id = "personal", AccountId = "acc", Summary = "Cá nhân", Color = "#2F6BFF", IsPrimary = true, IsSelected = true },
            }));
        var vm = new EventEditorViewModel(store, new LocalizationService(SupportedCultures.Vietnamese), new TestDisplayPreferences(), Substitute.For<IMutationService>());
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        var prefill = new EventEditorPrefill("Họp nhóm", new DateTime(2026, 11, 9, 18, 0, 0), new DateTime(2026, 11, 9, 19, 0, 0), false);

        vm.Initialize(new EventEditorRequest(EventEditorMode.Create, Prefill: prefill));

        vm.IsNew.Should().BeTrue();
        vm.Title.Should().Be("Họp nhóm");
        vm.StartDate.Should().Be(new DateOnly(2026, 11, 9));
        vm.StartTime.Should().Be(new TimeOnly(18, 0));
        vm.EndTime.Should().Be(new TimeOnly(19, 0));
        vm.EndDate.Should().Be(new DateOnly(2026, 11, 9));
        vm.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public async Task FR_CONFIRM_4_An_all_day_proposal_shows_its_last_day_not_the_exclusive_end()
    {
        var store = Substitute.For<IEventStore>();
        store.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new List<CalendarInfo>()));
        var vm = new EventEditorViewModel(store, new LocalizationService(SupportedCultures.Vietnamese), new TestDisplayPreferences(), Substitute.For<IMutationService>());
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        var prefill = new EventEditorPrefill("Nghỉ phép", new DateTime(2026, 11, 10, 0, 0, 0), new DateTime(2026, 11, 11, 0, 0, 0), true);

        vm.Initialize(new EventEditorRequest(EventEditorMode.Create, Prefill: prefill));

        vm.IsAllDay.Should().BeTrue();
        vm.StartDate.Should().Be(new DateOnly(2026, 11, 10));
        vm.EndDate.Should().Be(new DateOnly(2026, 11, 10));
    }
}
