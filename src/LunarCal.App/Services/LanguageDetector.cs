using System;
using System.Buffers;
using System.Collections.Generic;

namespace LunarCal.App.Services;

/// <summary>
/// Lightweight language detection for AI natural language commands (FR-I18N-2).
/// Distinguishes between Vietnamese ("vi"), English ("en"), or mixed ("mixed").
/// </summary>
public static class LanguageDetector
{
    private static readonly SearchValues<char> _vietnameseDiacritics = SearchValues.Create(
    [
        'à', 'á', 'ả', 'ã', 'ạ', 'ă', 'ằ', 'ắ', 'ẳ', 'ẵ', 'ặ', 'â', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ậ',
        'è', 'é', 'ẻ', 'ẽ', 'ẹ', 'ê', 'ề', 'ế', 'ể', 'ễ', 'ệ',
        'ì', 'í', 'ỉ', 'ĩ', 'ị',
        'ò', 'ó', 'ỏ', 'õ', 'ọ', 'ô', 'ồ', 'ố', 'ổ', 'ỗ', 'ộ', 'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ',
        'ù', 'ú', 'ủ', 'ũ', 'ụ', 'ư', 'ừ', 'ứ', 'ử', 'ữ', 'ự',
        'ỳ', 'ý', 'ỷ', 'ỹ', 'ỵ',
        'đ'
    ]);

    private static readonly char[] _separators = [' ', ',', '.', '!', '?', ';', ':', '\t', '\r', '\n', '-', '/', '\\', '(', ')'];

    private static readonly HashSet<string> _vietnameseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "nhắc", "nhac", "họp", "hop", "ngày", "ngay", "mai", "thứ", "thu", "tuần", "tuan",
        "lúc", "luc", "giờ", "gio", "lịch", "lich", "âm", "am", "rằm", "ram", "giỗ", "gio",
        "tháng", "thang", "năm", "nam", "tạo", "tao", "sự", "su", "kiện", "kien", "gặp", "gap",
        "đi", "di", "vào", "vao", "hôm", "hom", "nay", "chiều", "chieu", "sáng", "sang", "tối", "toi"
    };

    private static readonly HashSet<string> _englishKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "remind", "call", "meet", "meeting", "tomorrow", "today", "yesterday", "next",
        "week", "month", "year", "at", "pm", "am", "schedule", "event", "on", "in", "with",
        "doctor", "dentist", "lunch", "dinner", "breakfast", "team", "sync", "standup"
    };

    /// <summary>
    /// Detects the language of user input ("vi", "en", or "mixed").
    /// </summary>
    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "vi";
        }

        var lower = text.ToLowerInvariant();
        var hasDiacritics = lower.AsSpan().IndexOfAny(_vietnameseDiacritics) >= 0;

        var tokens = lower.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
        var viScore = 0;
        var enScore = 0;

        if (hasDiacritics)
        {
            viScore += 3;
        }

        foreach (var token in tokens)
        {
            if (_vietnameseKeywords.Contains(token))
            {
                viScore++;
            }

            if (_englishKeywords.Contains(token))
            {
                enScore++;
            }
        }

        if (viScore > 0 && enScore > 0)
        {
            return "mixed";
        }

        if (enScore > viScore)
        {
            return "en";
        }

        return "vi";
    }
}
