using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration V4 (P4-REVIEW, ADR-0031): adds <c>AiCommand.proposal_json</c>, the tool call the model proposed (or the
/// clarification exchange) for a queued command. Without it a command in "Chờ xác nhận" had nothing to confirm.
/// </summary>
public sealed class Migration004AiCommandProposal : ISqliteMigration
{
    /// <inheritdoc />
    public int Version => 4;

    /// <inheritdoc />
    public string Description => "AI command proposal (pending tool call / clarification)";

    /// <inheritdoc />
    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE AiCommand ADD COLUMN proposal_json TEXT;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
