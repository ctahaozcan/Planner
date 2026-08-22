using System.Windows;
using Planner.App.Services;
using Planner.App.ViewModels;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Views;

public partial class EveningCloseWindow : Window
{
    private readonly TodayViewModel _today;
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private List<TaskOccurrence> _open = [];

    public EveningCloseWindow(TodayViewModel today, TaskService tasks, IAppDialogs dialogs)
    {
        InitializeComponent();
        _today = today;
        _tasks = tasks;
        _dialogs = dialogs;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var date = DateOnly.FromDateTime(_today.SelectedDate.Date == default ? DateTime.Today : _today.SelectedDate);
        date = DateOnly.FromDateTime(DateTime.Today);
        var items = await _tasks.GetOccurrencesForDateAsync(date);
        _open = items.Where(o => o.Status != PlannerTaskStatus.Tamamlandi).ToList();
        OpenList.ItemsSource = _open.Select(o => o.Task).ToList();
        NoteBox.Text = _today.DailyNote;
    }

    private async void OnMove(object sender, RoutedEventArgs e)
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        foreach (var occ in _open)
        {
            await _tasks.MoveToDateAsync(occ.TaskId, tomorrow, occ.Date);
        }

        _today.DailyNote = NoteBox.Text;
        _dialogs.Info("Açık kayıtlar yarına taşındı.");
        await _today.LoadAsync();
        Close();
    }

    private async void OnClose(object sender, RoutedEventArgs e)
    {
        _today.DailyNote = NoteBox.Text;
        await _today.LoadAsync();
        Close();
    }
}
