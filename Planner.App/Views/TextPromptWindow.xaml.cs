using System.Windows;

namespace Planner.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string? initial)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ValueBox.Text = initial ?? "";
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text?.Trim() ?? "";
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
