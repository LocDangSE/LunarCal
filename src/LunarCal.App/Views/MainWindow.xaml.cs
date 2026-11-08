using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;
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
        viewModel.AiConfirmRequested += OnAiConfirmRequested;

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
    // Event handler: async void is allowed here (AGENTS.md §7); the calendars must be loaded before the form is
    // initialised, otherwise the calendar picker is empty and the event has no calendar to be written to.
    private async void OnEventEditorRequested(object? sender, EventEditorRequest request)
    {
        var eventStore = _services.GetRequiredService<IEventStore>();
        var localization = _services.GetRequiredService<ILocalizationService>();
        var preferences = _services.GetRequiredService<IDisplayPreferences>();
        var mutationService = _services.GetService<IMutationService>();

        var editorVm = new EventEditorViewModel(eventStore, localization, preferences, mutationService);
        await editorVm.LoadCalendarsAsync().ConfigureAwait(true);
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

    // Opens AI Confirmation Dialog (Figma 02, FR-CONFIRM-1..6, and S16 FR-CONFIRM-8) as a modal dialog owned by the shell.
    private async void OnAiConfirmRequested(object? sender, AiCommandItemViewModel item)
    {
        if (item is null)
        {
            return;
        }

        var validator = _services.GetService<IAiBusinessValidator>() ?? new AiBusinessValidator();
        var eventStore = _services.GetRequiredService<IEventStore>();
        var settingsStore = _services.GetRequiredService<ISettingsStore>();
        var localization = _services.GetRequiredService<ILocalizationService>();
        var preferences = _services.GetRequiredService<IDisplayPreferences>();

        var defaultStart = item.TypedAt.AddDays(1);
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            JsonSerializer.Serialize(new
            {
                summary = item.Text,
                start = defaultStart.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                end = defaultStart.AddHours(1).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
            }));

        var valResult = await validator.ValidateAsync(toolCall, eventStore, item.TypedAt).ConfigureAwait(true);
        var confResult = AiConfidenceEvaluator.Evaluate(toolCall, item.Text, item.TypedAt, valResult);

        var optIn = await settingsStore.GetBoolAsync(SettingKeys.TrainingOptIn).ConfigureAwait(true);

        var command = new AiCommand
        {
            Id = item.Id,
            DisplayNo = (int)item.DisplayNo,
            Text = item.Text,
            TypedAt = item.TypedAt,
            TimeZone = TimeZoneInfo.Local.Id,
            InputLang = item.InputLang,
            Status = item.Status,
            CreatedAt = item.TypedAt,
            UpdatedAt = item.TypedAt
        };

        var confirmVm = new AiConfirmDialogViewModel(localization);
        confirmVm.Initialize(command, toolCall, confResult, valResult, preferences.LunarEnabled, optIn);

        var dialog = new AiConfirmDialog(confirmVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var dialogResult = dialog.ShowDialog();
        if (dialogResult == true && DataContext is MainWindowViewModel mwVm)
        {
            if (confirmVm.ClarificationSubmitted)
            {
                await mwVm.AiQueueDrawer.SubmitClarificationAsync(item, confirmVm.ClarificationAnswer).ConfigureAwait(true);
            }
            else
            {
                await mwVm.AiQueueDrawer.ConfirmCommandAsync(item).ConfigureAwait(true);
            }
            await mwVm.ReloadActiveViewAsync().ConfigureAwait(true);
        }
        else if (confirmVm.RequestEdit)
        {
            var req = new EventEditorRequest(
                Mode: EventEditorMode.Create,
                Date: DateOnly.FromDateTime(item.TypedAt.LocalDateTime),
                Time: TimeOnly.FromDateTime(item.TypedAt.LocalDateTime));
            OnEventEditorRequested(this, req);
        }
    }
}
