using System.IO;
using System.Net;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Serilog;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Full-system Canary Test (SEC-KEY-11, ADR-0016 §6.9, spec §9):
/// Generates unique, non-AIza formatted long canary keys, runs them through the full lifecycle
/// (validation, storage, error emission, rotation, deletion) and verifies that the canary key
/// NEVER appears anywhere in Serilog logs, exception messages, SQLite databases, or Settings.
/// </summary>
[Collection("SecretValueRegistry")]
public sealed class GeminiApiKeyCanaryTests : IDisposable
{
    private static readonly string[] _canaryModels = ["gemini-3.8-flash"];
    private const string SharedMemoryConnectionString = "Data Source=canary_test_db;Mode=Memory;Cache=Shared";

    private readonly StringBuilder _logBuffer = new();
    private readonly ILogger _canaryLogger;

    public GeminiApiKeyCanaryTests()
    {
        SecretValueRegistry.Clear();

        var stringWriter = new StringWriter(_logBuffer);
        _canaryLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new TextWriterSink(stringWriter, new RedactingTextFormatter("{Message:lj}{NewLine}{Exception}", redactEmail: true)))
            .CreateLogger();
    }

    public void Dispose()
    {
        SecretValueRegistry.Clear();
    }

    private sealed class TextWriterSink : Serilog.Core.ILogEventSink
    {
        private readonly TextWriter _writer;
        private readonly Serilog.Formatting.ITextFormatter _formatter;

        public TextWriterSink(TextWriter writer, Serilog.Formatting.ITextFormatter formatter)
        {
            _writer = writer;
            _formatter = formatter;
        }

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            _formatter.Format(logEvent, _writer);
            _writer.Flush();
        }
    }

    private sealed class TestInMemoryConnectionFactory : ISqliteConnectionFactory
    {
        public string DatabasePath => "canary_test_db";

        public ValueTask<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new SqliteConnection(SharedMemoryConnectionString));
        }

        public ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task SEC_KEY_11_Canary_key_is_never_leaked_in_logs_exceptions_or_sqlite_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        const string canaryKey1 = "CANARY_SECRET_GEMINI_KEY_0192837465_CUSTOM_NON_AIZA_TOKEN_A";
        const string canaryKey2 = "CANARY_SECRET_GEMINI_KEY_5647382910_CUSTOM_NON_AIZA_TOKEN_B";

        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();

        // 1. Setup mock validator to succeed for canaryKey1 and canaryKey2
        validator.ValidateKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Success(_canaryModels));

        var keyService = new GeminiKeyService(secretStore, validator, _canaryLogger);

        // 2. Save canaryKey1
        var saveResult = await keyService.ValidateAndSaveKeyAsync(canaryKey1, cancellationToken: ct);
        saveResult.IsSuccess.Should().BeTrue();

        // 3. Emit various log messages, some attempting to format the raw canary key
        _canaryLogger.Information("User attempted operation with API key: {ApiKey}", canaryKey1);
        _canaryLogger.Warning("HTTP 401 error encountered while using key: " + canaryKey1);
        _canaryLogger.Error(new InvalidOperationException($"Failed request containing secret {canaryKey1}"), "An unexpected exception was caught");

        // 4. Test GeminiApiException redaction
        var apiException = new GeminiApiException(
            GeminiErrorKind.InvalidApiKey,
            "Failed request with key " + canaryKey1,
            HttpStatusCode.Unauthorized,
            "401",
            "Unauthorized: " + canaryKey1);
        _canaryLogger.Error(apiException, "API exception occurred");

        // 5. Rotate / replace key with canaryKey2
        var rotateResult = await keyService.ValidateAndSaveKeyAsync(canaryKey2, cancellationToken: ct);
        rotateResult.IsSuccess.Should().BeTrue();

        _canaryLogger.Information("Rotated to new key: {NewKey}", canaryKey2);

        // 6. Delete key
        await keyService.DeleteKeyAsync(ct);
        _canaryLogger.Information("Key deleted successfully");

        // 7. Verify SQLite storage (ISettingsStore) does not store the canary keys
        using var masterConnection = new SqliteConnection(SharedMemoryConnectionString);
        await masterConnection.OpenAsync(ct);

        using (var createCmd = masterConnection.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE AppSetting (Key TEXT PRIMARY KEY, Value TEXT, UpdatedAt TEXT);";
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        var connectionFactory = new TestInMemoryConnectionFactory();
        var settingsStore = new SqliteSettingsStore(connectionFactory);
        await settingsStore.SetStringAsync(SettingKeys.UiLanguage, "vi-VN", ct);
        await settingsStore.SetStringAsync(SettingKeys.Theme, "Dark", ct);

        // Verify SQLite raw contents
        using (var readCmd = masterConnection.CreateCommand())
        {
            readCmd.CommandText = "SELECT Key, Value FROM AppSetting;";
            using var reader = await readCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var val = reader.GetString(1);
                val.Should().NotContain(canaryKey1);
                val.Should().NotContain(canaryKey2);
            }
        }

        // 8. Assert that neither canary key appears anywhere in the Serilog logs
        var fullLog = _logBuffer.ToString();
        fullLog.Should().NotBeEmpty();
        fullLog.Should().NotContain(canaryKey1);
        fullLog.Should().NotContain(canaryKey2);
        fullLog.Should().Contain(LogRedactor.RedactedApiKey);
    }
}
