using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class AiCommandBarViewModelTests
{
    private readonly IAiCommandStore _commandStore = Substitute.For<IAiCommandStore>();
    private readonly ILocalizationService _localizationService = Substitute.For<ILocalizationService>();
    private readonly INetworkStatusService _networkStatusService = Substitute.For<INetworkStatusService>();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly IDisplayPreferences _preferences = Substitute.For<IDisplayPreferences>();

    public AiCommandBarViewModelTests()
    {
        _networkStatusService.IsOnline.Returns(true);
        _preferences.TimeZone.Returns(TimeZoneInfo.Utc);
        _localizationService["ai.offline_banner"].Returns("Không có kết nối");
        _settingsStore.SetBool(SettingKeys.GeminiEnabled, true);

        _commandStore.EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new AiCommand
            {
                Id = "cmd-1",
                DisplayNo = 1000,
                Text = ci.ArgAt<string>(0),
                TypedAt = ci.ArgAt<DateTimeOffset>(1),
                TimeZone = ci.ArgAt<string>(2),
                InputLang = ci.ArgAt<string>(3),
                Status = AiCommandStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }));
    }

    [Fact]
    public async Task FR_UI_5_SendCommand_enqueues_command_and_clears_text()
    {
        using var vm = new AiCommandBarViewModel(
            _commandStore,
            _localizationService,
            _networkStatusService,
            _settingsStore,
            _preferences);

        var enqueuedRaised = false;
        AiCommand? enqueuedCommand = null;
        vm.CommandEnqueued += (_, cmd) =>
        {
            enqueuedRaised = true;
            enqueuedCommand = cmd;
        };

        vm.CommandText = "Họp nhóm thứ 5 lúc 9h";
        vm.CanSubmit.Should().BeTrue();

        await vm.SendCommandAsync(TestContext.Current.CancellationToken);

        vm.CommandText.Should().BeEmpty();
        enqueuedRaised.Should().BeTrue();
        enqueuedCommand.Should().NotBeNull();
        enqueuedCommand!.Text.Should().Be("Họp nhóm thứ 5 lúc 9h");
        enqueuedCommand.InputLang.Should().Be("vi");

        await _commandStore.Received(1).EnqueueAsync(
            "Họp nhóm thứ 5 lúc 9h",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            "vi",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FR_I18N_2_DetectedLanguage_updates_dynamically_based_on_text()
    {
        using var vm = new AiCommandBarViewModel(
            _commandStore,
            _localizationService,
            _networkStatusService,
            _settingsStore,
            _preferences);

        vm.DetectedLanguage.Should().Be("VI · EN");

        vm.CommandText = "Nhắc tôi đi chợ ngày mai";
        vm.DetectedLanguage.Should().Be("VI");

        vm.CommandText = "Remind me to call mom tomorrow at 7pm";
        vm.DetectedLanguage.Should().Be("EN");

        vm.CommandText = "Meeting với đối tác lúc 10h sáng mai";
        vm.DetectedLanguage.Should().Be("VI · EN");

        vm.CommandText = string.Empty;
        vm.DetectedLanguage.Should().Be("VI · EN");
    }

    [Fact]
    public async Task FR_QUEUE_3_Offline_banner_shown_when_offline_and_command_still_enqueues()
    {
        _networkStatusService.IsOnline.Returns(false);

        using var vm = new AiCommandBarViewModel(
            _commandStore,
            _localizationService,
            _networkStatusService,
            _settingsStore,
            _preferences);

        vm.IsOffline.Should().BeTrue();
        vm.IsOfflineBannerVisible.Should().BeTrue();

        // User dismisses offline banner
        vm.DismissOfflineBannerCommand.Execute(null);
        vm.IsOfflineBannerVisible.Should().BeFalse();

        // Enqueueing still works completely offline
        vm.CommandText = "Ăn tối cùng gia đình lúc 19h";
        await vm.SendCommandAsync(TestContext.Current.CancellationToken);

        vm.CommandText.Should().BeEmpty();
        await _commandStore.Received(1).EnqueueAsync(
            "Ăn tối cùng gia đình lúc 19h",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            "vi",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_3_Long_command_truncated_to_4000_chars()
    {
        using var vm = new AiCommandBarViewModel(
            _commandStore,
            _localizationService,
            _networkStatusService,
            _settingsStore,
            _preferences);

        var longText = new string('A', 5000);
        vm.CommandText = longText;

        await vm.SendCommandAsync(TestContext.Current.CancellationToken);

        await _commandStore.Received(1).EnqueueAsync(
            Arg.Is<string>(s => s.Length == 4000),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _dict = new(StringComparer.Ordinal);

        public void SetBool(string key, bool value) => _dict[key] = value ? "true" : "false";

        public ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dict.TryGetValue(key, out var val) ? val : null);

        public ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            if (value is null)
            {
                _dict.Remove(key);
            }
            else
            {
                _dict[key] = value;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(_dict);

        public ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dict.TryGetValue(key, out var val) && bool.TryParse(val, out var b) ? b : defaultValue);

        public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
        {
            SetBool(key, value);
            return ValueTask.CompletedTask;
        }
    }
}
