using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Chat;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// P4-REVIEW regression (FR-CHAT-1, FR-CHAT-2, FR-CHAT-4, FR-QUEUE-2): the real DI graph and a real SQLite database
/// must carry a typed command all the way to a calendar event. Before the fix the orchestrator and tool executor were
/// not registered, the model's proposal was never stored, and "Confirm" only flipped the status to Completed.
/// </summary>
public sealed class AiPipelineEndToEndTests : IDisposable
{
    private const string ProposedArguments =
        """{"title": "Họp chiến lược Q4", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"LunarCal_AiE2E_{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Ignore transient cleanup failures.
        }
    }

    private ServiceProvider Build(IChatModelClient modelClient)
    {
        var services = new ServiceCollection();
        services.AddLunarCalInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LunarCal:Storage:DatabasePath"] = Path.Combine(_dir, "app.db"),
            ["LunarCal:Security:SecretStorePath"] = Path.Combine(_dir, "secrets"),
        }).Build());

        // Only the network edges are faked: the Gemini call, connectivity, and the push of mutations to Google.
        services.AddSingleton(modelClient);
        var network = Substitute.For<INetworkStatusService>();
        network.IsOnline.Returns(true);
        services.AddSingleton(network);
        services.AddSingleton<IMutationService, LocalOnlyMutationService>();
        return services.BuildServiceProvider();
    }

    private static IChatModelClient ModelProposing(string toolName, string arguments)
    {
        var client = Substitute.For<IChatModelClient>();
        client.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>()).Returns(new ChatResponse
        {
            ToolCalls = [new ToolCall(toolName, arguments)],
            FinishReason = ChatFinishReason.ToolCalls,
        });
        return client;
    }

    private static async Task SeedCalendarAsync(ServiceProvider sp, CancellationToken ct)
    {
        await using var connection = await sp.GetRequiredService<ISqliteConnectionFactory>().CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Account (id, email, display_name) VALUES ('acc-1', 'a@example.com', 'A');";
        await cmd.ExecuteNonQueryAsync(ct);

        await sp.GetRequiredService<IEventStore>().UpsertCalendarAsync(
            new CalendarInfo { Id = "cal-1", AccountId = "acc-1", Summary = "Cá nhân", IsPrimary = true }, ct);
    }

    private static AiCommandQueueWorker GetWorker(ServiceProvider sp) =>
        sp.GetServices<IHostedService>().OfType<AiCommandQueueWorker>().Single();

    private static async Task<AiCommand> EnqueueAndProcessAsync(ServiceProvider sp, CancellationToken ct)
    {
        var store = sp.GetRequiredService<IAiCommandStore>();
        var queued = await store.EnqueueAsync(
            "họp chiến lược thứ năm tuần sau 9h", new DateTimeOffset(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7)), "SE Asia Standard Time", "vi", ct);

        (await GetWorker(sp).ProcessPendingBatchAsync(ct)).Should().Be(1);
        return (await store.GetByIdAsync(queued.Id, ct))!;
    }

    [Fact]
    public async Task FR_CHAT_2_Worker_stores_the_model_proposal_and_writes_nothing_until_confirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        using var sp = Build(ModelProposing(ToolDefinitions.CreateEvent, ProposedArguments));
        await SeedCalendarAsync(sp, ct);

        var command = await EnqueueAndProcessAsync(sp, ct);

        command.Status.Should().Be(AiCommandStatus.NeedsConfirmation);
        command.Proposal.Should().NotBeNull();
        command.Proposal!.ToolCall!.Name.Should().Be(ToolDefinitions.CreateEvent);
        command.Proposal.ToolCall.ArgumentsJson.Should().Contain("Họp chiến lược Q4");

        var events = await sp.GetRequiredService<IEventStore>().GetEventsAsync(
            DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-11-30T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            null,
            ct);
        events.Should().BeEmpty("FR-CHAT-2: nothing may be written before the user confirms");
    }

    [Fact]
    public async Task FR_CHAT_1_Confirming_creates_exactly_one_event_and_a_second_confirm_does_not_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        using var sp = Build(ModelProposing(ToolDefinitions.CreateEvent, ProposedArguments));
        await SeedCalendarAsync(sp, ct);
        var command = await EnqueueAndProcessAsync(sp, ct);
        var confirmation = sp.GetRequiredService<IAiCommandConfirmationService>();

        var first = await confirmation.ConfirmAsync(command.Id, cancellationToken: ct);
        var second = await confirmation.ConfirmAsync(command.Id, cancellationToken: ct);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeFalse("a completed command is no longer waiting for confirmation");

        var store = sp.GetRequiredService<IEventStore>();
        var events = await store.GetEventsAsync(
            DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-11-30T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            null,
            ct);
        var created = events.Should().ContainSingle().Subject;
        created.Title.Should().Be("Họp chiến lược Q4");
        created.CalendarId.Should().Be("cal-1");
        created.StartUtc.Should().Be(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero));
        created.EndUtc.Should().Be(new DateTimeOffset(2026, 10, 22, 3, 0, 0, TimeSpan.Zero));
        created.ExtendedProperties["requestId"].Should().Be($"lunarcal-ai-{command.Id}");

        (await sp.GetRequiredService<IAiCommandStore>().GetByIdAsync(command.Id, ct))!.Status.Should().Be(AiCommandStatus.Completed);
    }

    [Fact]
    public async Task FR_CHAT_2_Confirm_is_refused_for_a_command_the_model_has_not_proposed_for()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        using var sp = Build(ModelProposing(ToolDefinitions.CreateEvent, ProposedArguments));
        var pending = await sp.GetRequiredService<IAiCommandStore>().EnqueueAsync(
            "tạo họp", DateTimeOffset.UtcNow, "SE Asia Standard Time", "vi", ct);

        var result = await sp.GetRequiredService<IAiCommandConfirmationService>().ConfirmAsync(pending.Id, cancellationToken: ct);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FR_CONFIRM_8_Clarification_question_is_stored_and_the_answer_requeues_the_command()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        var client = Substitute.For<IChatModelClient>();
        client.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>()).Returns(
            new ChatResponse { Text = "Bạn muốn họp lúc mấy giờ?", FinishReason = ChatFinishReason.Stop },
            new ChatResponse { ToolCalls = [new ToolCall(ToolDefinitions.CreateEvent, ProposedArguments)], FinishReason = ChatFinishReason.ToolCalls });
        using var sp = Build(client);

        var asked = await EnqueueAndProcessAsync(sp, ct);
        asked.Status.Should().Be(AiCommandStatus.NeedsConfirmation);
        asked.Proposal!.ToolCall.Should().BeNull();
        asked.Proposal.ClarificationQuestion.Should().Be("Bạn muốn họp lúc mấy giờ?");

        await sp.GetRequiredService<IAiCommandConfirmationService>().SubmitClarificationAsync(asked.Id, "9h sáng", ct);
        (await GetWorker(sp).ProcessPendingBatchAsync(ct)).Should().Be(1);

        var resolved = (await sp.GetRequiredService<IAiCommandStore>().GetByIdAsync(asked.Id, ct))!;
        resolved.Status.Should().Be(AiCommandStatus.NeedsConfirmation);
        resolved.Proposal!.ToolCall!.Name.Should().Be(ToolDefinitions.CreateEvent);

        await client.Received(1).SendAsync(
            Arg.Is<ChatRequest>(r => r.Messages.Any(m => m.Text != null && m.Text.Contains("9h sáng", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Applies the optimistic write to the local store only; the push to Google is outside this test.</summary>
    private sealed class LocalOnlyMutationService(IEventStore store) : IMutationService
    {
        public async Task<CalendarEvent> CreateEventOptimisticAsync(string accountId, string calendarId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            await store.UpsertEventAsync(calendarEvent, cancellationToken);
            return calendarEvent;
        }

        public Task<CalendarEvent> UpdateEventOptimisticAsync(string accountId, string calendarId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteEventOptimisticAsync(string accountId, string calendarId, string eventId, string? etag = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MutationProcessingResult> ProcessPendingMutationsAsync(string? accountId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
