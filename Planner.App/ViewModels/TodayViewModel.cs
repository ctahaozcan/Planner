using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Data;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TodayViewModel : ObservableObject
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private readonly TrayIconService _tray;
    private readonly PriorityService _priorities;
    private readonly HabitService _habits;
    private readonly DailyNoteService _notes;
    private readonly SettingsService _settings;
    private readonly BriefingService _briefing;
    private readonly LeaveService _leaves;
    private CancellationTokenSource? _noteCts;

    public TodayViewModel(
        TaskService tasks,
        IAppDialogs dialogs,
        TrayIconService tray,
        PriorityService priorities,
        HabitService habits,
        DailyNoteService notes,
        SettingsService settings,
        BriefingService briefing,
        LeaveService leaves)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        _tray = tray;
        _priorities = priorities;
        _habits = habits;
        _notes = notes;
        _settings = settings;
        _briefing = briefing;
        _leaves = leaves;
        SelectedDate = DateTime.Today;
    }

    [ObservableProperty] private DateTime _selectedDate;
    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isToday = true;
    [ObservableProperty] private string _dailyNote = "";
    [ObservableProperty] private string _briefingSummary = "";
    [ObservableProperty] private bool _hasBriefing;
    [ObservableProperty] private bool _priorityFull;
    [ObservableProperty] private bool _hasLeaves;
    [ObservableProperty] private bool _hasAllDayLeaves;

    public ObservableCollection<HourLaneVm> Hours { get; } = new();
    public ObservableCollection<TaskCardVm> Untimed { get; } = new();
    public ObservableCollection<TaskCardVm> Priorities { get; } = new();
    public ObservableCollection<HabitRowVm> Habits { get; } = new();
    public ObservableCollection<string> ContactEvents { get; } = new();
    public ObservableCollection<TaskCardVm> Overdue { get; } = new();
    public ObservableCollection<LeaveBannerVm> Leaves { get; } = new();

    public TaskCardCallbacks Callbacks => new()
    {
        SetStatus = SetStatusAsync,
        Edit = EditAsync,
        Delete = DeleteAsync,
        Pin = PinAsync,
        Skip = SkipAsync,
        Snooze = SnoozeAsync
    };

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.Date.AddDays(-1);

    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.Date.AddDays(1);

    [RelayCommand]
    private void GoToToday() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private async Task NewTaskAsync()
    {
        if (await _dialogs.EditTaskAsync(null, DateOnly.FromDateTime(SelectedDate)))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task NewAtHourAsync(object? hourObj)
    {
        var hour = hourObj is int i ? i : hourObj is string s && int.TryParse(s, out var h) ? h : 9;
        if (await _dialogs.EditTaskAsync(null, DateOnly.FromDateTime(SelectedDate), null, new TimeOnly(hour, 0)))
        {
            await LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        IsToday = SelectedDate.Date == DateTime.Today;
        var date = DateOnly.FromDateTime(SelectedDate);
        DateLabel = SelectedDate.ToString("dddd d MMMM yyyy", Tr);
        var items = await _tasks.GetOccurrencesForDateAsync(date);
        var pins = (await _priorities.GetAsync(date)).Select(p => p.TaskId).ToHashSet();
        PriorityFull = pins.Count >= 3;

        var dayStart = await _settings.GetTimeAsync(SettingKeys.DayViewStart, new TimeOnly(7, 0));
        var dayEnd = await _settings.GetTimeAsync(SettingKeys.DayViewEnd, new TimeOnly(22, 0));
        var workStart = await _settings.GetTimeAsync(SettingKeys.WorkBandStart, new TimeOnly(9, 0));
        var workEnd = await _settings.GetTimeAsync(SettingKeys.WorkBandEnd, new TimeOnly(18, 0));

        Hours.Clear();
        Untimed.Clear();
        Priorities.Clear();
        Leaves.Clear();
        var ctx = await _leaves.GetCountContextAsync();
        var dayLeaves = await _leaves.GetForDateAsync(date);
        foreach (var leave in dayLeaves.Where(l => l.DurationKind != LeaveDurationKind.Hourly && !LeaveMath.IsLedgerKind(LeaveMath.ResolveKind(l))))
        {
            Leaves.Add(LeaveBannerVm.From(leave, date, ctx));
        }

        HasAllDayLeaves = Leaves.Count > 0;
        HasLeaves = Leaves.Count > 0 || dayLeaves.Any(l => l.DurationKind == LeaveDurationKind.Hourly);
        for (var h = dayStart.Hour; h < Math.Max(dayEnd.Hour, dayStart.Hour + 1); h++)
        {
            var lane = new HourLaneVm
            {
                Hour = h,
                Label = $"{h:00}:00",
                IsWorkBand = h >= workStart.Hour && h < workEnd.Hour
            };
            foreach (var occ in items.Where(o => o.Task.Time?.Hour == h))
            {
                lane.Items.Add(new TaskCardVm(occ, Callbacks, pins.Contains(occ.TaskId)));
            }

            foreach (var leave in dayLeaves.Where(l => LeaveMath.CoversHour(l, date, h)))
            {
                lane.Leaves.Add(LeaveBannerVm.From(leave, date, ctx));
            }

            Hours.Add(lane);
        }

        foreach (var occ in items.Where(o => o.Task.Time is null))
        {
            Untimed.Add(new TaskCardVm(occ, Callbacks, pins.Contains(occ.TaskId)));
        }

        foreach (var occ in items.Where(o => pins.Contains(o.TaskId)).OrderBy(o => o.Task.Time == null).ThenBy(o => o.Task.Time))
        {
            Priorities.Add(new TaskCardVm(occ, Callbacks, true));
        }

        var habits = await _habits.GetSnapshotsAsync(date);
        Habits.Clear();
        foreach (var h in habits.Where(x => x.IsDueToday || date != DateOnly.FromDateTime(DateTime.Today)))
        {
            Habits.Add(new HabitRowVm(h, ToggleHabitAsync));
        }

        ContactEvents.Clear();
        foreach (var ev in await _briefing.GetContactEventsAsync(date))
        {
            ContactEvents.Add(ev);
        }

        Overdue.Clear();
        if (IsToday)
        {
            foreach (var t in await _tasks.GetOverdueAsync(date))
            {
                Overdue.Add(new TaskCardVm(t, Callbacks));
            }
        }

        DailyNote = await _notes.GetAsync(date);

        var open = items.Count(t => t.Status != PlannerTaskStatus.Tamamlandi);
        var done = items.Count(t => t.Status == PlannerTaskStatus.Tamamlandi);
        var leavePrefix = dayLeaves.Count > 0
            ? string.Join(" · ", dayLeaves.Select(LeaveMath.BannerTitle)) + " · "
            : "";
        SummaryText = items.Count == 0
            ? (dayLeaves.Count > 0 ? leavePrefix.Trim(' ', '·') : "Bu gün için henüz kayıt yok.")
            : $"{leavePrefix}{open} açık · {done} tamamlandı · {Priorities.Count} öncelik";
        IsEmpty = items.Count == 0 && dayLeaves.Count == 0;
        HasBriefing = ContactEvents.Count > 0 || Overdue.Count > 0 || Priorities.Count > 0 || dayLeaves.Count > 0;
        BriefingSummary = HasBriefing
            ? $"{Overdue.Count} gecikmiş · {ContactEvents.Count} kişi günü · {Priorities.Count} öncelik"
            : "";
        await _tray.RefreshTooltipAsync();
    }

    partial void OnDailyNoteChanged(string value)
    {
        _noteCts?.Cancel();
        _noteCts = new CancellationTokenSource();
        var token = _noteCts.Token;
        var date = DateOnly.FromDateTime(SelectedDate);
        var text = value;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token);
                await _notes.SaveAsync(date, text, token);
            }
            catch (OperationCanceledException)
            {
                // debounce
            }
        }, token);
    }

    private async Task ToggleHabitAsync(HabitRowVm row)
    {
        await _habits.ToggleTodayAsync(row.Id, DateOnly.FromDateTime(SelectedDate));
        await LoadAsync();
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
            if (series is null)
            {
                return;
            }

            await _tasks.DeleteAsync(card.Id, series.Value, card.OccurrenceDate);
        }
        else
        {
            if (!_dialogs.Confirm($"\"{card.Title}\" silinsin mi?", "Görevi sil"))
            {
                return;
            }

            await _tasks.DeleteAsync(card.Id);
        }

        await LoadAsync();
    }

    private async Task PinAsync(TaskCardVm card)
    {
        var date = DateOnly.FromDateTime(SelectedDate);
        if (card.IsPriority)
        {
            await _priorities.UnpinAsync(date, card.Id);
        }
        else
        {
            var ok = await _priorities.PinAsync(date, card.Id);
            if (!ok)
            {
                _dialogs.Info("Bugün için en fazla 3 öncelik seçebilirsiniz.");
            }
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
        var until = SnoozePresets.Resolve(preset, DateTime.Now, new TimeOnly(18, 0));
        await _tasks.SnoozeAsync(card.Id, until);
        await LoadAsync();
    }
}

public partial class HabitRowVm : ObservableObject
{
    private readonly Func<HabitRowVm, Task> _toggle;

    public HabitRowVm(HabitSnapshot snap, Func<HabitRowVm, Task> toggle)
    {
        _toggle = toggle;
        Id = snap.Habit.Id;
        Name = snap.Habit.Name;
        Streak = snap.Streak;
        IsCompleted = snap.IsCompletedToday;
        ReminderText = snap.Habit.ReminderTime?.ToString("HH\\:mm");
    }

    public Guid Id { get; }
    public string Name { get; }
    public int Streak { get; }
    public string? ReminderText { get; }

    [ObservableProperty] private bool _isCompleted;

    public string StreakText => Streak > 0 ? $"{Streak} gün seri" : "Seri yok";

    [RelayCommand]
    private Task ToggleAsync() => _toggle(this);
}
