using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();

    private DocumentsViewModel? Vm => DataContext as DocumentsViewModel;

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (NewButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = NewButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnNewText(object sender, RoutedEventArgs e) => Vm?.NewTextCommand.Execute(null);

    private void OnNewTable(object sender, RoutedEventArgs e) => Vm?.NewTableCommand.Execute(null);

    private void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FindVisualChild<WrapPanel>(DocList) is { } wrap)
        {
            wrap.Width = Math.Max(220, e.NewSize.Width - 8);
        }
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.Selected is { } row)
        {
            Vm.OpenItemCommand.Execute(row);
        }
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DocumentRowVm row } && Vm is not null)
        {
            Vm.Selected = row;
        }
    }

    private void OnOpenCard(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentRowVm row })
        {
            Vm?.OpenItemCommand.Execute(row);
        }
    }

    private void OnShareCard(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentRowVm row })
        {
            Vm?.ShareCommand.Execute(row);
        }
    }

    private void OnDeleteCard(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentRowVm row })
        {
            Vm?.DeleteCommand.Execute(row);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
