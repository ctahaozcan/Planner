using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class ContactsView : UserControl
{
    public ContactsView() => InitializeComponent();

    private ContactsViewModel? Vm => DataContext as ContactsViewModel;

    private async void OnSetup(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        await Vm.SetupAsync(SetupPassword.Password, SetupConfirm.Password);
        SetupPassword.Clear();
        SetupConfirm.Clear();
    }

    private async void OnUnlock(object sender, RoutedEventArgs e) => await UnlockAsync();

    private async void OnUnlockKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await UnlockAsync();
        }
    }

    private async Task UnlockAsync()
    {
        if (Vm is null)
        {
            return;
        }

        await Vm.UnlockAsync(UnlockPassword.Password);
        UnlockPassword.Clear();
    }
}
