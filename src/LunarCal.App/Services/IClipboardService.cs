namespace LunarCal.App.Services;

/// <summary>
/// Service for interacting with the system clipboard (SEC-KEY-10, ADR-0016).
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Checks if the clipboard currently contains text.
    /// </summary>
    bool ContainsText();

    /// <summary>
    /// Gets the text currently stored in the clipboard.
    /// </summary>
    string? GetText();

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    void Clear();

    /// <summary>
    /// Clears the clipboard ONLY if its current content exactly matches the provided text (SEC-KEY-10).
    /// Returns true if the clipboard was cleared, false otherwise.
    /// </summary>
    bool ClearIfMatches(string? textToClear);
}
