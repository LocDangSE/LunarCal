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
        viewModel.SettingsTabRequested += OnSettingsTabRequested;
        viewModel.EventEditorRequested += OnEventEditorRequested;
        viewModel.AiConfirmRequested += OnAiConfirmRequested;

        // FR-SYNC-3: sync on focus; poll less often while minimized.
        Activated += (_, _) => viewModel.OnWindowActivated();
        StateChanged += (_, _) => viewModel.OnWindowMinimizedChanged(WindowState == WindowState.Minimized);
    }

    // Opens Settings Window (Figma 04, S13, S20) as a modal window owned by the shell (FR-CHAT-7).
    private void OnSettingsRequested(object? sender, EventArgs e) => OpenSettingsWindow(SettingsTab.General);

    private void OnSettingsTabRequested(object? sender, SettingsTab tab) => OpenSettingsWindow(tab);

    private void OpenSettingsWindow(SettingsTab initialTab)
    {
        var settingsWindow = _services.GetRequiredService<SettingsWindow>();
        if (settingsWindow.DataContext is SettingsViewModel svm)
        {
            svm.SelectTab(initialTab);
        }

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

        // The proposal comes from the model and was stored by the queue worker; it is never rebuilt from the raw text.
        var storedCommand = await _services.GetRequiredService<IAiCommandStore>().GetByIdAsync(item.Id).ConfigureAwait(true);
        if (storedCommand is null)
        {
            return;
        }

        // "Xem" on a command that is not waiting for the user (queued, failed, done) opens the same dialog read-only:
        // with no real proposal to show, an empty create_event makes the validator block Confirm (FR-QUEUE-6).
        var isActionable = storedCommand.Status == AiCommandStatus.NeedsConfirmation;
        var proposal = storedCommand.Proposal ?? new AiCommandProposal(null);
        var askingQuestion = isActionable && proposal.ToolCall is null && proposal.ClarificationQuestion is not null;
        var toolCall = proposal.ToolCall ?? (askingQuestion ? null : new ToolCall(ToolDefinitions.CreateEvent, "{}"));
        var valResult = toolCall is null
            ? AiBusinessValidationResult.Success()
            : await validator.ValidateAsync(toolCall, eventStore, item.TypedAt).ConfigureAwait(true);
        var confResult = AiConfidenceEvaluator.Evaluate(toolCall, item.Text, item.TypedAt, valResult);

        var optIn = await settingsStore.GetBoolAsync(SettingKeys.TrainingOptIn).ConfigureAwait(true);

        var confirmVm = new AiConfirmDialogViewModel(localization);
        if (toolCall is null)
        {
            // FR-CONFIRM-8: the model asked a question instead of proposing an action.
            confirmVm.InitializeForClarification(
                storedCommand,
                proposal.ClarificationQuestion ?? storedCommand.LastError ?? string.Empty,
                isLunarEnabled: preferences.LunarEnabled,
                isTrainingOptIn: optIn);
        }
        else
        {
            confirmVm.Initialize(storedCommand, toolCall, confResult, valResult, preferences.LunarEnabled, optIn);
        }

        var dialog = new AiConfirmDialog(confirmVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var dialogResult = dialog.ShowDialog();
        if (dialogResult == true && isActionable && DataContext is MainWindowViewModel mwVm)
        {
            if (confirmVm.ClarificationSubmitted)
            {
                await mwVm.AiQueueDrawer.SubmitClarificationAsync(item, confirmVm.ClarificationAnswer).ConfigureAwait(true);
            }
            else
            {
                // Only here, after the user pressed Confirm, is the calendar written (FR-CHAT-2).
                await mwVm.AiQueueDrawer.CompleteConfirmationAsync(item).ConfigureAwait(true);
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
