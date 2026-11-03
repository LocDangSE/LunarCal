using System.Buffers.Text;
using System.Text.Json;
using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Helper to extract user profile details from Google ID tokens or userinfo responses (spec §6 Account table).
/// </summary>
public static class GoogleUserInfoParser
{
    /// <summary>
    /// Parses the payload of an OpenID Connect ID token (JWT) to extract <see cref="GoogleUserInfo"/>.
    /// </summary>
    public static GoogleUserInfo? ParseIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = Base64Url.DecodeFromChars(parts[1]);
            using var doc = JsonDocument.Parse(payloadBytes);
            return ExtractUserInfo(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses user information from a JSON payload (e.g. userinfo endpoint response).
    /// </summary>
    public static GoogleUserInfo? ParseUserInfoJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ExtractUserInfo(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static GoogleUserInfo? ExtractUserInfo(JsonElement root)
    {
        // 'sub' is standard OIDC, fallback to 'id' if userinfo endpoint uses legacy format
        var id = root.TryGetProperty("sub", out var subProp) ? subProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) && root.TryGetProperty("id", out var idProp))
        {
            id = idProp.GetString();
        }

        var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var picture = root.TryGetProperty("picture", out var pictureProp) ? pictureProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new GoogleUserInfo
        {
            Id = id,
            Email = email,
            DisplayName = name,
            PictureUrl = picture
        };
    }
}
