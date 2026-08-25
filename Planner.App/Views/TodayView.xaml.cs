using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Planner.App.ViewModels;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Planner.App.Views;

public partial class TodayView : UserControl
{
    private const string DragFormat = "Yaver.KanbanCard";
    private Point _dragStart;
    private TaskCardVm? _dragCard;
    private bool _dragStarted;

    public TodayView() => InitializeComponent();

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragCard = null;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragCard = (sender as FrameworkElement)?.DataContext as TaskCardVm;
        _dragStarted = false;
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCard is null || _dragStarted)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStarted = true;
        var data = new DataObject(DragFormat, _dragCard);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        _dragCard = null;
        _dragStarted = false;
        ClearDropTargets();
    }

    private void OnCardGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void OnColumnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        if (sender is FrameworkElement { DataContext: KanbanColumnVm column })
        {
            foreach (var col in Columns())
            {
                col.IsDropTarget = ReferenceEquals(col, column);
            }
        }

        e.Handled = true;
    }

    private void OnColumnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: KanbanColumnVm column })
        {
            column.IsDropTarget = false;
        }
    }

    private async void OnColumnDrop(object sender, DragEventArgs e)
    {
        ClearDropTargets();
        if (e.Data.GetData(DragFormat) is not TaskCardVm card
            || sender is not FrameworkElement element
            || element.DataContext is not KanbanColumnVm target
            || DataContext is not TodayViewModel vm)
        {
            return;
        }

        var insert = GetInsertIndex(element, e.GetPosition(element), card);
        await vm.MoveCardAsync(card, target, insert);
        e.Handled = true;
    }

    private static int GetInsertIndex(FrameworkElement columnRoot, Point pos, TaskCardVm dragged)
    {
        var items = FindVisual<ItemsControl>(columnRoot, ic => ic.ItemsSource is System.Collections.IEnumerable);
        if (items is null)
        {
            return 0;
        }

        var index = items.Items.Count;
        for (var i = 0; i < items.Items.Count; i++)
        {
            if (items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            var topLeft = container.TransformToAncestor(columnRoot).Transform(new Point(0, 0));
            var midY = topLeft.Y + container.ActualHeight / 2;
            if (pos.Y < midY)
            {
                index = i;
                break;
            }
        }

        if (items.Items.Contains(dragged) && items.Items.IndexOf(dragged) is var old && old >= 0 && old < index)
        {
            // caller adjusts when moving within the same list
        }

        return index;
    }

    private IEnumerable<KanbanColumnVm> Columns()
        => DataContext is TodayViewModel vm ? vm.Columns : [];

    private void ClearDropTargets()
    {
        foreach (var col in Columns())
        {
            col.IsDropTarget = false;
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

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var nested = FindVisual(child, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
