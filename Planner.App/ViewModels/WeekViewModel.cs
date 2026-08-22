using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class WeekViewModel : ObservableObject
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private readonly LeaveService _leaves;

    public WeekViewModel(TaskService tasks, IAppDialogs dialogs, LeaveService leaves)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        _leaves = leaves;
        _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
    }

    private DateOnly _weekStart;

    [ObservableProperty] private string _rangeLabel = "";

    public ObservableCollection<WeekDayVm> Days { get; } = new();

    public event Action<DateOnly>? OpenDayRequested;

    public TaskCardCallbacks Callbacks => new()
    {
        SetStatus = SetStatusAsync,
        Edit = EditAsync,
        Delete = DeleteAsync,
        Skip = SkipAsync
    };

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ThisWeekAsync()
    {
        _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenDay(object? dateObj)
    {
        if (dateObj is DateOnly d)
        {
            OpenDayRequested?.Invoke(d);
        }
        else if (dateObj is WeekDayVm vm)
        {
            OpenDayRequested?.Invoke(vm.Date);
        }
    }

    public async Task LoadAsync()
    {
        var from = _weekStart;
        var to = from.AddDays(6);
        RangeLabel = $"{from.ToString("d MMMM", Tr)} – {to.ToString("d MMMM yyyy", Tr)}";
        var items = await _tasks.GetOccurrencesRangeAsync(from, to);
        var lookup = items.ToLookup(t => t.Date);
        var leaves = await _leaves.GetRangeAsync(from, to);
        var ctx = await _leaves.GetCountContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        Days.Clear();
        for (var i = 0; i < 7; i++)
        {
            var date = from.AddDays(i);
            var day = new WeekDayVm
            {
                Date = date,
                Title = date.ToString("ddd", Tr),
                Subtitle = date.ToString("d MMM", Tr),
                IsToday = date == today
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
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.AddDays(-delta);
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
}
