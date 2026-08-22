using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.ViewModels;

public partial class HabitsViewModel : ObservableObject
{
    private readonly HabitService _habits;
    private readonly IAppDialogs _dialogs;

    public HabitsViewModel(HabitService habits, IAppDialogs dialogs)
    {
        _habits = habits;
        _dialogs = dialogs;
    }

    public ObservableCollection<HabitRowVm> Items { get; } = new();

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private bool _weekdaysOnly;
    [ObservableProperty] private bool _hasReminder;
    [ObservableProperty] private string _reminderHour = "08";
    [ObservableProperty] private string _reminderMinute = "00";
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<string> Hours { get; } = new(Enumerable.Range(0, 24).Select(h => h.ToString("00")));
    public ObservableCollection<string> Minutes { get; } = new() { "00", "15", "30", "45" };

    public async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var snaps = await _habits.GetSnapshotsAsync(today);
        Items.Clear();
        foreach (var s in snaps)
        {
            Items.Add(new HabitRowVm(s, ToggleAsync));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        TimeOnly? reminder = null;
        if (HasReminder && int.TryParse(ReminderHour, out var h) && int.TryParse(ReminderMinute, out var m))
        {
            reminder = new TimeOnly(h, m);
        }

        try
        {
            await _habits.AddAsync(
                NewName,
                WeekdaysOnly ? HabitScheduleKind.Weekdays : HabitScheduleKind.Daily,
                reminder);
            NewName = "";
            StatusMessage = "Alışkanlık eklendi.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Info(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(object? rowObj)
    {
        if (rowObj is not HabitRowVm row)
        {
            return;
        }

        if (!_dialogs.Confirm($"\"{row.Name}\" silinsin mi?", "Alışkanlığı sil"))
        {
            return;
        }

        await _habits.DeleteAsync(row.Id);
        await LoadAsync();
    }

    private async Task ToggleAsync(HabitRowVm row)
    {
        await _habits.ToggleTodayAsync(row.Id, DateOnly.FromDateTime(DateTime.Today));
        await LoadAsync();
    }
}
