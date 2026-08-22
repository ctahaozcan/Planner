using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class TaskEditorViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly AttachmentService _attachments;
    private readonly IAppDialogs _dialogs;
    private Guid? _id;
    private DateOnly? _occurrenceDate;
    private readonly List<string> _pendingFiles = [];

    public TaskEditorViewModel(
        TaskService tasks,
        CategoryService categories,
        AttachmentService attachments,
        IAppDialogs dialogs)
    {
        _tasks = tasks;
        _categories = categories;
        _attachments = attachments;
        _dialogs = dialogs;
        Hours.Add("");
        for (var h = 0; h < 24; h++)
        {
            Hours.Add(h.ToString("00"));
        }

        Minutes.Add("");
        Minutes.Add("00");
        Minutes.Add("15");
        Minutes.Add("30");
        Minutes.Add("45");
        RecurrenceOptions.Add(new RecurrenceOption("Yok", RecurrenceKind.None));
        RecurrenceOptions.Add(new RecurrenceOption("Her gün", RecurrenceKind.Daily));
        RecurrenceOptions.Add(new RecurrenceOption("Haftalık", RecurrenceKind.Weekly));
        RecurrenceOptions.Add(new RecurrenceOption("Aylık (ayın günü)", RecurrenceKind.Monthly));
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<string> Hours { get; } = new();
    public ObservableCollection<string> Minutes { get; } = new();
    public ObservableCollection<StatusOption> Statuses { get; } = new()
    {
        new("Başlamadı", PlannerTaskStatus.Baslamadi),
        new("Devam Ediyor", PlannerTaskStatus.DevamEdiyor),
        new("Duraklatıldı", PlannerTaskStatus.Duraklatildi),
        new("Tamamlandı", PlannerTaskStatus.Tamamlandi)
    };
    public ObservableCollection<RecurrenceOption> RecurrenceOptions { get; } = new();
    public ObservableCollection<TaskAttachment> Attachments { get; } = new();

    public event Action<bool>? CloseRequested;

    [ObservableProperty] private string _windowTitle = "Yeni kayıt";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _hour = "";
    [ObservableProperty] private string _minute = "";
    [ObservableProperty] private bool _hasReminder;
    [ObservableProperty] private DateTime _reminderDate = DateTime.Today;
    [ObservableProperty] private string _reminderHour = "09";
    [ObservableProperty] private string _reminderMinute = "00";
    [ObservableProperty] private StatusOption? _selectedStatus;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isSpecialHint;
    [ObservableProperty] private RecurrenceOption? _selectedRecurrence;
    [ObservableProperty] private bool _wdMon = true;
    [ObservableProperty] private bool _wdTue = true;
    [ObservableProperty] private bool _wdWed = true;
    [ObservableProperty] private bool _wdThu = true;
    [ObservableProperty] private bool _wdFri = true;
    [ObservableProperty] private bool _wdSat;
    [ObservableProperty] private bool _wdSun;
    [ObservableProperty] private bool _hasEndDate;
    [ObservableProperty] private DateTime _endDate = DateTime.Today.AddMonths(3);
    [ObservableProperty] private bool _isRecurringEdit;
    [ObservableProperty] private bool _editEntireSeries = true;
    [ObservableProperty] private bool _canAttach;

    public bool ShowWeekdays => SelectedRecurrence?.Kind == RecurrenceKind.Weekly;

    partial void OnSelectedRecurrenceChanged(RecurrenceOption? value)
        => OnPropertyChanged(nameof(ShowWeekdays));

    public async Task LoadAsync(Guid? taskId, DateOnly? presetDate, DateOnly? occurrenceDate = null, TimeOnly? presetTime = null)
    {
        _pendingFiles.Clear();
        Attachments.Clear();
        var cats = await _categories.GetAllAsync();
        Categories.Clear();
        foreach (var c in cats)
        {
            Categories.Add(c);
        }

        SelectedStatus = Statuses[0];
        SelectedRecurrence = RecurrenceOptions[0];
        _occurrenceDate = occurrenceDate;
        if (taskId is { } id)
        {
            var task = await _tasks.GetAsync(id) ?? throw new InvalidOperationException("Kayıt bulunamadı.");
            _id = id;
            WindowTitle = "Kaydı düzenle";
            Title = task.Title;
            Notes = task.Notes ?? "";
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == task.CategoryId) ?? Categories.FirstOrDefault();
            Date = (occurrenceDate ?? task.Date).ToDateTime(TimeOnly.MinValue);
            Hour = task.Time?.Hour.ToString("00") ?? "";
            Minute = task.Time?.Minute.ToString("00") ?? "";
            HasReminder = task.ReminderAt is not null;
            if (task.ReminderAt is { } reminder)
            {
                ReminderDate = reminder.Date;
                ReminderHour = reminder.Hour.ToString("00");
                ReminderMinute = reminder.Minute.ToString("00");
            }

            SelectedStatus = Statuses.First(s => s.Value == task.Status);
            SelectedRecurrence = RecurrenceOptions.FirstOrDefault(r => r.Kind == task.RecurrenceKind) ?? RecurrenceOptions[0];
            ApplyWeekdays(task.RecurrenceWeekdays);
            HasEndDate = task.RecurrenceEndDate is not null;
            if (task.RecurrenceEndDate is { } end)
            {
                EndDate = end.ToDateTime(TimeOnly.MinValue);
            }

            IsRecurringEdit = task.IsRecurring;
            EditEntireSeries = true;
            CanAttach = true;
            foreach (var a in await _attachments.GetForTaskAsync(id))
            {
                Attachments.Add(a);
            }
        }
        else
        {
            _id = null;
            WindowTitle = "Yeni kayıt";
            Date = (presetDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            SelectedCategory = Categories.FirstOrDefault();
            ReminderDate = Date;
            CanAttach = false;
            IsRecurringEdit = false;
            if (presetTime is { } t)
            {
                Hour = t.Hour.ToString("00");
                Minute = t.Minute.ToString("00");
            }
        }

        UpdateHint();
    }

    partial void OnSelectedCategoryChanged(Category? value) => UpdateHint();

    private void UpdateHint()
        => IsSpecialHint = SelectedCategory is { Name: "Özel" or "Kişisel" };

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Başlık gerekli.";
            return;
        }

        if (SelectedCategory is null)
        {
            ErrorMessage = "Kategori seçin.";
            return;
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(Hour) && !string.IsNullOrWhiteSpace(Minute)
            && int.TryParse(Hour, out var h) && int.TryParse(Minute, out var m))
        {
            time = new TimeOnly(h, m);
        }

        DateTime? reminder = null;
        if (HasReminder
            && int.TryParse(ReminderHour, out var rh)
            && int.TryParse(ReminderMinute, out var rm))
        {
            reminder = DateOnly.FromDateTime(ReminderDate).ToDateTime(new TimeOnly(rh, rm));
        }

        var kind = SelectedRecurrence?.Kind ?? RecurrenceKind.None;
        var entity = new PlannerTask
        {
            Id = _id ?? Guid.NewGuid(),
            Title = Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            CategoryId = SelectedCategory.Id,
            Date = DateOnly.FromDateTime(Date),
            Time = time,
            ReminderAt = reminder,
            Status = SelectedStatus?.Value ?? PlannerTaskStatus.Baslamadi,
            RecurrenceKind = kind,
            RecurrenceWeekdays = kind == RecurrenceKind.Weekly ? PackWeekdays() : 0,
            RecurrenceMonthDay = kind == RecurrenceKind.Monthly ? DateOnly.FromDateTime(Date).Day : null,
            RecurrenceEndDate = HasEndDate ? DateOnly.FromDateTime(EndDate) : null
        };

        if (_id is null)
        {
            await _tasks.AddAsync(entity);
            foreach (var file in _pendingFiles)
            {
                try
                {
                    await _attachments.AddAsync(entity.Id, file);
                }
                catch (Exception ex)
                {
                    ErrorMessage = ex.Message;
                }
            }
        }
        else
        {
            await _tasks.UpdateAsync(entity, EditEntireSeries || !IsRecurringEdit, _occurrenceDate);
        }

        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private async Task SkipThisAsync()
    {
        if (_id is { } id && _occurrenceDate is { } occ)
        {
            await _tasks.SkipOccurrenceAsync(id, occ);
            CloseRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        var path = _dialogs.OpenAnyFile();
        if (path is null)
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > AttachmentService.MaxFileBytes)
        {
            _dialogs.Info("Dosya 20 MB sınırını aşıyor. Daha küçük bir dosya seçin.");
            return;
        }

        if (_id is { } id)
        {
            try
            {
                var row = await _attachments.AddAsync(id, path);
                Attachments.Add(row);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }
        else
        {
            _pendingFiles.Add(path);
            Attachments.Add(new TaskAttachment
            {
                Id = Guid.NewGuid(),
                OriginalName = Path.GetFileName(path),
                SizeBytes = info.Length,
                CreatedAt = DateTime.Now
            });
            CanAttach = true;
        }
    }

    [RelayCommand]
    private async Task RemoveAttachmentAsync(object? item)
    {
        if (item is not TaskAttachment att)
        {
            return;
        }

        if (_id is not null && !string.IsNullOrEmpty(att.StoredFileName))
        {
            await _attachments.DeleteAsync(att.Id);
        }
        else
        {
            _pendingFiles.RemoveAll(p => Path.GetFileName(p) == att.OriginalName);
        }

        Attachments.Remove(att);
    }

    [RelayCommand]
    private void OpenAttachment(object? item)
    {
        if (item is not TaskAttachment att)
        {
            return;
        }

        var path = string.IsNullOrEmpty(att.StoredFileName)
            ? _pendingFiles.FirstOrDefault(p => Path.GetFileName(p) == att.OriginalName)
            : _attachments.GetFullPath(att);
        if (path is null || !File.Exists(path))
        {
            _dialogs.Info("Dosya bulunamadı.");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private int PackWeekdays()
    {
        var mask = 0;
        if (WdMon) mask |= WeekdayBits.Monday;
        if (WdTue) mask |= WeekdayBits.Tuesday;
        if (WdWed) mask |= WeekdayBits.Wednesday;
        if (WdThu) mask |= WeekdayBits.Thursday;
        if (WdFri) mask |= WeekdayBits.Friday;
        if (WdSat) mask |= WeekdayBits.Saturday;
        if (WdSun) mask |= WeekdayBits.Sunday;
        return mask == 0 ? WeekdayBits.For(DateOnly.FromDateTime(Date).DayOfWeek) : mask;
    }

    private void ApplyWeekdays(int mask)
    {
        if (mask == 0)
        {
            mask = WeekdayBits.Weekdays;
        }

        WdMon = (mask & WeekdayBits.Monday) != 0;
        WdTue = (mask & WeekdayBits.Tuesday) != 0;
        WdWed = (mask & WeekdayBits.Wednesday) != 0;
        WdThu = (mask & WeekdayBits.Thursday) != 0;
        WdFri = (mask & WeekdayBits.Friday) != 0;
        WdSat = (mask & WeekdayBits.Saturday) != 0;
        WdSun = (mask & WeekdayBits.Sunday) != 0;
    }

    public sealed record StatusOption(string Name, PlannerTaskStatus Value)
    {
        public override string ToString() => Name;
    }

    public sealed record RecurrenceOption(string Name, RecurrenceKind Kind)
    {
        public override string ToString() => Name;
    }
}
