using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for the AI command queue drawer (Figma 03, FR-QUEUE-2, FR-QUEUE-6, FR-CONFIRM-8).
/// </summary>
public sealed partial class AiQueueDrawerViewModel : ObservableObject, IDisposable
{
    private readonly IAiCommandStore _commandStore;
    private readonly ILocalizationService _localizationService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private string _pendingCountText = string.Empty;

    public ObservableCollection<AiCommandItemViewModel> Commands { get; } = new();

    /// <summary>
    /// Raised when the user requests editing a command (putting its text back into the command bar).
    /// </summary>
    public event EventHandler<string>? CommandEditRequested;

    /// <summary>
    /// Raised when the user requests viewing/confirming a command.
    /// </summary>
    public event EventHandler<AiCommandItemViewModel>? CommandActionRequested;

    /// <summary>
    /// Raised when the user requests navigating to AI Settings (e.g. to fix an invalid API key, FR-CHAT-7).
    /// </summary>
    public event EventHandler? OpenAiSettingsRequested;

    public AiQueueDrawerViewModel(
        IAiCommandStore commandStore,
        ILocalizationService localizationService,
        INetworkStatusService? networkStatusService = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _networkStatusService = networkStatusService;

        if (_networkStatusService is not null)
        {
            _isOffline = !_networkStatusService.IsOnline;
            _networkStatusService.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        UpdatePendingCountText();
        _localizationService.PropertyChanged += (_, _) => UpdatePendingCountText();
    }

    private void OnNetworkAvailabilityChanged(object? sender, bool isOnline)
    {
        IsOffline = !isOnline;
    }

    private void UpdatePendingCountText()
    {
        PendingCountText = _localizationService.Format("ai_queue.pending_count", PendingCount);
    }

    partial void OnPendingCountChanged(int value)
    {
        UpdatePendingCountText();
    }

    [RelayCommand]
    public async Task OpenDrawerAsync()
    {
        IsOpen = true;
        await RefreshCommandsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public void CloseDrawer()
    {
        IsOpen = false;
    }

    [RelayCommand]
    public async Task ToggleDrawerAsync()
    {
        if (IsOpen)
        {
            CloseDrawer();
        }
        else
        {
            await OpenDrawerAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task RefreshCommandsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var list = await _commandStore.GetAllAsync(limit: 50, cancellationToken)
                .ConfigureAwait(true);

            RunOnUi(() =>
            {
                Commands.Clear();
                foreach (var cmd in list)
                {
                    Commands.Add(new AiCommandItemViewModel(cmd, _localizationService));
                }

                PendingCount = Commands.Count(c => c.Status != AiCommandStatus.Completed && c.Status != AiCommandStatus.Cancelled);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CancelCommandAsync(AiCommandItemViewModel? item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return;
        }

        await _commandStore.CancelAsync(item.Id, cancellationToken).ConfigureAwait(true);

        item.Status = AiCommandStatus.Cancelled;
        PendingCount = Commands.Count(c => c.Status != AiCommandStatus.Completed && c.Status != AiCommandStatus.Cancelled);
    }

    [RelayCommand]
    public async Task RetryCommandAsync(AiCommandItemViewModel? item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return;
        }

        await _commandStore.RetryNowAsync(item.Id, cancellationToken).ConfigureAwait(true);

        item.Status = AiCommandStatus.Pending;
        PendingCount = Commands.Count(c => c.Status != AiCommandStatus.Completed && c.Status != AiCommandStatus.Cancelled);
    }

    [RelayCommand]
    public async Task ConfirmCommandAsync(AiCommandItemViewModel? item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return;
        }

        CommandActionRequested?.Invoke(this, item);

        // Transition to Completed upon user confirmation
        await _commandStore.UpdateStatusAsync(item.Id, AiCommandStatus.Completed, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        item.Status = AiCommandStatus.Completed;
        PendingCount = Commands.Count(c => c.Status != AiCommandStatus.Completed && c.Status != AiCommandStatus.Cancelled);
    }

    public async Task SubmitClarificationAsync(AiCommandItemViewModel? item, string answer, CancellationToken cancellationToken = default)
    {
        if (item is null || string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        CommandActionRequested?.Invoke(this, item);

        // Transition to Completed upon submitting clarification answer and executing the resolved action
        await _commandStore.UpdateStatusAsync(item.Id, AiCommandStatus.Completed, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        item.Status = AiCommandStatus.Completed;
        PendingCount = Commands.Count(c => c.Status != AiCommandStatus.Completed && c.Status != AiCommandStatus.Cancelled);
    }

    [RelayCommand]
    public void EditCommand(AiCommandItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        CommandEditRequested?.Invoke(this, item.Text);
        CloseDrawer();
    }

    [RelayCommand]
    public void OpenAiSettings(AiCommandItemViewModel? item)
    {
        OpenAiSettingsRequested?.Invoke(this, EventArgs.Empty);
        CloseDrawer();
    }

    [RelayCommand]
    public void ViewCommand(AiCommandItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        CommandActionRequested?.Invoke(this, item);
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_networkStatusService is not null)
        {
            _networkStatusService.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
    }
}
