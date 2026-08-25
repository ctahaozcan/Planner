using System.Windows;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class AuthWindow : Window
{
    private readonly AuthViewModel _vm;

    public AuthWindow(AuthViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += ok =>
        {
            try { DialogResult = ok; }
            catch { Close(); }
        };
        Loaded += async (_, _) => await vm.InitializeAsync();
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
        await _vm.SubmitCommand.ExecuteAsync(null);
    }

    private async void OnPasswordKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            _vm.Password = PasswordBox.Password;
            await _vm.SubmitCommand.ExecuteAsync(null);
        }
    }
}
