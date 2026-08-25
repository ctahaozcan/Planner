using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TaskDetailViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly AttachmentService _attachments;
    private readonly IAppDialogs _dialogs;
    private Guid _taskId;
    private DateOnly _occurrenceDate;

    public TaskDetailViewModel(TaskService tasks, AttachmentService attachments, IAppDialogs dialogs)
    {
        _tasks = tasks;
        _attachments = attachments;
        _dialogs = dialogs;
    }

    public ObservableCollection<TaskAttachment> Attachments { get; } = new();
    public event Action<bool>? CloseRequested;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _reminderText = "";
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _categoryColor = "#0F766E";
    [ObservableProperty] private string _recurrenceText = "";
    [ObservableProperty] private bool _hasAttachments;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasTime;
    [ObservableProperty] private bool _hasReminder;
    [ObservableProperty] private bool _isRecurring;

    public async Task LoadAsync(Guid taskId, DateOnly? occurrenceDate)
    {
        var task = await _tasks.GetAsync(taskId) ?? throw new InvalidOperationException("Kayıt bulunamadı.");
        _taskId = taskId;
        _occurrenceDate = occurrenceDate ?? task.Date;
        var tr = new System.Globalization.CultureInfo("tr-TR");
        Title = task.Title;
        Notes = task.Notes ?? "";
        HasNotes = !string.IsNullOrWhiteSpace(task.Notes);
        StatusText = task.Status.ToDisplay();
        DateText = _occurrenceDate.ToString("d MMMM yyyy dddd", tr);
        TimeText = task.Time?.ToString("HH\\:mm") ?? "";
        HasTime = task.Time is not null;
        HasReminder = task.ReminderAt is not null;
        ReminderText = task.ReminderAt?.ToString("d MMMM yyyy HH:mm", tr) ?? "";
        CategoryName = task.Category?.Name ?? "";
        CategoryColor = task.Category?.ColorHex ?? "#0F766E";
        IsRecurring = task.IsRecurring;
        RecurrenceText = task.IsRecurring ? task.RecurrenceKind.ToDisplay() : "";

        Attachments.Clear();
        foreach (var a in await _attachments.GetForTaskAsync(taskId))
        {
            Attachments.Add(a);
        }

        HasAttachments = Attachments.Count > 0;
    }

    [RelayCommand]
    private void OpenAttachment(object? item)
    {
        if (item is not TaskAttachment att)
        {
            return;
        }

        if (!_attachments.TryOpen(att, out var error))
        {
            _dialogs.Info(error, "Ek açılamadı");
        }
    }

    [RelayCommand]
    private void Edit() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(false);
}
