using System.Windows;

namespace Planner.App.Views;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    public string Password { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
