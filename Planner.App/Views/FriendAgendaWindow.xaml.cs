using System.Windows;
using Planner.Core.Models;

namespace Planner.App.Views;

public sealed class AgendaLine
{
    public required string Title { get; init; }
    public required string When { get; init; }
}

public partial class FriendAgendaWindow : Window
{
    public FriendAgendaWindow(string ownerName, IEnumerable<AgendaLine> items, string? hint = null)
    {
        InitializeComponent();
        Title = ownerName + " · ajanda";
        var list = items.ToList();
        ItemList.ItemsSource = list;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintBlock.Text = hint;
        }
        else if (list.Count == 0)
        {
            HintBlock.Text = "Bu dönemde görüntülenecek kayıt yok. Salt okunur.";
        }
    }

    public static IEnumerable<AgendaLine> FromSignals(IEnumerable<AgendaItemSignal> items)
        => items.Select(i => new AgendaLine { Title = i.Title, When = i.When });
}
