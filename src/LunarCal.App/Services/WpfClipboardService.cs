using System.Windows;

namespace LunarCal.App.Services;

/// <summary>
/// WPF implementation of <see cref="IClipboardService"/> (SEC-KEY-10, ADR-0016).
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public bool ContainsText()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch
        {
            return false;
        }
    }

    public string? GetText()
    {
        try
        {
            return Clipboard.GetText();
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            Clipboard.Clear();
        }
        catch
        {
            // Clipboard access failure is ignored
        }
    }

    public bool ClearIfMatches(string? textToClear)
    {
        if (string.IsNullOrEmpty(textToClear))
        {
            return false;
        }

        try
        {
            if (Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), textToClear, StringComparison.Ordinal))
            {
                Clipboard.Clear();
                return true;
            }
        }
        catch
        {
            // Clipboard access failure is ignored
        }

        return false;
    }
}
