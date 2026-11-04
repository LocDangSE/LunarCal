using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Default <see cref="IOAuthClientOverrideService"/>: mutates the shared <see cref="GoogleOAuthOptions"/> that
/// <see cref="GoogleOAuthClient"/> reads on every request (FR-AUTH-6, SEC-KEY-1).
/// </summary>
public sealed class OAuthClientOverrideService : IOAuthClientOverrideService
{
    private readonly GoogleOAuthOptions _options;
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly string _buildClientId;
    private readonly string? _buildClientSecret;

    /// <summary>Creates the service; the options' current values are remembered as the build defaults.</summary>
    public OAuthClientOverrideService(GoogleOAuthOptions options, ISettingsStore settings, ISecretStore secrets)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _buildClientId = options.ClientId;
        _buildClientSecret = options.ClientSecret;
    }

    /// <inheritdoc />
    public string? CustomClientId { get; private set; }

    /// <inheritdoc />
    public bool HasCustomClientSecret { get; private set; }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Builds before P3-R4 stored the secret in AppSetting: move it into the DPAPI store and clear the setting.
        var legacySecret = await _settings.GetStringAsync(SettingKeys.GoogleOAuthClientSecret, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(legacySecret))
        {
            await _secrets.SetSecretAsync(KnownSecretKeys.GoogleOAuthClientSecretOverride, legacySecret, cancellationToken).ConfigureAwait(false);
        }

        if (legacySecret is not null)
        {
            await _settings.SetStringAsync(SettingKeys.GoogleOAuthClientSecret, null, cancellationToken).ConfigureAwait(false);
        }

        var clientId = await _settings.GetStringAsync(SettingKeys.GoogleOAuthClientId, cancellationToken).ConfigureAwait(false);
        var secret = await _secrets.GetSecretAsync(KnownSecretKeys.GoogleOAuthClientSecretOverride, cancellationToken).ConfigureAwait(false);
        Apply(string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim(), secret);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        await _settings.SetStringAsync(SettingKeys.GoogleOAuthClientId, clientId.Trim(), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            await _secrets.SetSecretAsync(KnownSecretKeys.GoogleOAuthClientSecretOverride, clientSecret.Trim(), cancellationToken).ConfigureAwait(false);
        }

        var secret = await _secrets.GetSecretAsync(KnownSecretKeys.GoogleOAuthClientSecretOverride, cancellationToken).ConfigureAwait(false);
        Apply(clientId.Trim(), secret);
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _settings.SetStringAsync(SettingKeys.GoogleOAuthClientId, null, cancellationToken).ConfigureAwait(false);
        await _secrets.DeleteSecretAsync(KnownSecretKeys.GoogleOAuthClientSecretOverride, cancellationToken).ConfigureAwait(false);
        Apply(null, null);
    }

    private void Apply(string? clientId, string? secret)
    {
        CustomClientId = clientId;
        HasCustomClientSecret = clientId is not null && !string.IsNullOrEmpty(secret);
        _options.ClientId = clientId ?? _buildClientId;
        _options.ClientSecret = clientId is null ? _buildClientSecret : secret;
    }
}
