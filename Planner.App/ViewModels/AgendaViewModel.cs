using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class AgendaViewModel : ObservableObject
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private readonly PriorityService _priorities;
    private readonly LeaveService _leaves;

    public AgendaViewModel(TaskService tasks, IAppDialogs dialogs, PriorityService priorities, LeaveService leaves)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        _priorities = priorities;
        _leaves = leaves;
    }

    [ObservableProperty] private bool _isEmpty = true;

    public ObservableCollection<DayAgendaVm> Days { get; } = new();

    public TaskCardCallbacks Callbacks => new()
    {
        SetStatus = SetStatusAsync,
        Edit = EditAsync,
        Delete = DeleteAsync,
        Skip = SkipAsync,
        Snooze = SnoozeAsync
    };

    [RelayCommand]
    private async Task NewTaskAsync()
    {
        if (await _dialogs.EditTaskAsync(null, DateOnly.FromDateTime(DateTime.Today)))
        {
            await LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        var from = DateOnly.FromDateTime(DateTime.Today);
        var to = from.AddDays(13);
        var items = await _tasks.GetOccurrencesRangeAsync(from, to);
        var lookup = items.ToLookup(t => t.Date);
        var leaves = await _leaves.GetRangeAsync(from, to);
        var ctx = await _leaves.GetCountContextAsync();

        Days.Clear();
        for (var i = 0; i < 14; i++)
        {
            var date = from.AddDays(i);
            var day = new DayAgendaVm
            {
                Date = date,
                Title = date.ToString("dddd", Tr),
                Subtitle = date.ToString("d MMMM", Tr),
                IsToday = i == 0
            };
            foreach (var leave in leaves.Where(l => LeaveMath.Covers(l, date)))
            {
                day.Leaves.Add(LeaveBannerVm.From(leave, date, ctx));
            }
            foreach (var occ in lookup[date])
            {
                day.Items.Add(new TaskCardVm(occ, Callbacks));
            }

            Days.Add(day);
        }

        IsEmpty = items.Count == 0 && leaves.Count == 0;
    }

    private async Task SetStatusAsync(TaskCardVm card, PlannerTaskStatus status)
    {
        await _tasks.SetStatusAsync(card.Id, status, card.OccurrenceDate);
        await LoadAsync();
    }

    private async Task EditAsync(TaskCardVm card)
    {
        if (await _dialogs.EditTaskAsync(card.Id, card.Date, card.OccurrenceDate))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(TaskCardVm card)
    {
        if (card.IsRecurring)
        {
            var series = _dialogs.ConfirmSeries($"\"{card.Title}\" silinsin mi?");
            if (series is null) return;
            await _tasks.DeleteAsync(card.Id, series.Value, card.OccurrenceDate);
        }
        else
        {
            if (!_dialogs.Confirm($"\"{card.Title}\" silinsin mi?", "Görevi sil")) return;
            await _tasks.DeleteAsync(card.Id);
        }

        await LoadAsync();
    }

    private async Task SkipAsync(TaskCardVm card)
    {
        await _tasks.SkipOccurrenceAsync(card.Id, card.OccurrenceDate);
        await LoadAsync();
    }

    private async Task SnoozeAsync(TaskCardVm card, string preset)
    {
        await _tasks.SnoozeAsync(card.Id, SnoozePresets.Resolve(preset, DateTime.Now, new TimeOnly(18, 0)));
        await LoadAsync();
    }
}
