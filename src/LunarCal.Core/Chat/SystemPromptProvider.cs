using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using LunarCal.Core.Abstractions;

namespace LunarCal.Core.Chat;

/// <summary>
/// Default implementation of <see cref="ISystemPromptProvider"/> that loads versioned prompts
/// from embedded markdown resources in <c>LunarCal.Core.Prompts</c> (FR-CHAT-1, FR-CHAT-6, FR-QUEUE-4, spec §7, ADR-0030).
/// </summary>
public sealed class SystemPromptProvider : ISystemPromptProvider
{
    public const string V1 = "v1";

    private const string PlaceholderTypedAt = "{{TYPED_AT}}";
    private const string PlaceholderTimeZone = "{{TIME_ZONE}}";
    private const string PlaceholderUserLocale = "{{USER_LOCALE}}";
    private const string PlaceholderAvailableCalendars = "{{AVAILABLE_CALENDARS}}";

    private static readonly Assembly _coreAssembly = typeof(SystemPromptProvider).Assembly;
    private readonly ConcurrentDictionary<string, string> _cachedTemplates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string DefaultVersion => V1;

    /// <inheritdoc />
    public bool HasVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var resourceName = ResolveResourceName(version);
        return _coreAssembly.GetManifestResourceNames().Contains(resourceName, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string GetRawTemplate(string? version = null)
    {
        var targetVersion = string.IsNullOrWhiteSpace(version) ? DefaultVersion : version.Trim();

        return _cachedTemplates.GetOrAdd(targetVersion, v =>
        {
            var resourceName = ResolveResourceName(v);
            using var stream = _coreAssembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"System prompt resource not found for version '{v}' ({resourceName}).");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    /// <inheritdoc />
    public string RenderSystemPrompt(PromptContext context, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var template = GetRawTemplate(version);

        var typedAtStr = context.TypedAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var timeZoneStr = !string.IsNullOrWhiteSpace(context.TimeZoneId) ? context.TimeZoneId : "SE Asia Standard Time";
        var localeStr = !string.IsNullOrWhiteSpace(context.Locale) ? context.Locale : "vi-VN";
        var calendarsStr = context.FormatAvailableCalendars();

        return template
            .Replace(PlaceholderTypedAt, typedAtStr, StringComparison.Ordinal)
            .Replace(PlaceholderTimeZone, timeZoneStr, StringComparison.Ordinal)
            .Replace(PlaceholderUserLocale, localeStr, StringComparison.Ordinal)
            .Replace(PlaceholderAvailableCalendars, calendarsStr, StringComparison.Ordinal);
    }

    private static string ResolveResourceName(string version)
    {
        var cleanVersion = version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? version.ToLowerInvariant()
            : $"v{version.ToLowerInvariant()}";

        return $"LunarCal.Core.Prompts.system.{cleanVersion}.md";
    }
}
