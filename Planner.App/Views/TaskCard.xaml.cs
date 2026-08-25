using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class TaskCard : UserControl
{
    public TaskCard()
    {
        InitializeComponent();
        MouseLeftButtonUp += OnClick;
        MouseDoubleClick += OnDoubleClick;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (DataContext is TaskCardVm vm)
        {
            vm.DetailsCommand.Execute(null);
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (DataContext is TaskCardVm vm)
        {
            vm.EditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
