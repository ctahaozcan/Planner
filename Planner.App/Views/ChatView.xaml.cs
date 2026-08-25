using System.Windows.Controls;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is ChatViewModel vm)
            {
                await vm.LoadAsync();
            }
        };
    }
}
