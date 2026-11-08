using LunarCal.Core.Abstractions;

namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CHAT-1, FR-CHAT-2, SEC-PI-2: Default implementation of <see cref="IChatOrchestrator"/>.
/// Manages state transitions, security boundary checks, confirmation gates, and tool execution.
/// </summary>
public sealed class ChatOrchestrator : IChatOrchestrator
{
    private readonly IChatModelClient _chatModelClient;
    private readonly ISystemPromptProvider _systemPromptProvider;
    private readonly IToolExecutor _toolExecutor;
    private readonly ChatOrchestratorOptions _options;
    private readonly List<ChatMessage> _messages = new();
    private string? _lastProviderState;

    /// <inheritdoc />
    public ChatOrchestratorState State { get; private set; } = ChatOrchestratorState.Idle;

    /// <inheritdoc />
    public ToolCall? PendingToolCall { get; private set; }

    /// <inheritdoc />
    public ToolValidationResult? ValidationResult { get; private set; }

    /// <inheritdoc />
    public string? ClarificationQuestion { get; private set; }

    /// <inheritdoc />
    public string? LastResponseText { get; private set; }

    /// <inheritdoc />
    public ToolExecutionResult? ExecutionResult { get; private set; }

    /// <inheritdoc />
    public string? ErrorMessage { get; private set; }

    /// <inheritdoc />
    public int SessionToolCallCount { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<ChatMessage> ConversationHistory => _messages.AsReadOnly();

    /// <inheritdoc />
    public ChatOrchestratorOptions Options => _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ChatOrchestrator"/>.
    /// </summary>
    /// <param name="chatModelClient">Vendor-neutral chat client (FR-BYOK-7).</param>
    /// <param name="systemPromptProvider">System prompt template provider (ADR-0030).</param>
    /// <param name="toolExecutor">Executor for confirmed calendar tools.</param>
    /// <param name="options">Optional orchestrator configuration options.</param>
    public ChatOrchestrator(
        IChatModelClient chatModelClient,
        ISystemPromptProvider systemPromptProvider,
        IToolExecutor toolExecutor,
        ChatOrchestratorOptions? options = null)
    {
        _chatModelClient = chatModelClient ?? throw new ArgumentNullException(nameof(chatModelClient));
        _systemPromptProvider = systemPromptProvider ?? throw new ArgumentNullException(nameof(systemPromptProvider));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _options = options ?? new ChatOrchestratorOptions();
    }

    /// <inheritdoc />
    public async Task<ChatOrchestratorState> ProcessUserMessageAsync(
        string userMessage,
        PromptContext promptContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promptContext);

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = "User message cannot be empty.";
            return State;
        }

        if (userMessage.Length > ToolDefinitions.MaxStringLength)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = $"User message exceeds max length of {ToolDefinitions.MaxStringLength} characters.";
            return State;
        }

        if (SessionToolCallCount >= _options.MaxToolCallsPerSession)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = $"Session tool call limit of {_options.MaxToolCallsPerSession} exceeded.";
            return State;
        }

        // Reset previous step results
        ErrorMessage = null;
        ValidationResult = null;
        ExecutionResult = null;
        ClarificationQuestion = null;
        PendingToolCall = null;

        State = ChatOrchestratorState.CallingModel;
        _messages.Add(ChatMessage.CreateUser(userMessage));

        var renderedPrompt = _systemPromptProvider.RenderSystemPrompt(promptContext, _options.PromptVersion);

        var request = new ChatRequest
        {
            SystemPrompt = renderedPrompt,
            Messages = _messages.ToArray(),
            Tools = ToolDefinitions.AllTools,
            ModelId = _options.ModelId,
            ProviderState = _lastProviderState
        };

        ChatResponse response;
        try
        {
            response = await _chatModelClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            State = ChatOrchestratorState.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = ex.Message;
            return State;
        }

        _lastProviderState = response.ProviderState;
        LastResponseText = response.Text;

        if (response.FinishReason == ChatFinishReason.Safety)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = "Generation was blocked by safety filters.";
            return State;
        }

        if (response.HasToolCalls)
        {
            var toolCall = response.ToolCalls[0];
            State = ChatOrchestratorState.Validating;

            var validation = ToolCallValidator.Validate(toolCall, SessionToolCallCount);
            ValidationResult = validation;

            if (!validation.IsValid)
            {
                State = ChatOrchestratorState.Failed;
                ErrorMessage = validation.ErrorMessage;
                return State;
            }

            SessionToolCallCount++;
            PendingToolCall = toolCall;

            // FR-CHAT-2, SEC-PI-2: Auto-confirm is ONLY allowed for single event creation,
            // and ONLY when the option is explicitly enabled. Deletion, bulk creation, and modifications
            // ALWAYS require manual user confirmation.
            var canAutoConfirm = _options.AutoConfirmSingleEvent &&
                                 string.Equals(toolCall.Name, ToolDefinitions.CreateEvent, StringComparison.OrdinalIgnoreCase);

            if (canAutoConfirm)
            {
                return await ExecutePendingToolAsync(cancellationToken).ConfigureAwait(false);
            }

            State = ChatOrchestratorState.AwaitingConfirmation;
            return State;
        }

        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            _messages.Add(ChatMessage.CreateModel(response.Text));
            ClarificationQuestion = response.Text;
            State = ChatOrchestratorState.AwaitingClarification;
            return State;
        }

        State = ChatOrchestratorState.Failed;
        ErrorMessage = "Model returned empty response with no actions.";
        return State;
    }

    /// <inheritdoc />
    public async Task<ChatOrchestratorState> ConfirmAsync(
        ToolCall? modifiedCall = null,
        CancellationToken cancellationToken = default)
    {
        if (State != ChatOrchestratorState.AwaitingConfirmation)
        {
            throw new InvalidOperationException(
                $"Cannot confirm when orchestrator is in '{State}' state. Expected '{ChatOrchestratorState.AwaitingConfirmation}'.");
        }

        if (modifiedCall is not null)
        {
            var validation = ToolCallValidator.Validate(modifiedCall, SessionToolCallCount);
            ValidationResult = validation;

            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return State; // Remains in AwaitingConfirmation for user to correct
            }

            PendingToolCall = modifiedCall;
        }

        return await ExecutePendingToolAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChatOrchestratorState> AnswerClarificationAsync(
        string answer,
        PromptContext promptContext,
        CancellationToken cancellationToken = default)
    {
        if (State != ChatOrchestratorState.AwaitingClarification)
        {
            throw new InvalidOperationException(
                $"Cannot answer clarification when orchestrator is in '{State}' state. Expected '{ChatOrchestratorState.AwaitingClarification}'.");
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new ArgumentException("Answer cannot be empty.", nameof(answer));
        }

        ClarificationQuestion = null;
        return await ProcessUserMessageAsync(answer, promptContext, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        State = ChatOrchestratorState.Cancelled;
        PendingToolCall = null;
        ClarificationQuestion = null;
    }

    /// <inheritdoc />
    public void Reset()
    {
        State = ChatOrchestratorState.Idle;
        PendingToolCall = null;
        ValidationResult = null;
        ExecutionResult = null;
        ClarificationQuestion = null;
        LastResponseText = null;
        ErrorMessage = null;
        SessionToolCallCount = 0;
        _lastProviderState = null;
        _messages.Clear();
    }

    private async Task<ChatOrchestratorState> ExecutePendingToolAsync(CancellationToken cancellationToken)
    {
        if (PendingToolCall is null)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = "No pending tool call to execute.";
            return State;
        }

        State = ChatOrchestratorState.Executing;

        ToolExecutionResult execResult;
        try
        {
            execResult = await _toolExecutor.ExecuteAsync(PendingToolCall, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            State = ChatOrchestratorState.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = ex.Message;
            return State;
        }

        ExecutionResult = execResult;

        if (execResult.IsSuccess)
        {
            State = ChatOrchestratorState.Completed;
            _messages.Add(ChatMessage.CreateToolResult(
                PendingToolCall.Name,
                execResult.OutputMessage ?? "Success",
                PendingToolCall.Id));
        }
        else
        {
            State = ChatOrchestratorState.Failed;
            ErrorMessage = execResult.ErrorMessage ?? "Tool execution failed.";
        }

        return State;
    }
}
