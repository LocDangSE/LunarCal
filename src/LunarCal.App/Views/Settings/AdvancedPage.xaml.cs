using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views.Settings;

/// <summary>Settings > Nâng cao (Figma S20); DataContext is the <c>SettingsViewModel</c>.</summary>
public partial class AdvancedPage : UserControl
{
    private AdvancedSettingsViewModel? _advanced;
    private bool _syncing;

    /// <summary>Creates the page.</summary>
    public AdvancedPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private bool IsRevealed => SecretPlain.Visibility == Visibility.Visible;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_advanced is not null)
        {
            _advanced.PropertyChanged -= OnAdvancedPropertyChanged;
        }

        _advanced = (e.NewValue as SettingsViewModel)?.Advanced;
        if (_advanced is not null)
        {
            _advanced.PropertyChanged += OnAdvancedPropertyChanged;
        }
    }

    // After "Lưu và đăng nhập lại" the view model drops the typed secret; clear both boxes so no dots remain.
    private void OnAdvancedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdvancedSettingsViewModel.ClientSecret) && _advanced?.ClientSecret.Length == 0)
        {
            SetBoxes(string.Empty);
        }
    }

    // A PasswordBox cannot be bound (by design); copy the typed secret into the view model.
    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncing && _advanced is not null)
        {
            _advanced.ClientSecret = SecretBox.Password;
        }
    }

    private void OnPlainSecretChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && _advanced is not null)
        {
            _advanced.ClientSecret = SecretPlain.Text;
        }
    }

    // "Hiện"/"Ẩn" (S20): swap the masked box for a plain one holding the same typed text.
    private void OnRevealSecret(object sender, RoutedEventArgs e)
    {
        var reveal = !IsRevealed;
        SetBoxes(reveal ? SecretBox.Password : SecretPlain.Text);
        SecretPlain.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        SecretBox.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        BtnRevealSecret.Content = LocalizationService.Instance[reveal ? "common.hide" : "common.show"];
    }

    private void SetBoxes(string value)
    {
        _syncing = true;
        try
        {
            SecretBox.Password = value;
            SecretPlain.Text = value;
        }
        finally
        {
            _syncing = false;
        }
    }
}
