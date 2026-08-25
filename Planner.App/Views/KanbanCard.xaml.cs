using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Planner.App.ViewModels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Planner.App.Views;

public partial class KanbanCard : UserControl
{
    private readonly DispatcherTimer _clickTimer;
    private Point _down;
    private bool _moved;
    private bool _armed;

    public KanbanCard()
    {
        InitializeComponent();
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _clickTimer.Tick += OnClickTimer;
        PreviewMouseLeftButtonDown += OnPreviewDown;
        PreviewMouseMove += OnPreviewMove;
        PreviewMouseLeftButtonUp += OnPreviewUp;
        MouseDoubleClick += OnDoubleClick;
    }

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _armed = false;
            _clickTimer.Stop();
            return;
        }

        _down = e.GetPosition(this);
        _moved = false;
        _armed = true;
        if (e.ClickCount >= 2)
        {
            _clickTimer.Stop();
            _armed = false;
            if (DataContext is TaskCardVm vm)
            {
                vm.EditCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (!_armed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _down.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(pos.Y - _down.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _moved = true;
            _clickTimer.Stop();
        }
    }

    private void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (!_armed || _moved || e.ClickCount >= 2)
        {
            _armed = false;
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            _armed = false;
            return;
        }

        _clickTimer.Stop();
        _clickTimer.Start();
        _armed = false;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _clickTimer.Stop();
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

    private void OnClickTimer(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        if (DataContext is TaskCardVm vm)
        {
            vm.DetailsCommand.Execute(null);
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
