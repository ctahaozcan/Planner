using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class KanbanColumnVm : ObservableObject
{
    public KanbanColumnVm(PlannerTaskStatus status, string accentHex)
    {
        Status = status;
        Title = status.ToDisplay();
        AccentHex = accentHex;
    }

    public PlannerTaskStatus Status { get; }
    public string Title { get; }
    public string AccentHex { get; }
    public ObservableCollection<TaskCardVm> Items { get; } = new();

    [ObservableProperty] private bool _isDropTarget;

    public int Count => Items.Count;
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public void NotifyCount()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
    }
}

public partial class TodayViewModel : ObservableObject
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly TaskService _tasks;
    private readonly IAppDialogs _dialogs;
    private readonly TrayIconService _tray;
    private readonly PriorityService _priorities;
    private readonly DailyNoteService _notes;
    private readonly LeaveService _leaves;
    private CancellationTokenSource? _noteCts;
    private bool _persistOpening;

    public TodayViewModel(
        TaskService tasks,
        IAppDialogs dialogs,
        TrayIconService tray,
        PriorityService priorities,
        DailyNoteService notes,
        LeaveService leaves)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        _tray = tray;
        _priorities = priorities;
        _notes = notes;
        _leaves = leaves;
        Columns.Add(new KanbanColumnVm(PlannerTaskStatus.Baslamadi, "#94A3B8"));
        Columns.Add(new KanbanColumnVm(PlannerTaskStatus.DevamEdiyor, "#2563EB"));
        Columns.Add(new KanbanColumnVm(PlannerTaskStatus.Duraklatildi, "#D97706"));
        Columns.Add(new KanbanColumnVm(PlannerTaskStatus.Tamamlandi, "#059669"));
        SelectedDate = DateTime.Today;
    }

    [ObservableProperty] private DateTime _selectedDate;
    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isToday = true;
    [ObservableProperty] private string _dailyNote = "";
    [ObservableProperty] private bool _showDailyNote;

    public string NoteToggleText => ShowDailyNote ? "Gizle" : "Aç";

    public bool IsListView => !IsBoardView;
    [ObservableProperty] private bool _isBoardView = true;
    [ObservableProperty] private bool _hasLeaves;
    [ObservableProperty] private bool _hasOverdue;
    [ObservableProperty] private string _overdueText = "";

    public ObservableCollection<KanbanColumnVm> Columns { get; } = new();
    public ObservableCollection<LeaveBannerVm> Leaves { get; } = new();
    public ObservableCollection<TaskCardVm> Overdue { get; } = new();

    public TaskCardCallbacks Callbacks => new()
    {
        SetStatus = SetStatusAsync,
        Edit = EditAsync,
        Details = ShowDetailsAsync,
        Delete = DeleteAsync,
        Pin = PinAsync,
        Skip = SkipAsync,
        Snooze = SnoozeAsync
    };

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnIsBoardViewChanged(bool value) => OnPropertyChanged(nameof(IsListView));
    partial void OnShowDailyNoteChanged(bool value) => OnPropertyChanged(nameof(NoteToggleText));

    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.Date.AddDays(-1);

    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.Date.AddDays(1);

    [RelayCommand]
    private void GoToToday() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private void ShowBoard() => IsBoardView = true;

    [RelayCommand]
    private void ShowList() => IsBoardView = false;

    [RelayCommand]
    private void ToggleDailyNote() => ShowDailyNote = !ShowDailyNote;

    [RelayCommand]
    private async Task NewTaskAsync()
        => await CreateOnDayAsync(PlannerTaskStatus.Baslamadi);

    [RelayCommand]
    private async Task NewInColumnAsync(object? statusObj)
    {
        var status = statusObj is PlannerTaskStatus s
            ? s
            : statusObj is KanbanColumnVm col
                ? col.Status
                : PlannerTaskStatus.Baslamadi;
        await CreateOnDayAsync(status);
    }

    private async Task CreateOnDayAsync(PlannerTaskStatus status)
    {
        if (await _dialogs.EditTaskAsync(null, DateOnly.FromDateTime(SelectedDate), null, null, status))
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

        foreach (var column in Columns)
        {
            column.Items.Clear();
            column.IsDropTarget = false;
        }

        Leaves.Clear();
        var ctx = await _leaves.GetCountContextAsync();
        var dayLeaves = await _leaves.GetForDateAsync(date);
        foreach (var leave in dayLeaves)
        {
            Leaves.Add(LeaveBannerVm.From(leave, date, ctx));
        }

        HasLeaves = Leaves.Count > 0;

        foreach (var occ in items)
        {
            var column = ColumnFor(occ.Status);
            column.Items.Add(new TaskCardVm(occ, Callbacks, pins.Contains(occ.TaskId)));
        }

        foreach (var column in Columns)
        {
            column.NotifyCount();
        }

        Overdue.Clear();
        if (IsToday)
        {
            foreach (var t in await _tasks.GetOverdueAsync(date))
            {
                Overdue.Add(new TaskCardVm(t, Callbacks));
            }
        }

        HasOverdue = Overdue.Count > 0;
        OverdueText = Overdue.Count == 0
            ? ""
            : Overdue.Count == 1
                ? $"1 gecikmiş görev: {Overdue[0].Title}"
                : $"{Overdue.Count} gecikmiş görev";

        _persistOpening = true;
        DailyNote = await _notes.GetAsync(date);
        _persistOpening = false;

        var open = items.Count(t => t.Status != PlannerTaskStatus.Tamamlandi);
        var done = items.Count(t => t.Status == PlannerTaskStatus.Tamamlandi);
        var leavePrefix = dayLeaves.Count > 0
            ? string.Join(" · ", dayLeaves.Select(LeaveMath.BannerTitle)) + " · "
            : "";
        SummaryText = items.Count == 0 && dayLeaves.Count == 0
            ? "Bu gün için henüz görev yok."
            : $"{leavePrefix}{open} açık · {done} tamamlandı";
        IsEmpty = items.Count == 0 && dayLeaves.Count == 0;
        await _tray.RefreshTooltipAsync();
    }

    public KanbanColumnVm? ColumnForPoint(PlannerTaskStatus status) => ColumnFor(status);

    public async Task MoveCardAsync(TaskCardVm card, KanbanColumnVm target, int insertIndex)
    {
        var source = Columns.FirstOrDefault(c => c.Items.Contains(card));
        if (source is null)
        {
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, target.Items.Count);
        if (ReferenceEquals(source, target))
        {
            var old = source.Items.IndexOf(card);
            if (old < 0 || old == insertIndex || old + 1 == insertIndex)
            {
                return;
            }

            source.Items.RemoveAt(old);
            if (insertIndex > old)
            {
                insertIndex--;
            }

            source.Items.Insert(Math.Clamp(insertIndex, 0, source.Items.Count), card);
            source.NotifyCount();
        }
        else
        {
            source.Items.Remove(card);
            source.NotifyCount();
            insertIndex = Math.Clamp(insertIndex, 0, target.Items.Count);
            target.Items.Insert(insertIndex, card);
            card.Status = target.Status;
            target.NotifyCount();
            await _tasks.SetStatusAsync(card.Id, target.Status, card.OccurrenceDate);
        }

        await PersistVisibleOrderAsync();
        if (card.IsRecurring)
        {
            await LoadAsync();
        }
        else
        {
            RefreshSummary();
            await _tray.RefreshTooltipAsync();
        }
    }

    private async Task PersistVisibleOrderAsync()
    {
        var rows = new List<(Guid Id, PlannerTaskStatus Status, int SortOrder)>();
        foreach (var column in Columns)
        {
            var order = 0;
            foreach (var item in column.Items)
            {
                item.SortOrder = order;
                rows.Add((item.Id, column.Status, order));
                order++;
            }

            column.NotifyCount();
        }

        await _tasks.PersistBoardOrderAsync(DateOnly.FromDateTime(SelectedDate), rows);
    }

    private void RefreshSummary()
    {
        var open = Columns.Where(c => c.Status != PlannerTaskStatus.Tamamlandi).Sum(c => c.Count);
        var done = ColumnFor(PlannerTaskStatus.Tamamlandi).Count;
        var leavePrefix = Leaves.Count > 0
            ? string.Join(" · ", Leaves.Select(l => l.Title)) + " · "
            : "";
        var total = Columns.Sum(c => c.Count);
        IsEmpty = total == 0 && Leaves.Count == 0;
        SummaryText = total == 0 && Leaves.Count == 0
            ? "Bu gün için henüz görev yok."
            : $"{leavePrefix}{open} açık · {done} tamamlandı";
    }

    private KanbanColumnVm ColumnFor(PlannerTaskStatus status)
        => Columns.First(c => c.Status == status);

    partial void OnDailyNoteChanged(string value)
    {
        if (_persistOpening)
        {
            return;
        }

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

    private async Task ShowDetailsAsync(TaskCardVm card)
    {
        if (await _dialogs.ShowTaskDetailsAsync(card.Id, card.OccurrenceDate))
        {
            await EditAsync(card);
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
