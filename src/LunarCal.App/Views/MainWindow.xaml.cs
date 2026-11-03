using System.Windows;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
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

        // FR-SYNC-3: sync on focus; poll less often while minimized.
        Activated += (_, _) => viewModel.OnWindowActivated();
        StateChanged += (_, _) => viewModel.OnWindowMinimizedChanged(WindowState == WindowState.Minimized);
    }

    // Opens Settings Window (Figma 04, S13, S20) as a modal window owned by the shell.
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        var settingsWindow = _services.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();

        // Sign-in, sign-out or a data wipe may have happened in Settings: refresh account, calendars and views.
        if (DataContext is MainWindowViewModel vm)
        {
            _ = RefreshAfterSettingsAsync(vm);
        }
    }

    private static async Task RefreshAfterSettingsAsync(MainWindowViewModel vm)
    {
        await vm.LoadAccountAsync().ConfigureAwait(true);
        await vm.LoadCalendarsAsync().ConfigureAwait(true);
        await vm.ReloadAllViewsAsync().ConfigureAwait(true);
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
