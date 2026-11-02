using System.Globalization;
using LunarCal.Core.Calendar;

namespace LunarCal.App.ViewModels;

/// <summary>Calendar colour values shared by every view (FR-UI-10, FR-UI-11).</summary>
public static class CalendarColors
{
    /// <summary>
    /// Colour used when a calendar has no colour of its own. Same value as the <c>AccentPrimaryBrush</c> token
    /// in <c>Themes/Light.xaml</c> (Figma 01–05).
    /// </summary>
    public const string DefaultHex = "#2F6BFF";

    /// <summary>Value of the <c>createdBy</c> extended property on events created from an AI command (FR-CHAT-2).</summary>
    public const string AiCreatorTag = "lunarcal-chat";

    // Figma 01 / S06 pairs for the four calendar colours: fill (stripe, dot) → (subtle background, text).
    private static readonly Dictionary<string, CalendarPalette> _figmaPalettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#2F6BFF"] = new("#2F6BFF", "#EAF1FF", "#1D4ED8"),
        ["#12B76A"] = new("#12B76A", "#E7F8EF", "#067647"),
        ["#F79009"] = new("#F79009", "#FFF4E5", "#B54708"),
        ["#7A5AF8"] = new("#7A5AF8", "#F1ECFF", "#5925DC"),
    };

    /// <summary>
    /// Chip colours for a calendar colour. The four Figma colours use the exact Figma pairs; any other colour
    /// (Google calendars can have 24) gets a 12 % tint for the background and a 40 % darker shade for the text.
    /// </summary>
    public static CalendarPalette Palette(string? hex)
    {
        var fill = string.IsNullOrWhiteSpace(hex) ? DefaultHex : hex.Trim();
        if (_figmaPalettes.TryGetValue(fill, out var known))
        {
            return known;
        }

        if (!TryParse(fill, out var r, out var g, out var b))
        {
            return _figmaPalettes[DefaultHex];
        }

        return new CalendarPalette(
            ToHex(r, g, b),
            ToHex(Mix(r, 255, 0.12), Mix(g, 255, 0.12), Mix(b, 255, 0.12)),
            ToHex(Mix(r, 0, 0.6), Mix(g, 0, 0.6), Mix(b, 0, 0.6)));
    }

    /// <summary>Calendars by id, for the chip colours and calendar names of a view.</summary>
    public static async Task<IReadOnlyDictionary<string, CalendarInfo>> LoadAsync(
        LunarCal.Core.Abstractions.IEventStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var calendars = await store.GetCalendarsAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        var map = new Dictionary<string, CalendarInfo>(StringComparer.Ordinal);
        foreach (var calendar in calendars ?? Array.Empty<CalendarInfo>())
        {
            if (calendar is not null)
            {
                map[calendar.Id] = calendar;
            }
        }

        return map;
    }

    /// <summary>True when the event was created by the AI command bar (shown with the ✦ sparkle in Figma).</summary>
    public static bool IsAiEvent(CalendarEvent calendarEvent) =>
        string.Equals(calendarEvent?.CreatedBy, AiCreatorTag, StringComparison.Ordinal);

    // weight = share of the calendar colour in the result
    private static byte Mix(byte channel, byte target, double weight) =>
        (byte)Math.Round((channel * weight) + (target * (1 - weight)));

    private static string ToHex(byte r, byte g, byte b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");

    private static bool TryParse(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        r = (byte)(rgb >> 16);
        g = (byte)(rgb >> 8);
        b = (byte)rgb;
        return true;
    }
}

/// <summary>Colours for one calendar: fill (stripe, dot, checkbox), subtle chip background and chip text.</summary>
public sealed record CalendarPalette(string Fill, string Subtle, string Text);
