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

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        // Initialize active theme
        var themeService = _host.Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme("System");

        // Hosted services are started in a later phase (sync engine, AI queue); nothing to start yet.
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
