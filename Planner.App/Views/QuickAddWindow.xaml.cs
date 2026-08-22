using System.Windows;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class QuickAddWindow : Window
{
    public QuickAddWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.QuickAddText) && string.IsNullOrEmpty(vm.QuickAddText) && IsVisible)
            {
                Close();
            }
        };
        Loaded += (_, _) => InputBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };
    }
}
