using System.Windows;
using Planner.App.ViewModels;

namespace Planner.App.Views;

public partial class ChatWindow : Window
{
    public ChatWindow(ChatViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}
