using System.Windows;

namespace Planner.App.Views;

public partial class PickListWindow : Window
{
    public PickListWindow(string title, string hint, IEnumerable<string> items)
    {
        InitializeComponent();
        Title = title;
        HintBlock.Text = hint;
        foreach (var item in items)
        {
            ItemList.Items.Add(item);
        }

        if (ItemList.Items.Count > 0)
        {
            ItemList.SelectedIndex = 0;
        }
    }

    public int SelectedIndex { get; private set; } = -1;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedIndex < 0)
        {
            return;
        }

        SelectedIndex = ItemList.SelectedIndex;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
