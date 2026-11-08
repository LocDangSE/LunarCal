using AwesomeAssertions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="SqliteAiCommandStore"/> (FR-QUEUE-1, FR-QUEUE-2, FR-QUEUE-8, FR-QUEUE-9).
/// </summary>
public sealed class SqliteAiCommandStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _secretsDirectory;
    private readonly string _dbPath;
    private readonly DpapiSecretStore _secretStore;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteAiCommandStore _store;

    public SqliteAiCommandStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_AiQueueTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "test_ai_queue.db");

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_secretsDirectory);

        _secretStore = new DpapiSecretStore(_secretsDirectory);
        _connectionFactory = new SqliteConnectionFactory(_secretStore, new SqliteStorageOptions { DatabasePath = _dbPath });
        _store = new SqliteAiCommandStore(_connectionFactory);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();
        _secretStore.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Transient test cleanup
            }
        }
    }

    private async Task EnsureDbAsync(CancellationToken ct)
    {
        await _connectionFactory.EnsureInitializedAsync(ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task FR_QUEUE_3_A_pending_command_with_a_retry_time_waits_for_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);
        var cmd = await _store.EnqueueAsync("họp 9h", DateTimeOffset.Now, "SE Asia Standard Time", "vi", ct);
        var later = DateTimeOffset.UtcNow.AddMinutes(10);

        await _store.UpdateStatusAsync(cmd.Id, AiCommandStatus.Pending, "network", nextRetryAt: later, cancellationToken: ct);

        (await _store.GetPendingQueueAsync(DateTimeOffset.UtcNow, 10, ct)).Should().BeEmpty("it is waiting for its backoff");
        (await _store.GetPendingQueueAsync(later.AddSeconds(1), 10, ct)).Should().ContainSingle(c => c.Id == cmd.Id);

        await _store.RetryNowAsync(cmd.Id, ct);
        (await _store.GetPendingQueueAsync(DateTimeOffset.UtcNow, 10, ct)).Should().ContainSingle(c => c.Id == cmd.Id);
    }

    [Fact]
    public async Task FR_QUEUE_5_A_command_that_fails_permanently_is_never_picked_up_again()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);
        var cmd = await _store.EnqueueAsync("họp chiến lược Q4", DateTimeOffset.Now, "SE Asia Standard Time", "vi", ct);

        // First a transient failure schedules a retry in the past...
        var past = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _store.UpdateStatusAsync(cmd.Id, AiCommandStatus.Error, "timed out", retryCount: 1, nextRetryAt: past, cancellationToken: ct);
        (await _store.GetPendingQueueAsync(DateTimeOffset.UtcNow, 10, ct)).Should().ContainSingle(c => c.Id == cmd.Id);

        // ...then a permanent failure (daily quota) must clear it. It used to keep the old retry time, so the worker
        // called Gemini for this command again and again, thousands of times (P4-R9).
        await _store.UpdateStatusAsync(cmd.Id, AiCommandStatus.Error, "daily quota", retryCount: 1, nextRetryAt: null, cancellationToken: ct);

        (await _store.GetPendingQueueAsync(DateTimeOffset.UtcNow.AddDays(1), 10, ct)).Should().BeEmpty();
        (await _store.GetByIdAsync(cmd.Id, ct))!.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public async Task FR_QUEUE_4_Typed_at_keeps_the_local_offset_after_a_round_trip_through_storage()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        // 00:30 in Vietnam on 9 Nov = 17:30 UTC on 8 Nov. Stored as UTC, it used to come back as +00:00 (P4-R5).
        var typedAt = new DateTimeOffset(2026, 11, 9, 0, 30, 0, TimeSpan.FromHours(7));
        var created = await _store.EnqueueAsync("Nhắc tôi họp lúc 6h tối mai", typedAt, "SE Asia Standard Time", "vi", ct);

        var read = await _store.GetByIdAsync(created.Id, ct);

        read!.TypedAt.Should().Be(typedAt);
        read.TypedAt.Offset.Should().Be(TimeSpan.FromHours(7));
        (read.TypedAt.Day, read.TypedAt.Hour).Should().Be((9, 0));

        var listed = (await _store.GetAllAsync(10, ct)).Single(c => c.Id == created.Id);
        listed.TypedAt.Offset.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public async Task FR_QUEUE_1_Enqueue_creates_command_with_sequential_display_no_starting_at_1000()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        var typedAt = new DateTimeOffset(2026, 10, 16, 8, 30, 0, TimeSpan.FromHours(7));

        // Act: Enqueue 1st command
        var cmd1 = await _store.EnqueueAsync("Họp sáng mai", typedAt, "SE Asia Standard Time", "vi", ct);

        // Assert 1
        cmd1.Should().NotBeNull();
        cmd1.DisplayNo.Should().Be(1000);
        cmd1.DisplayCode.Should().Be("#1000");
        cmd1.Status.Should().Be(AiCommandStatus.Pending);
        cmd1.Text.Should().Be("Họp sáng mai");
        cmd1.TimeZone.Should().Be("SE Asia Standard Time");
        cmd1.InputLang.Should().Be("vi");
        cmd1.RetryCount.Should().Be(0);
        cmd1.NextRetryAt.Should().BeNull();
        cmd1.RequestId.Should().Be($"lunarcal-ai-{cmd1.Id}");

        // Act: Enqueue 2nd command
        var cmd2 = await _store.EnqueueAsync("Remind dentist next Tuesday", typedAt, "SE Asia Standard Time", "en", ct);

        // Assert 2: sequence increments to 1001
        cmd2.DisplayNo.Should().Be(1001);
        cmd2.DisplayCode.Should().Be("#1001");
    }

    [Fact]
    public void FR_QUEUE_2_AiCommand_status_matches_AccountDataPurger_terminal_statuses()
    {
        // Assert: AiCommand terminal statuses must match AccountDataPurger.TerminalAiCommandStatuses
        AiCommandStatus.Completed.Should().Be("completed");
        AiCommandStatus.Cancelled.Should().Be("cancelled");

        AccountDataPurger.TerminalAiCommandStatuses.Should().Contain(AiCommandStatus.Completed);
        AccountDataPurger.TerminalAiCommandStatuses.Should().Contain(AiCommandStatus.Cancelled);
        AccountDataPurger.TerminalAiCommandStatuses.Should().BeEquivalentTo(AiCommandStatus.TerminalStatuses);
    }

    [Fact]
    public async Task FR_QUEUE_2_GetActiveCount_counts_only_non_terminal_commands()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var cmd1 = await _store.EnqueueAsync("Task 1", now, "UTC", "en", ct);
        var cmd2 = await _store.EnqueueAsync("Task 2", now, "UTC", "en", ct);
        var cmd3 = await _store.EnqueueAsync("Task 3", now, "UTC", "en", ct);

        // Transition cmd1 to completed, cmd2 to cancelled
        await _store.UpdateStatusAsync(cmd1.Id, AiCommandStatus.Completed, cancellationToken: ct);
        await _store.UpdateStatusAsync(cmd2.Id, AiCommandStatus.Cancelled, cancellationToken: ct);

        // Act
        var activeCount = await _store.GetActiveCountAsync(ct);

        // Assert: only cmd3 is pending / non-terminal
        activeCount.Should().Be(1);
    }

    [Fact]
    public async Task FR_QUEUE_8_Crash_recovery_resets_processing_commands_to_pending()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var cmd = await _store.EnqueueAsync("In flight task", now, "UTC", "en", ct);

        // Simulate crash: command was marked processing when app stopped abnormally
        await _store.UpdateStatusAsync(cmd.Id, AiCommandStatus.Processing, cancellationToken: ct);

        var beforeRecovery = await _store.GetByIdAsync(cmd.Id, ct);
        beforeRecovery!.Status.Should().Be(AiCommandStatus.Processing);

        // Act: App restarts, triggers crash recovery
        var recoveredCount = await _store.RecoverProcessingCommandsAsync(now, ct);

        // Assert
        recoveredCount.Should().Be(1);
        var afterRecovery = await _store.GetByIdAsync(cmd.Id, ct);
        afterRecovery!.Status.Should().Be(AiCommandStatus.Pending);
    }

    [Fact]
    public async Task FR_QUEUE_9_Cleanup_deletes_terminal_commands_older_than_retention_period()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var cmdOldCompleted = await _store.EnqueueAsync("Old completed", now.AddDays(-35), "UTC", "en", ct);
        var cmdRecentCompleted = await _store.EnqueueAsync("Recent completed", now.AddDays(-5), "UTC", "en", ct);
        var cmdOldPending = await _store.EnqueueAsync("Old pending", now.AddDays(-35), "UTC", "en", ct);

        await _store.UpdateStatusAsync(cmdOldCompleted.Id, AiCommandStatus.Completed, cancellationToken: ct);
        await _store.UpdateStatusAsync(cmdRecentCompleted.Id, AiCommandStatus.Completed, cancellationToken: ct);

        // Manually backdate updated_at of cmdOldCompleted to 35 days ago
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE AiCommand SET updated_at = @old WHERE id = @id;";
            updateCmd.Parameters.AddWithValue("@old", now.AddDays(-35).ToString("O"));
            updateCmd.Parameters.AddWithValue("@id", cmdOldCompleted.Id);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        // Act: Clean up older than 30 days
        var cutoff = now.AddDays(-30);
        var cleanedCount = await _store.CleanupOldCommandsAsync(cutoff, ct);

        // Assert
        cleanedCount.Should().Be(1);

        (await _store.GetByIdAsync(cmdOldCompleted.Id, ct)).Should().BeNull();
        (await _store.GetByIdAsync(cmdRecentCompleted.Id, ct)).Should().NotBeNull();
        (await _store.GetByIdAsync(cmdOldPending.Id, ct)).Should().NotBeNull(); // Pending commands are never cleaned up
    }

    [Fact]
    public async Task UpdateStatus_updates_metadata_and_RetryNow_resets_error_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnsureDbAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var cmd = await _store.EnqueueAsync("Test command", now, "UTC", "en", ct);

        var nextRetry = now.AddMinutes(5);
        await _store.UpdateStatusAsync(
            cmd.Id,
            AiCommandStatus.Error,
            lastError: "Rate limit 429",
            retryCount: 2,
            nextRetryAt: nextRetry,
            batchId: "batch-123",
            cancellationToken: ct);

        var updated = await _store.GetByIdAsync(cmd.Id, ct);
        updated!.Status.Should().Be(AiCommandStatus.Error);
        updated.LastError.Should().Be("Rate limit 429");
        updated.RetryCount.Should().Be(2);
        updated.NextRetryAt.Should().BeCloseTo(nextRetry, TimeSpan.FromSeconds(1));
        updated.BatchId.Should().Be("batch-123");

        // Act: RetryNowAsync resets status to pending and clears nextRetryAt
        await _store.RetryNowAsync(cmd.Id, ct);

        var retried = await _store.GetByIdAsync(cmd.Id, ct);
        retried!.Status.Should().Be(AiCommandStatus.Pending);
        retried.NextRetryAt.Should().BeNull();
    }
}
