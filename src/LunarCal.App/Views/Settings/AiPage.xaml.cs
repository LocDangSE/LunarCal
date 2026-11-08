using System.Windows;
using System.Windows.Controls;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.Views.Settings;

/// <summary>
/// Interaction logic for AiPage.xaml (Figma 04, FR-BYOK-1..6, SEC-KEY-9).
/// </summary>
public partial class AiPage : UserControl
{
    public AiPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiSettingsViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void OnCheckAndSaveClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiSettingsViewModel vm)
        {
            return;
        }

        var password = KeyPasswordBox.Password;
        try
        {
            await vm.CheckAndSaveKeyAsync(password);
        }
        finally
        {
            // SEC-KEY-9: PasswordBox is cleared immediately after save
            KeyPasswordBox.Clear();
        }
    }

    private async void OnSaveOfflineClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiSettingsViewModel vm)
        {
            return;
        }

        var password = KeyPasswordBox.Password;
        try
        {
            await vm.SaveOfflineAsync(password);
        }
        finally
        {
            // SEC-KEY-9: PasswordBox is cleared immediately after save
            KeyPasswordBox.Clear();
        }
    }

    private async void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AiSettingsViewModel vm || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is AiModelInfo selected && selected != vm.SelectedModel)
        {
            await vm.SelectModelAsync(selected);
        }
    }
}
