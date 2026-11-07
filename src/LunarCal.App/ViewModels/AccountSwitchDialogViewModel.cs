using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Texts of the "Switch to a different Google account?" confirmation (Figma S14 1.5, ADR-0029): names the old and new
/// account and lists only the kinds of data that would really be lost.
/// </summary>
public sealed class AccountSwitchDialogViewModel
{
    /// <summary>Creates the texts for <paramref name="assessment"/> in the current UI language.</summary>
    public AccountSwitchDialogViewModel(AccountSignInAssessment assessment, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(localization);

        var current = assessment.CurrentAccount?.Email ?? string.Empty;
        var incoming = assessment.IncomingAccount.Email;

        Title = localization["account_switch.title"];
        Subtitle = localization.Format("account_switch.subtitle", current, incoming);
        Body = localization.Format("account_switch.body", current, incoming);
        WarningTitle = localization["account_switch.warn_title"];
        KeptText = localization["account_switch.kept"];
        KeepButtonText = localization["account_switch.keep_button"];
        ConfirmButtonText = localization["account_switch.confirm_button"];

        var lines = new List<string>();
        AddLine(lines, localization, "account_switch.warn_pending", assessment.PendingChanges);
        AddLine(lines, localization, "account_switch.warn_ai", assessment.PendingAiCommands);
        AddLine(lines, localization, "account_switch.warn_lunar", assessment.LocalLunarRules);
        WarningLines = lines;
    }

    /// <summary>"Đổi sang tài khoản Google khác?".</summary>
    public string Title { get; }

    /// <summary>"Đang đăng nhập: old · tài khoản mới: new".</summary>
    public string Subtitle { get; }

    /// <summary>What switching does to the old account's data on this computer.</summary>
    public string Body { get; }

    /// <summary>"Sẽ bị xoá khỏi máy này".</summary>
    public string WarningTitle { get; }

    /// <summary>One bullet per kind of data that exists and would be lost; empty when only the cache goes.</summary>
    public IReadOnlyList<string> WarningLines { get; }

    /// <summary>Whether the warning box is shown (there is something beyond the re-downloadable cache to lose).</summary>
    public bool HasWarning => WarningLines.Count > 0;

    /// <summary>"Được giữ lại: cài đặt, ngôn ngữ và Gemini API key".</summary>
    public string KeptText { get; }

    /// <summary>Safe choice (default): stay with the current account.</summary>
    public string KeepButtonText { get; }

    /// <summary>Destructive choice: delete the old data and switch.</summary>
    public string ConfirmButtonText { get; }

    private static void AddLine(List<string> lines, ILocalizationService localization, string key, int count)
    {
        if (count > 0)
        {
            lines.Add("• " + localization.Format(key, count));
        }
    }
}
