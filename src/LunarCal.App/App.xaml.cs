using System.ComponentModel;
using System.Windows;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure;
using LunarCal.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LunarCal.App;

/// <summary>Application entry point: builds the Generic Host and shows the main window.</summary>
public partial class App : Application
{
    private IHost? _host;
    private bool _hostStopped;

    /// <summary>Initializes the application and hooks the asynchronous startup handler.</summary>
    public App()
    {
        Startup += OnAppStartup;
    }

    // Event handler: async void is allowed here (AGENTS.md §7). Exceptions are caught so startup never fails silently.
    private async void OnAppStartup(object sender, StartupEventArgs e)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory,
            });

            // Configure Serilog file sink with redaction and 14-day rotation (SEC-LOG-1, SEC-LOG-2, ADR-0019)
            var logDir = builder.Configuration.GetValue<string>("LunarCal:Logging:LogDirectory");
            var redactEmail = builder.Configuration.GetValue<bool>("LunarCal:Logging:RedactEmail");
            var defaultLevelStr = builder.Configuration.GetValue<string>("Logging:LogLevel:Default");
            var minLevel = Enum.TryParse<LogEventLevel>(defaultLevelStr, true, out var parsedLevel)
                ? parsedLevel
                : LogEventLevel.Information;

            var levelSwitch = new LoggingLevelSwitch(minLevel);
            var loggerConfig = new LoggerConfiguration()
                .ConfigureLunarCalLogging(logDir, minLevel, redactEmail, levelSwitch);

            Log.Logger = loggerConfig.CreateLogger();
            builder.Services.AddSerilog(Log.Logger, dispose: true);
            builder.Services.AddSingleton(sp => new LogLevelService(levelSwitch, sp.GetRequiredService<ISettingsStore>()));
            builder.Services.AddSingleton<IConfirmationService, DialogConfirmationService>();

            builder.Services.AddLunarCalInfrastructure(builder.Configuration);

            var enablePseudo = builder.Configuration.GetValue<bool>("LunarCal:Localization:EnablePseudoLocale");
            LocalizationService.Instance.IsPseudoLocaleEnabled = enablePseudo;
            builder.Services.AddSingleton<ILocalizationService>(LocalizationService.Instance);

            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<IDisplayPreferences, DisplayPreferences>();
            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddSingleton<MainWindow>();

            builder.Services.AddTransient<AppearanceSettingsViewModel>();
            builder.Services.AddTransient<GoogleAccountViewModel>();
            builder.Services.AddTransient<AdvancedSettingsViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<SettingsWindow>();
            builder.Services.AddTransient<FirstRunViewModel>();
            builder.Services.AddTransient<FirstRunWindow>();

            _host = builder.Build();

            // Load display preferences (time zone, time format, week start, lunar toggle) before any view is built,
            // so the first render already uses the user's settings (FR-UI-7).
            await _host.Services.GetRequiredService<IDisplayPreferences>().LoadAsync().ConfigureAwait(true);

            // Apply the saved theme and UI language (FR-UI-7, FR-I18N-1); both persist in AppSetting (P3-16).
            var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme(await settingsStore.GetThemeAsync().ConfigureAwait(true));
            var uiLanguage = await settingsStore.GetUiLanguageAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<LogLevelService>().LoadAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<IOAuthClientOverrideService>().LoadAsync().ConfigureAwait(true);
            LocalizationService.Instance.SetCulture(uiLanguage);

            // First run (S14, FR-AUTH-1): shown until the wizard is finished or skipped once, and on demand.
            var firstRunDone = string.Equals(
                await settingsStore.GetStringAsync(SettingKeys.FirstRunCompleted).ConfigureAwait(true), "true", StringComparison.Ordinal);
            // "--skip-first-run" lets the out-of-process UI smoke tests reach the main window on a clean profile.
            if ((!firstRunDone && !e.Args.Contains("--skip-first-run")) || e.Args.Contains("--first-run"))
            {
                // The wizard is the first window; without this, closing it would end the app (OnMainWindowClose).
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var firstRun = _host.Services.GetRequiredService<FirstRunWindow>();
                firstRun.ShowDialog();
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Closing += OnMainWindowClosing;
            window.Show();

            // Start hosted services after the window exists: the sync scheduler runs its startup sync and then polls
            // (FR-SYNC-3). Before P3-R1 the host was never started, so nothing synced in the background.
            await _host.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed.");
            MessageBox.Show(ex.Message, "LunarCal", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Stops hosted services (the sync scheduler, FR-SYNC-3) before the shell closes, so an in-flight sync is
    /// cancelled and awaited instead of being cut off by <see cref="IDisposable.Dispose"/>. The first close is
    /// deferred until <see cref="IHost.StopAsync"/> finishes (at most 5 seconds), then the window closes for real.
    /// </summary>
    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_hostStopped || _host is null || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        _hostStopped = true;
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warning(ex, "Hosted services did not stop cleanly.");
        }

        window.Close();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
