using System.Windows;
using LunarCal.App.Views;

namespace LunarCal.App.Services;

/// <summary><see cref="IConfirmationService"/> that shows <see cref="ConfirmDialog"/> over the active window.</summary>
public sealed class DialogConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
        var dialog = new ConfirmDialog(request);
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
