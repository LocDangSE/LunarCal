namespace LunarCal.App.Services;

/// <summary>Content of a confirmation prompt (FR-AUTH-4, SEC-DATA-3, FR-AUTH-6).</summary>
/// <param name="Title">Short question, e.g. "Ngắt kết nối tài khoản Google?".</param>
/// <param name="Message">What will happen and what will not.</param>
/// <param name="ConfirmText">Label of the confirming button.</param>
/// <param name="IsDestructive">Shows the confirming button in the danger style.</param>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmText, bool IsDestructive);

/// <summary>Asks the user to confirm an action before it runs. Implemented by a modal dialog; faked in tests.</summary>
public interface IConfirmationService
{
    /// <summary>Returns <see langword="true"/> only when the user chose the confirming button.</summary>
    Task<bool> ConfirmAsync(ConfirmationRequest request);
}
