using System.Globalization;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// FR-CHAT-7: UI tests verifying error classification, localized messages,
/// and navigation triggers to Settings &gt; AI upon API key errors.
/// </summary>
public sealed class AiErrorHandlingUiTests
{
    private readonly LocalizationService _localization = new();

    public AiErrorHandlingUiTests()
    {
        _localization.SetCulture(new CultureInfo("vi-VN"));
    }

    private static AiCommand CreateCommand(
        string id,
        int displayNo,
        string text,
        string status,
        string? lastError,
        int retryCount = 0)
    {
        var now = DateTimeOffset.Now;
        return new AiCommand
        {
            Id = id,
            DisplayNo = displayNo,
            Text = text,
            Status = status,
            LastError = lastError,
            RetryCount = retryCount,
            TypedAt = now,
            TimeZone = TimeZoneInfo.Local.Id,
            InputLang = "vi",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    [Fact]
    public void FR_CHAT_7_ItemViewModel_enables_open_settings_for_api_key_errors()
    {
        var command = CreateCommand(
            id: "cmd-1",
            displayNo: 101,
            text: "Họp",
            status: AiCommandStatus.Error,
            lastError: "ai_error.invalid_key");

        var itemVm = new AiCommandItemViewModel(command, _localization);

        itemVm.IsApiKeyError.Should().BeTrue();
        itemVm.CanOpenAiSettings.Should().BeTrue();
        itemVm.CanRetry.Should().BeFalse();
        itemVm.RetryInfoText.Should().Contain("Cài đặt > AI");
    }

    [Fact]
    public void FR_CHAT_7_ItemViewModel_handles_safety_blocked_as_non_retryable()
    {
        var command = CreateCommand(
            id: "cmd-2",
            displayNo: 102,
            text: "Lệnh bị chặn",
            status: AiCommandStatus.Error,
            lastError: "ai_error.safety_blocked");

        var itemVm = new AiCommandItemViewModel(command, _localization);

        itemVm.IsSafetyBlocked.Should().BeTrue();
        itemVm.CanOpenAiSettings.Should().BeFalse();
        itemVm.CanRetry.Should().BeFalse();
        itemVm.RetryInfoText.Should().Contain("bộ lọc an toàn");
    }

    [Fact]
    public void FR_CHAT_7_ItemViewModel_handles_daily_quota_as_non_retryable()
    {
        var command = CreateCommand(
            id: "cmd-3",
            displayNo: 103,
            text: "Lệnh hết quota",
            status: AiCommandStatus.Error,
            lastError: "ai_error.daily_quota");

        var itemVm = new AiCommandItemViewModel(command, _localization);

        itemVm.IsDailyQuotaExceeded.Should().BeTrue();
        itemVm.CanOpenAiSettings.Should().BeFalse();
        itemVm.CanRetry.Should().BeFalse();
        itemVm.RetryInfoText.Should().Contain("nửa đêm giờ Pacific");
    }

    [Fact]
    public void FR_CHAT_7_ItemViewModel_retains_retry_button_for_transient_recoverable_errors()
    {
        var command = CreateCommand(
            id: "cmd-4",
            displayNo: 104,
            text: "Lệnh tạm thời lỗi",
            status: AiCommandStatus.Error,
            lastError: "ai_error.transient",
            retryCount: 1);

        var itemVm = new AiCommandItemViewModel(command, _localization);

        itemVm.IsApiKeyError.Should().BeFalse();
        itemVm.CanOpenAiSettings.Should().BeFalse();
        itemVm.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_ItemViewModel_displays_english_strings_when_culture_is_en_US()
    {
        _localization.SetCulture(new CultureInfo("en-US"));

        var command = CreateCommand(
            id: "cmd-en",
            displayNo: 105,
            text: "Meeting",
            status: AiCommandStatus.Error,
            lastError: "ai_error.invalid_key");

        var itemVm = new AiCommandItemViewModel(command, _localization);

        itemVm.RetryInfoText.Should().Contain("Settings > AI");
    }

    [Fact]
    public void FR_CHAT_7_AiQueueDrawer_OpenAiSettingsCommand_raises_event_and_closes_drawer()
    {
        var store = Substitute.For<IAiCommandStore>();
        var drawerVm = new AiQueueDrawerViewModel(store, _localization);
        drawerVm.IsOpen = true;

        var raised = false;
        drawerVm.OpenAiSettingsRequested += (_, _) => raised = true;

        drawerVm.OpenAiSettingsCommand.Execute(null);

        raised.Should().BeTrue();
        drawerVm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_AiCommandBar_OpenAiSettingsCommand_raises_event()
    {
        var store = Substitute.For<IAiCommandStore>();
        var barVm = new AiCommandBarViewModel(store, _localization);

        var raised = false;
        barVm.OpenAiSettingsRequested += (_, _) => raised = true;

        barVm.OpenAiSettingsCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_MainWindowViewModel_navigates_to_Ai_settings_when_requested()
    {
        var eventStore = Substitute.For<IEventStore>();
        var themeService = Substitute.For<IThemeService>();
        var settingsStore = Substitute.For<ISettingsStore>();
        var preferences = Substitute.For<IDisplayPreferences>();
        preferences.TimeZone.Returns(TimeZoneInfo.Local);

        var commandStore = Substitute.For<IAiCommandStore>();

        var mainVm = new MainWindowViewModel(
            eventStore,
            _localization,
            themeService,
            settingsStore,
            preferences,
            aiCommandStore: commandStore);

        SettingsTab? requestedTab = null;
        mainVm.SettingsTabRequested += (_, tab) => requestedTab = tab;

        // Trigger request from queue drawer
        mainVm.AiQueueDrawer.OpenAiSettingsCommand.Execute(null);

        requestedTab.Should().Be(SettingsTab.Ai);
    }
}
