using System.Windows;
using System.Windows.Automation;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.App.Views.Settings;
using LunarCal.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LunarCal.App.Views;

/// <summary>Main shell window.</summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the main window bound to its view model.</summary>
    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _services = services ?? throw new ArgumentNullException(nameof(services));

        InitializeComponent();
        DataContext = viewModel;
        viewModel.SettingsRequested += OnSettingsRequested;
        viewModel.EventEditorRequested += OnEventEditorRequested;
    }

    // Opens Settings > Appearance (Figma S13) as a modal window owned by the shell.
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var localization = _services.GetRequiredService<ILocalizationService>();
        var page = _services.GetRequiredService<AppearancePage>();
        var window = new Window
        {
            Owner = this,
            Title = localization["topbar.settings"],
            Content = page,
            Width = 760,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        window.SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        AutomationProperties.SetAutomationId(window, "SettingsWindow");
        window.ShowDialog();
    }

    // Opens Event Editor Dialog (Figma S10) as a modal dialog owned by the shell.
    private void OnEventEditorRequested(object? sender, EventEditorRequest request)
    {
        var eventStore = _services.GetRequiredService<IEventStore>();
        var localization = _services.GetRequiredService<ILocalizationService>();
        var preferences = _services.GetRequiredService<IDisplayPreferences>();
        var mutationService = _services.GetService<IMutationService>();

        var editorVm = new EventEditorViewModel(eventStore, localization, preferences, mutationService);
        editorVm.Initialize(request);

        var dialog = new EventEditorDialog(editorVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        if (dialog.ShowDialog() == true && DataContext is MainWindowViewModel mwVm)
        {
            _ = mwVm.ReloadActiveViewAsync();
        }
    }
}
