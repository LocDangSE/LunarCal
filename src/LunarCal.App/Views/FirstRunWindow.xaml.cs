using System.Windows;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for FirstRunWindow.xaml (Figma S14, FR-AUTH-1, FR-BYOK-1..3).
/// </summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        viewModel.Finished += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    private async void OnFirstRunSaveAiClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FirstRunViewModel vm)
        {
            return;
        }

        var password = Step3PasswordBox.Password;
        try
        {
            await vm.SaveAiKeyAndCompleteAsync(password);
        }
        finally
        {
            // SEC-KEY-9: Cleared immediately after reading
            Step3PasswordBox.Clear();
        }
    }
}
