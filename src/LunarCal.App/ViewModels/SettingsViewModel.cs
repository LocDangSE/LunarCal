using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Root view model for the Settings window with sidebar navigation (Figma 04, S13, S20).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private SettingsTab _activeTab = SettingsTab.Appearance;

    public AppearanceSettingsViewModel Appearance { get; }

    public GoogleAccountViewModel Account { get; }

    public AdvancedSettingsViewModel Advanced { get; }

    public SettingsViewModel(
        AppearanceSettingsViewModel appearance,
        GoogleAccountViewModel account,
        AdvancedSettingsViewModel advanced)
    {
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Advanced = advanced ?? throw new ArgumentNullException(nameof(advanced));
    }

    [RelayCommand]
    public void SelectTab(SettingsTab tab)
    {
        ActiveTab = tab;
    }
}
