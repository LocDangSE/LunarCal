using System.Windows;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Infrastructure;
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
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

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
