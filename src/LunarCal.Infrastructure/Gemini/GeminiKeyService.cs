using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;
using Serilog;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Manages the Gemini BYOK API key: DPAPI storage, masked display, runtime redaction,
/// validation, deletion and reaction to suspected key leaks (FR-BYOK-1..4, SEC-KEY-1..11, ADR-0016).
/// </summary>
public sealed class GeminiKeyService : IAiKeyService
{
    private readonly ISecretStore _secretStore;
    private readonly IAiKeyValidator _validator;
    private readonly ILogger _logger;
    private bool _isQueuePaused;

    public bool IsQueuePaused => _isQueuePaused;
    public event EventHandler<bool>? QueuePausedChanged;

    public GeminiKeyService(
        ISecretStore secretStore,
        IAiKeyValidator validator,
        ILogger? logger = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? Log.ForContext<GeminiKeyService>();
    }

    public async Task<bool> HasKeyAsync(CancellationToken cancellationToken = default)
    {
        var key = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(key))
        {
            // Ensure runtime redactor knows the active key
            SecretValueRegistry.Register(key);
            return true;
        }

        return false;
    }

    public async Task<string?> GetMaskedKeyAsync(CancellationToken cancellationToken = default)
    {
        var key = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        SecretValueRegistry.Register(key);
        return MaskKey(key);
    }

    public async Task<KeyValidationResult> ValidateAndSaveKeyAsync(
        string apiKey,
        bool saveWithoutValidation = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return KeyValidationResult.Failed(KeyValidationStatus.EmptyKey, "API key cannot be empty.");
        }

        var trimmedKey = apiKey.Trim();
        KeyValidationResult validationResult;

        if (!saveWithoutValidation)
        {
            validationResult = await _validator.ValidateKeyAsync(trimmedKey, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsSuccess)
            {
                return validationResult;
            }
        }
        else
        {
            validationResult = KeyValidationResult.Success();
        }

        // Unregister previous key if any
        var oldKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(oldKey))
        {
            SecretValueRegistry.Unregister(oldKey);
        }

        // Store new key and register for redaction
        await _secretStore.SetSecretAsync(KnownSecretKeys.GeminiApiKey, trimmedKey, cancellationToken).ConfigureAwait(false);
        SecretValueRegistry.Register(trimmedKey);

        if (_isQueuePaused)
        {
            SetQueuePaused(false);
        }

        _logger.Information("Gemini API key successfully saved and registered (validation skipped: {Skipped})", saveWithoutValidation);
        return validationResult;
    }

    public async Task DeleteKeyAsync(CancellationToken cancellationToken = default)
    {
        var currentKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(currentKey))
        {
            SecretValueRegistry.Unregister(currentKey);
        }

        await _secretStore.DeleteSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        _logger.Information("Gemini API key removed from secret storage and runtime redactor");
    }

    public async Task SuspectLeakedAsync(CancellationToken cancellationToken = default)
    {
        await DeleteKeyAsync(cancellationToken).ConfigureAwait(false);
        SetQueuePaused(true);
        _logger.Warning("Gemini API key marked as suspected leak: key wiped and AI queue paused");
    }

    public string MaskKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        var trimmed = apiKey.Trim();
        if (trimmed.StartsWith("AIza", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 8)
        {
            return $"AIza••••{trimmed[^4..]}";
        }

        if (trimmed.Length >= 4)
        {
            return $"••••{trimmed[^4..]}";
        }

        return "••••";
    }

    private void SetQueuePaused(bool paused)
    {
        if (_isQueuePaused != paused)
        {
            _isQueuePaused = paused;
            QueuePausedChanged?.Invoke(this, paused);
        }
    }
}
