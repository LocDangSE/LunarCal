using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;

namespace LunarCal.App.ViewModels;

/// <summary>What the progress card under the AI command bar is showing.</summary>
public enum AiProgressState
{
    /// <summary>Nothing to show.</summary>
    Hidden,

    /// <summary>The queue is waiting for or running the model.</summary>
    Processing,

    /// <summary>The last attempt failed and the queue will try again by itself.</summary>
    Retrying,

    /// <summary>The proposal is ready but the confirmation dialog could not open by itself.</summary>
    Ready,

    /// <summary>The command failed for good; the user has to act.</summary>
    Failed,

    /// <summary>The confirmed action was written to the calendar (short lived).</summary>
    Done,
}

/// <summary>
/// P4-R7 (FR-QUEUE-2, FR-QUEUE-6, FR-CONFIRM-1): non-blocking progress for the commands the user just sent. After
/// sending, the card shows that the model is working (with elapsed time and a cancel button), shows retries, and when
/// the proposal is ready it asks the shell to open the confirmation dialog. The user can keep using the calendar while
/// the model works; nothing here writes to the calendar.
/// </summary>
public sealed partial class AiProgressViewModel : ObservableObject
{
    private const int MaxRetries = 5;
    private static readonly TimeSpan _doneVisibleFor = TimeSpan.FromSeconds(4);

    private readonly IAiCommandStore _store;
    private readonly ILocalizationService _loc;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DateTimeOffset> _tracked = new(StringComparer.Ordinal);
    private AiCommandItemViewModel? _readyItem;
    private AiCommandItemViewModel? _failedItem;
    private DateTimeOffset _doneUntil;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsRetrying))]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private AiProgressState _state = AiProgressState.Hidden;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    /// <summary>The command the card is about (for Cancel and View).</summary>
    [ObservableProperty]
    private string? _commandId;

    /// <inheritdoc cref="AiProgressViewModel"/>
    public AiProgressViewModel(IAiCommandStore store, ILocalizationService localization, Func<DateTimeOffset>? now = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _loc = localization ?? throw new ArgumentNullException(nameof(localization));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Decides whether the confirmation dialog may open by itself now (no other dialog, the window is in front, the user
    /// is not typing another command). When it returns <c>false</c> the card shows a "Xem" button instead.
    /// </summary>
    public Func<bool>? CanAutoOpen { get; set; }

    /// <summary>Raised when a tracked command needs the user's confirmation and the dialog may open now.</summary>
    public event EventHandler<AiCommandItemViewModel>? AutoOpenRequested;

    /// <summary>Raised when the user presses "Xem" on the card.</summary>
    public event EventHandler<AiCommandItemViewModel>? ViewRequested;

    /// <summary>Raised when the user asks for the queue drawer.</summary>
    public event EventHandler? OpenQueueRequested;

    /// <summary>Whether the card is shown.</summary>
    public bool IsVisible => State != AiProgressState.Hidden;

    /// <summary>Whether the model is working.</summary>
    public bool IsProcessing => State == AiProgressState.Processing;

    /// <summary>Whether a retry is scheduled.</summary>
    public bool IsRetrying => State == AiProgressState.Retrying;

    /// <summary>Whether a proposal is waiting and the dialog did not open by itself.</summary>
    public bool IsReady => State == AiProgressState.Ready;

    /// <summary>Whether the command failed for good.</summary>
    public bool IsFailed => State == AiProgressState.Failed;

    /// <summary>Whether the action was just written to the calendar.</summary>
    public bool IsDone => State == AiProgressState.Done;

    /// <summary>Whether the command is still in flight (the Cancel button applies).</summary>
    public bool IsBusy => State is AiProgressState.Processing or AiProgressState.Retrying;

    /// <summary>Whether any command is being followed (the shell only polls then).</summary>
    public bool HasTrackedCommands => _tracked.Count > 0 || State == AiProgressState.Done;

    /// <summary>Starts following a command the user just sent.</summary>
    public void Track(AiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _tracked[command.Id] = _now();
        Apply(AiProgressState.Processing, command.Id, command.DisplayNo, startedAt: _tracked[command.Id], retryCount: 0, nextRetryAt: null);
    }

    /// <summary>Shows the "added to the calendar" confirmation for a few seconds.</summary>
    public void ShowCompleted()
    {
        _readyItem = null;
        _failedItem = null;
        CommandId = null;
        Title = _loc["ai_progress.done"];
        Detail = string.Empty;
        _doneUntil = _now() + _doneVisibleFor;
        State = AiProgressState.Done;
    }

    /// <summary>Re-reads the followed commands and updates the card (called about once a second by the shell).</summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (State == AiProgressState.Done && _now() >= _doneUntil)
        {
            Hide();
        }

        if (_tracked.Count == 0)
        {
            RefreshElapsed();
            return;
        }

        AiCommand? active = null;
        var activeCount = 0;
        foreach (var id in _tracked.Keys.ToList())
        {
            var command = await _store.GetByIdAsync(id, cancellationToken).ConfigureAwait(true);
            if (command is null || command.Status is AiCommandStatus.Completed or AiCommandStatus.Cancelled)
            {
                _tracked.Remove(id);
                if (CommandId == id && IsBusy)
                {
                    Hide();
                }

                continue;
            }

            switch (command.Status)
            {
                case AiCommandStatus.NeedsConfirmation:
                    _tracked.Remove(id);
                    OnNeedsConfirmation(command);
                    break;

                case AiCommandStatus.Error when !IsRetryScheduled(command):
                    _tracked.Remove(id);
                    _failedItem = new AiCommandItemViewModel(command, _loc);
                    Apply(AiProgressState.Failed, id, command.DisplayNo, _now(), command.RetryCount, null);
                    Detail = _failedItem.RetryInfoText ?? string.Empty;
                    break;

                default:
                    activeCount++;
                    active ??= command;
                    break;
            }
        }

        if (active is not null && State is AiProgressState.Hidden or AiProgressState.Processing or AiProgressState.Retrying or AiProgressState.Done)
        {
            var retrying = active.Status == AiCommandStatus.Error;
            var startedAt = _tracked.GetValueOrDefault(active.Id, _now());
            Apply(
                retrying ? AiProgressState.Retrying : AiProgressState.Processing,
                active.Id,
                active.DisplayNo,
                startedAt,
                active.RetryCount,
                active.NextRetryAt);
            if (activeCount > 1)
            {
                Detail = string.Concat(Detail, " · ", _loc.Format("ai_progress.more", activeCount - 1));
            }
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (CommandId is null)
        {
            return;
        }

        var id = CommandId;
        _tracked.Remove(id);
        await _store.CancelAsync(id).ConfigureAwait(true);
        Hide();
    }

    [RelayCommand]
    private void View()
    {
        var item = IsFailed ? _failedItem : _readyItem;
        if (item is null)
        {
            return;
        }

        var wasReady = IsReady;
        Hide();
        if (wasReady)
        {
            ViewRequested?.Invoke(this, item);
        }
    }

    [RelayCommand]
    private void OpenQueue()
    {
        Hide();
        OpenQueueRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Dismiss() => Hide();

    private void OnNeedsConfirmation(AiCommand command)
    {
        var item = new AiCommandItemViewModel(command, _loc);
        if (CanAutoOpen?.Invoke() ?? true)
        {
            if (CommandId == command.Id || IsBusy)
            {
                Hide();
            }

            AutoOpenRequested?.Invoke(this, item);
            return;
        }

        // Another dialog is open, the window is in the background or the user is typing: do not steal the focus.
        _readyItem = item;
        CommandId = command.Id;
        Title = _loc.Format("ai_progress.ready", command.DisplayNo);
        Detail = _loc["ai_progress.ready_detail"];
        State = AiProgressState.Ready;
    }

    private static bool IsRetryScheduled(AiCommand command) =>
        command.Status == AiCommandStatus.Error && command.NextRetryAt.HasValue && command.RetryCount < MaxRetries;

    private void Apply(AiProgressState state, string id, long displayNo, DateTimeOffset startedAt, int retryCount, DateTimeOffset? nextRetryAt)
    {
        CommandId = id;
        _startedAt = startedAt;
        _nextRetryAt = nextRetryAt;
        switch (state)
        {
            case AiProgressState.Retrying:
                Title = _loc.Format("ai_progress.retrying", displayNo, Math.Max(1, retryCount), MaxRetries);
                break;
            case AiProgressState.Failed:
                Title = _loc.Format("ai_progress.failed", displayNo);
                break;
            default:
                Title = _loc.Format("ai_progress.processing", displayNo);
                break;
        }

        State = state;
        RefreshElapsed();
    }

    private DateTimeOffset _startedAt;
    private DateTimeOffset? _nextRetryAt;

    private void RefreshElapsed()
    {
        switch (State)
        {
            case AiProgressState.Processing:
                Detail = _loc.Format("ai_progress.processing_detail", FormatDuration(_now() - _startedAt));
                break;
            case AiProgressState.Retrying:
                var wait = _nextRetryAt.HasValue ? _nextRetryAt.Value - _now() : TimeSpan.Zero;
                Detail = wait > TimeSpan.Zero
                    ? _loc.Format("ai_progress.retrying_detail", FormatDuration(wait))
                    : _loc["ai_progress.retrying_now"];
                break;
        }
    }

    private string FormatDuration(TimeSpan span)
    {
        var seconds = (int)Math.Max(0, Math.Ceiling(span.TotalSeconds));
        return seconds < 60
            ? _loc.Format("ai_progress.seconds", seconds.ToString(CultureInfo.CurrentCulture))
            : _loc.Format("ai_progress.minutes_seconds", seconds / 60, seconds % 60);
    }

    private void Hide()
    {
        _readyItem = null;
        _failedItem = null;
        CommandId = null;
        Title = string.Empty;
        Detail = string.Empty;
        State = AiProgressState.Hidden;
    }
}
