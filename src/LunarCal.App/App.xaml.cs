using System.Windows;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LunarCal.App;

/// <summary>Application entry point: builds the Generic Host and shows the main window.</summary>
public partial class App : Application
{
    private IHost? _host;

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

            builder.Services.AddLunarCalInfrastructure(builder.Configuration);

            var enablePseudo = builder.Configuration.GetValue<bool>("LunarCal:Localization:EnablePseudoLocale");
            LocalizationService.Instance.IsPseudoLocaleEnabled = enablePseudo;
            builder.Services.AddSingleton<ILocalizationService>(LocalizationService.Instance);

            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<IDisplayPreferences, DisplayPreferences>();
            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddSingleton<MainWindow>();
            builder.Services.AddTransient<AppearanceSettingsViewModel>();
            builder.Services.AddTransient<Views.Settings.AppearancePage>();

            _host = builder.Build();

            // Load display preferences (time zone, time format, week start, lunar toggle) before any view is built,
            // so the first render already uses the user's settings (FR-UI-7).
            await _host.Services.GetRequiredService<IDisplayPreferences>().LoadAsync().ConfigureAwait(true);

            // Initialize active theme
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme("System");

            // Hosted services are started in a later phase (sync engine, AI queue); nothing to start yet.
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LunarCal", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
