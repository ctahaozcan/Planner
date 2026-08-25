using System.Windows.Controls;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class OrgWorkView : UserControl
{
    public OrgWorkView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is OrgWorkViewModel vm)
            {
                await vm.LoadAsync();
            }
        };
    }
}
