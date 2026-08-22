using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.Core.Models;

namespace Planner.App.ViewModels;

public enum AppPage
{
    Today,
    Agenda,
    Week,
    Tasks,
    Habits,
    Leaves,
    Contacts,
    Settings
}

public sealed class TaskCardCallbacks
{
    public required Func<TaskCardVm, PlannerTaskStatus, Task> SetStatus { get; init; }
    public required Func<TaskCardVm, Task> Edit { get; init; }
    public required Func<TaskCardVm, Task> Delete { get; init; }
    public Func<TaskCardVm, Task>? Pin { get; init; }
    public Func<TaskCardVm, Task>? Skip { get; init; }
    public Func<TaskCardVm, string, Task>? Snooze { get; init; }
}

public partial class TaskCardVm : ObservableObject
{
    private readonly TaskCardCallbacks _cb;

    public TaskCardVm(PlannerTask task, TaskCardCallbacks callbacks, DateOnly? occurrenceDate = null, bool isPriority = false, bool completedOccurrence = false)
        : this(new TaskOccurrence
        {
            Task = task,
            Date = occurrenceDate ?? task.Date,
            IsVirtual = task.IsRecurring && occurrenceDate is not null && occurrenceDate != task.Date,
            IsCompletedOccurrence = completedOccurrence || task.Status == PlannerTaskStatus.Tamamlandi
        }, callbacks, isPriority)
    {
    }

    public TaskCardVm(TaskOccurrence occurrence, TaskCardCallbacks callbacks, bool isPriority = false)
    {
        _cb = callbacks;
        var task = occurrence.Task;
        Id = task.Id;
        Title = task.Title;
        Notes = task.Notes;
        CategoryName = task.Category?.Name ?? "";
        CategoryColor = task.Category?.ColorHex ?? "#0F766E";
        Status = occurrence.Status;
        Date = occurrence.Date;
        TimeText = task.Time?.ToString("HH\\:mm");
        HasReminder = task.ReminderAt is not null || task.Time is not null;
        IsQuickAdd = task.IsQuickAdd;
        DateText = occurrence.Date.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("tr-TR"));
        IsRecurring = task.IsRecurring;
        RecurrenceText = task.IsRecurring ? task.RecurrenceKind.ToDisplay() : "";
        IsPriority = isPriority;
        OccurrenceDate = occurrence.Date;
        CanSkip = task.IsRecurring;
        CanPin = callbacks.Pin is not null;
        CanSnooze = callbacks.Snooze is not null && HasReminder;
    }

    public Guid Id { get; }
    public DateOnly Date { get; }
    public DateOnly OccurrenceDate { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _categoryColor = "#0F766E";
    [ObservableProperty] private PlannerTaskStatus _status;
    [ObservableProperty] private string? _timeText;
    [ObservableProperty] private bool _hasReminder;
    [ObservableProperty] private bool _isQuickAdd;
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private bool _isRecurring;
    [ObservableProperty] private string _recurrenceText = "";
    [ObservableProperty] private bool _isPriority;
    [ObservableProperty] private bool _canSkip;
    [ObservableProperty] private bool _canPin;
    [ObservableProperty] private bool _canSnooze;

    public string StatusText => Status.ToDisplay();
    public bool IsCompleted => Status == PlannerTaskStatus.Tamamlandi;
    public bool IsNotStarted => Status == PlannerTaskStatus.Baslamadi;
    public bool IsInProgress => Status == PlannerTaskStatus.DevamEdiyor;
    public bool IsPaused => Status == PlannerTaskStatus.Duraklatildi;

    partial void OnStatusChanged(PlannerTaskStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsNotStarted));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsPaused));
    }

    [RelayCommand]
    private Task MarkNotStartedAsync() => _cb.SetStatus(this, PlannerTaskStatus.Baslamadi);

    [RelayCommand]
    private Task MarkInProgressAsync() => _cb.SetStatus(this, PlannerTaskStatus.DevamEdiyor);

    [RelayCommand]
    private Task MarkPausedAsync() => _cb.SetStatus(this, PlannerTaskStatus.Duraklatildi);

    [RelayCommand]
    private Task MarkDoneAsync() => _cb.SetStatus(this, PlannerTaskStatus.Tamamlandi);

    [RelayCommand]
    private Task EditAsync() => _cb.Edit(this);

    [RelayCommand]
    private Task DeleteAsync() => _cb.Delete(this);

    [RelayCommand]
    private Task PinAsync() => _cb.Pin?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private Task SkipAsync() => _cb.Skip?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private Task Snooze10Async() => _cb.Snooze?.Invoke(this, "10m") ?? Task.CompletedTask;

    [RelayCommand]
    private Task SnoozeHourAsync() => _cb.Snooze?.Invoke(this, "1h") ?? Task.CompletedTask;

    [RelayCommand]
    private Task SnoozeEveningAsync() => _cb.Snooze?.Invoke(this, "evening") ?? Task.CompletedTask;

    [RelayCommand]
    private Task SnoozeTomorrowAsync() => _cb.Snooze?.Invoke(this, "tomorrow") ?? Task.CompletedTask;
}

public sealed class TaskGroupVm
{
    public string Title { get; init; } = "";
    public System.Collections.ObjectModel.ObservableCollection<TaskCardVm> Items { get; } = new();
}

public sealed class DayAgendaVm
{
    public DateOnly Date { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool IsToday { get; init; }
    public System.Collections.ObjectModel.ObservableCollection<TaskCardVm> Items { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<LeaveBannerVm> Leaves { get; } = new();
    public bool HasLeaves => Leaves.Count > 0;
    public bool IsEmpty => Items.Count == 0 && Leaves.Count == 0;
}

public sealed class HourLaneVm
{
    public int Hour { get; init; }
    public string Label { get; init; } = "";
    public bool IsWorkBand { get; init; }
    public System.Collections.ObjectModel.ObservableCollection<TaskCardVm> Items { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<LeaveBannerVm> Leaves { get; } = new();
    public bool HasLeaves => Leaves.Count > 0;
}

public sealed class WeekDayVm
{
    public DateOnly Date { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool IsToday { get; init; }
    public System.Collections.ObjectModel.ObservableCollection<TaskCardVm> Items { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<LeaveBannerVm> Leaves { get; } = new();
    public bool HasLeaves => Leaves.Count > 0;
}
