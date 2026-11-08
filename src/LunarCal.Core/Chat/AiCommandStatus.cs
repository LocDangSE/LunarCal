namespace LunarCal.Core.Chat;

/// <summary>
/// FR-QUEUE-2: Status codes for AI command lifecycle states.
/// <para>
/// Exact string codes: <c>pending</c>, <c>processing</c>, <c>needs_confirmation</c>,
/// <c>error</c>, <c>completed</c>, <c>cancelled</c>.
/// </para>
/// Must strictly match <c>AccountDataPurger.TerminalAiCommandStatuses</c> for terminal states.
/// </summary>
public static class AiCommandStatus
{
    /// <summary>Pending processing in queue (Chờ xử lý).</summary>
    public const string Pending = "pending";

    /// <summary>Currently being processed by the worker (Đang xử lý).</summary>
    public const string Processing = "processing";

    /// <summary>Awaiting manual user confirmation or clarification (Chờ xác nhận).</summary>
    public const string NeedsConfirmation = "needs_confirmation";

    /// <summary>Processing encountered an error; retrying or waiting for manual action (Lỗi).</summary>
    public const string Error = "error";

    /// <summary>Successfully executed and completed (Hoàn tất).</summary>
    public const string Completed = "completed";

    /// <summary>Cancelled by user or system (Đã huỷ).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Terminal states that indicate the command has finished processing (FR-QUEUE-2, FR-AUTH-5).
    /// </summary>
    public static readonly IReadOnlyList<string> TerminalStatuses = [Completed, Cancelled];

    /// <summary>
    /// Checks whether the given status represents a finished terminal state.
    /// </summary>
    public static bool IsTerminal(string status) =>
        string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase);
}
