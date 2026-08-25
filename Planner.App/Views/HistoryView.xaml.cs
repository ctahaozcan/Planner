using System.Windows.Controls;
using System.Windows.Input;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void OnItemActivate(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: HistoryRowVm row }
            && DataContext is HistoryViewModel vm)
        {
            vm.OpenCommand.Execute(row);
        }
    }
}
