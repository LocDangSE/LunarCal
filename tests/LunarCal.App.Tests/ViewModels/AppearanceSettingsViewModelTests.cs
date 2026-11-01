using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class AppearanceSettingsViewModelTests
{
    private readonly FakeSettingsStore _settingsStore;
    private readonly IThemeService _themeService;
    private readonly LocalizationService _localizationService;

    public AppearanceSettingsViewModelTests()
    {
        _settingsStore = new FakeSettingsStore();
        _settingsStore.SetString(SettingKeys.Theme, "Light");
        _settingsStore.SetString(SettingKeys.WeekStart, "Monday");
        _settingsStore.SetString(SettingKeys.TimeFormat, "24h");
        _settingsStore.SetString(SettingKeys.Timezone, "SE Asia Standard Time");
        _settingsStore.SetBool(SettingKeys.LunarEnabled, true);

        _themeService = Substitute.For<IThemeService>();
        _localizationService = new LocalizationService(SupportedCultures.Vietnamese);
    }

    [Fact]
    public async Task FR_UI_7_AppearanceSettings_initializes_with_values_from_store()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.SelectedTheme.Should().Be("Light");
        vm.IsThemeLight.Should().BeTrue();
        vm.IsThemeDark.Should().BeFalse();

        vm.SelectedWeekStart.Should().Be(DayOfWeek.Monday);
        vm.IsMondayStart.Should().BeTrue();
        vm.IsSundayStart.Should().BeFalse();

        vm.SelectedTimeFormat.Should().Be("24h");
        vm.Is24Hours.Should().BeTrue();
        vm.Is12Hours.Should().BeFalse();

        vm.SelectedTimezone.Should().Be("SE Asia Standard Time");
        vm.IsLunarEnabled.Should().BeTrue();
        vm.AvailableTimezones.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FR_UI_7_AppearanceSettings_theme_change_triggers_service_and_persists()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        vm.SelectedTheme = "Dark";

        _themeService.Received(1).ApplyTheme("Dark");
        var savedTheme = await _settingsStore.GetThemeAsync(cancellationToken: TestContext.Current.CancellationToken);
        savedTheme.Should().Be("Dark");

        vm.IsThemeDark.Should().BeTrue();
        vm.IsThemeLight.Should().BeFalse();

        // High contrast test
        vm.IsThemeHighContrast = true;
        _themeService.Received(1).ApplyTheme("HighContrast");
        vm.SelectedTheme.Should().Be("HighContrast");
    }

    [Fact]
    public async Task FR_UI_7_AppearanceSettings_week_start_change_fires_event_and_persists()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        DayOfWeek? receivedWeekStart = null;
        vm.WeekStartChanged += (_, ws) => receivedWeekStart = ws;

        vm.SelectedWeekStart = DayOfWeek.Sunday;

        receivedWeekStart.Should().Be(DayOfWeek.Sunday);
        var savedWeekStart = await _settingsStore.GetWeekStartAsync(cancellationToken: TestContext.Current.CancellationToken);
        savedWeekStart.Should().Be(DayOfWeek.Sunday);

        vm.IsSundayStart.Should().BeTrue();
        vm.IsMondayStart.Should().BeFalse();

        // Radio toggle test
        vm.IsMondayStart = true;
        receivedWeekStart.Should().Be(DayOfWeek.Monday);
        vm.SelectedWeekStart.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public async Task FR_I18N_4_AppearanceSettings_time_format_change_fires_event_and_persists()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        string? receivedFormat = null;
        vm.TimeFormatChanged += (_, fmt) => receivedFormat = fmt;

        vm.SelectedTimeFormat = TimeFormattingHelper.Format12Hours;

        receivedFormat.Should().Be("12h");
        var savedFormat = await _settingsStore.GetTimeFormatAsync(cancellationToken: TestContext.Current.CancellationToken);
        savedFormat.Should().Be("12h");

        vm.Is12Hours.Should().BeTrue();
        vm.Is24Hours.Should().BeFalse();

        // Radio toggle test
        vm.Is24Hours = true;
        receivedFormat.Should().Be("24h");
        vm.SelectedTimeFormat.Should().Be("24h");
    }

    [Fact]
    public async Task FR_UI_7_AppearanceSettings_timezone_change_fires_event_and_persists()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        string? receivedTz = null;
        vm.TimezoneChanged += (_, tz) => receivedTz = tz;

        vm.SelectedTimezone = "UTC";

        receivedTz.Should().Be("UTC");
        var savedTz = await _settingsStore.GetTimezoneAsync(cancellationToken: TestContext.Current.CancellationToken);
        savedTz.Should().Be("UTC");
    }

    [Fact]
    public async Task FR_LUNAR_6_AppearanceSettings_lunar_toggle_fires_event_and_persists()
    {
        var vm = new AppearanceSettingsViewModel(_settingsStore, _themeService, _localizationService);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        bool? receivedLunar = null;
        vm.LunarEnabledChanged += (_, enabled) => receivedLunar = enabled;

        vm.IsLunarEnabled = false;

        receivedLunar.Should().BeFalse();
        var savedLunar = await _settingsStore.GetLunarEnabledAsync(cancellationToken: TestContext.Current.CancellationToken);
        savedLunar.Should().BeFalse();
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _dict = new(StringComparer.Ordinal);

        public void SetString(string key, string? value)
        {
            if (value is null)
            {
                _dict.Remove(key);
            }
            else
            {
                _dict[key] = value;
            }
        }

        public void SetBool(string key, bool value) => _dict[key] = value ? "true" : "false";

        public ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dict.TryGetValue(key, out var val) ? val : null);

        public ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            SetString(key, value);
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
