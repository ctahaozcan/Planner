using System.Windows;

namespace Planner.App.Views;

public partial class ReminderActionWindow : Window
{
    private readonly Func<string, Task> _snooze;

    public ReminderActionWindow(string title, Func<string, Task> snooze)
    {
        InitializeComponent();
        TitleText.Text = title;
        _snooze = snooze;
    }

    private async void On10(object sender, RoutedEventArgs e) => await Done("10m");
    private async void OnHour(object sender, RoutedEventArgs e) => await Done("1h");
    private async void OnEvening(object sender, RoutedEventArgs e) => await Done("evening");
    private async void OnTomorrow(object sender, RoutedEventArgs e) => await Done("tomorrow");
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async Task Done(string preset)
    {
        await _snooze(preset);
        Close();
    }
}
